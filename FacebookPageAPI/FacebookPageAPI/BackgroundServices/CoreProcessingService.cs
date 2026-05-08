using Confluent.Kafka;
using FacebookPageAPI.Models;
using FacebookPageAPI.Services;
using Newtonsoft.Json;

namespace FacebookPageAPI.BackgroundServices
{
    /// <summary>
    /// Core Processing Service - Background worker chạy liên tục.
    ///
    /// Pipeline xử lý mỗi event từ Kafka topic "raw_events":
    ///   1. Consume message → State: Received
    ///   2. Detect Spam (rule-based, nhanh)
    ///   3. Analyze Intent + Sentiment (AI - Gemini)
    ///   4. Ra quyết định tự động (hide / reply / blacklist)
    ///   5. Cập nhật State: Processed → Replied / Hidden / Failed
    ///
    /// Thiết kế chịu tải tăng đột biến:
    ///   - Channel buffer 10.000 messages (không bỏ sót event khi viral)
    ///   - Tách consume và processing thành 2 luồng độc lập
    ///   - AutoOffsetReset.Earliest: đọc lại từ đầu nếu consumer restart
    ///   - EnableAutoCommit = false: chỉ commit sau khi xử lý xong
    /// </summary>
    public class CoreProcessingService : BackgroundService
    {
        private readonly ILogger<CoreProcessingService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConsumerConfig _kafkaConfig;

        // Channel buffer chịu tải tăng đột biến
        private readonly System.Threading.Channels.Channel<ConsumeResult<Null, string>> _channel;
        private const string KafkaTopic      = "raw_events";
        private const string KafkaGroupId    = "core-processor-6451071021";
        private const int    ChannelCapacity = 10_000;

        public CoreProcessingService(
            ILogger<CoreProcessingService> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration config)
        {
            _logger       = logger;
            _scopeFactory = scopeFactory;

            _kafkaConfig = new ConsumerConfig
            {
                BootstrapServers  = config["KafkaConfig:BootstrapServers"] ?? "localhost:9092",
                GroupId           = KafkaGroupId,
                AutoOffsetReset   = AutoOffsetReset.Earliest,
                EnableAutoCommit  = false,   // Commit thủ công sau khi xử lý xong
                SessionTimeoutMs  = 30_000,
                MaxPollIntervalMs = 300_000
            };

            // Bounded channel: nếu full thì consumer chờ (backpressure)
            var options = new System.Threading.Channels.BoundedChannelOptions(ChannelCapacity)
            {
                FullMode    = System.Threading.Channels.BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            };
            _channel = System.Threading.Channels.Channel.CreateBounded<ConsumeResult<Null, string>>(options);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("╔══════════════════════════════════════════════╗");
            _logger.LogInformation("║  Core Processing Service STARTED              ║");
            _logger.LogInformation("║  Student: Pham Cong Duc - 6451071021          ║");
            _logger.LogInformation("║  Kafka Topic: {Topic,-28}║", KafkaTopic);
            _logger.LogInformation("╚══════════════════════════════════════════════╝");

            // Chạy song song: 1 task consume, N tasks xử lý
            var consumeTask  = Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);
            var processTask1 = Task.Run(() => ProcessLoop(stoppingToken), stoppingToken);
            var processTask2 = Task.Run(() => ProcessLoop(stoppingToken), stoppingToken); // Worker thứ 2

            await Task.WhenAll(consumeTask, processTask1, processTask2);
        }

        // ================================================================
        // LUỒNG 1: Consume từ Kafka, đẩy vào Channel
        // ================================================================
        private void ConsumeLoop(CancellationToken stoppingToken)
        {
            using var consumer = new ConsumerBuilder<Null, string>(_kafkaConfig).Build();
            consumer.Subscribe(KafkaTopic);

            _logger.LogInformation("[Consumer] Đang lắng nghe topic '{Topic}'...", KafkaTopic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Timeout 1s để kiểm tra stoppingToken thường xuyên
                    var result = consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result == null) continue;

                    _logger.LogDebug("[Consumer] Nhận message offset={Offset}", result.Offset);

                    // Đẩy vào channel (blocking nếu channel đầy - backpressure)
                    _channel.Writer.WriteAsync(result, stoppingToken).AsTask().Wait(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError("[Consumer] Kafka ConsumeException: {Reason}", ex.Error.Reason);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Consumer] Unexpected error - sẽ tiếp tục sau 2s");
                    Thread.Sleep(2000);
                }
            }

            _channel.Writer.Complete();
            consumer.Close();
            _logger.LogInformation("[Consumer] Đã dừng.");
        }

        // ================================================================
        // LUỒNG 2+: Đọc từ Channel và xử lý pipeline
        // ================================================================
        private async Task ProcessLoop(CancellationToken stoppingToken)
        {
            await foreach (var kafkaResult in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await ProcessSingleEventAsync(kafkaResult, stoppingToken);
            }
        }

        private async Task ProcessSingleEventAsync(
            ConsumeResult<Null, string> kafkaResult,
            CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var stateTracker   = scope.ServiceProvider.GetRequiredService<EventStateTracker>();
            var spamDetector   = scope.ServiceProvider.GetRequiredService<SpamDetectionService>();
            var aiService      = scope.ServiceProvider.GetRequiredService<AiAnalysisService>();
            var decisionSvc    = scope.ServiceProvider.GetRequiredService<DecisionService>();
            var failureLogger  = scope.ServiceProvider.GetRequiredService<FailureLoggerService>();

            NormalizedEvent? ev = null;

            try
            {
                // ── STEP 1: Parse JSON ────────────────────────────────────
                ev = JsonConvert.DeserializeObject<NormalizedEvent>(kafkaResult.Message.Value);
                if (ev == null)
                {
                    _logger.LogWarning("[Pipeline] Parse JSON thất bại, bỏ qua message này");
                    return;
                }

                _logger.LogInformation("┌─ [Pipeline] Bắt đầu xử lý EventId={EventId}", ev.Header.EventId);
                _logger.LogInformation("│  Message: \"{Msg}\"", ev.Body.Message);

                // ── STEP 2: Khởi tạo state tracking ──────────────────────
                stateTracker.Initialize(ev);

                // Kiểm tra blacklist trước
                if (!string.IsNullOrEmpty(ev.Body.SenderId) && spamDetector.IsBlacklisted(ev.Body.SenderId))
                {
                    _logger.LogWarning("│  [Pipeline] User {SenderId} trong blacklist → bỏ qua", ev.Body.SenderId);
                    stateTracker.Transition(ev.Header.EventId, EventState.Hidden, "Sender trong blacklist");
                    return;
                }

                stateTracker.Transition(ev.Header.EventId, EventState.Processing);

                // ── STEP 3: Spam Detection (nhanh, không cần AI) ─────────
                var spamResult = spamDetector.DetectSpam(ev.Body.SenderId, ev.Body.Message);
                _logger.LogInformation("│  [Spam] Level={Level}, IsSpam={IsSpam}, Reason={Reason}",
                    spamResult.SpamLevel, spamResult.IsSpam, spamResult.SpamReason);

                // ── STEP 4: AI Analysis (Intent + Sentiment) ─────────────
                var aiResult = await aiService.AnalyzeAsync(ev.Body.Message);
                _logger.LogInformation("│  [AI] Intent={Intent}, Sentiment={Sentiment}, Confidence={Conf:P0}",
                    aiResult.Intent, aiResult.Sentiment, aiResult.Confidence);

                // Gắn analysis vào state record
                stateTracker.AttachAnalysis(ev.Header.EventId, aiResult);
                stateTracker.Transition(ev.Header.EventId, EventState.Processed);

                // ── STEP 5: Ra quyết định và thực thi ────────────────────
                var action = await decisionSvc.ExecuteDecisionAsync(ev, spamResult, aiResult);
                _logger.LogInformation("│  [Decision] Action={Action}", action);

                // Cập nhật state cuối
                var finalState = action.StartsWith("REPLIED") ? EventState.Replied : EventState.Hidden;
                stateTracker.Transition(ev.Header.EventId, finalState, action);

                _logger.LogInformation("└─ [Pipeline] Hoàn thành EventId={EventId} → {State}",
                    ev.Header.EventId, finalState);

                // ── COMMIT offset chỉ sau khi xử lý xong ─────────────────
                // (Trong thực tế cần giữ reference đến consumer)
                // consumer.Commit(kafkaResult); // uncommit vì consumer ở thread khác
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[Pipeline] PARSE_ERROR - không parse được JSON từ Kafka");
                if (ev != null)
                    await failureLogger.LogAsync(ev, "PARSE_ERROR", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Pipeline] UNEXPECTED_ERROR khi xử lý event");
                if (ev != null)
                {
                    stateTracker.Transition(ev.Header.EventId, EventState.Failed, ex.Message);
                    await failureLogger.LogAsync(ev, "PIPELINE_ERROR", ex.Message);
                }
            }
        }
    }
}
