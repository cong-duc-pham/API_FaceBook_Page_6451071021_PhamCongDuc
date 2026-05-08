using FacebookPageAPI.Models;
using Newtonsoft.Json;

namespace FacebookPageAPI.Services
{
    /// <summary>
    /// Ra quyết định tự động dựa trên kết quả phân tích:
    ///   - Spam nhẹ (Mild)      → Ẩn bình luận ngay
    ///   - Spam lặp (Repeated)  → Ẩn + đưa vào blacklist nội bộ (đã xử lý trong SpamDetectionService)
    ///   - Spam độc hại / scam  → Ẩn ngay + đẩy sang hàng chờ review thủ công
    ///   - Không spam           → Gửi auto-reply phù hợp theo intent
    /// </summary>
    public class DecisionService
    {
        private readonly ILogger<DecisionService> _logger;
        private readonly FailureLoggerService _failureLogger;
        private readonly HttpClient _httpClient;
        private readonly string _pageAccessToken;

        // Hàng chờ review thủ công (in-memory, trong production dùng DB/Queue)
        private static readonly List<ManualReviewItem> _manualReviewQueue = new();
        private static readonly object _lock = new();

        public DecisionService(
            ILogger<DecisionService> logger,
            FailureLoggerService failureLogger,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _logger = logger;
            _failureLogger = failureLogger;
            _httpClient = httpClientFactory.CreateClient("facebook");
            _pageAccessToken = config["FacebookConfig:AccessToken"] ?? string.Empty;
        }

        /// <summary>
        /// Thực thi quyết định tự động dựa trên kết quả spam detection + AI.
        /// Trả về hành động đã thực hiện.
        /// </summary>
        public async Task<string> ExecuteDecisionAsync(
            NormalizedEvent ev,
            AiAnalysisResult spamResult,
            AiAnalysisResult aiResult)
        {
            var commentId = ev.Body.CommentId;
            var message   = ev.Body.Message;
            var senderId  = ev.Body.SenderId;

            // === CASE 1: Spam độc hại / scam / bot rõ ràng ===
            if (spamResult.SpamLevel == SpamLevel.Malicious)
            {
                _logger.LogWarning("[Decision] MALICIOUS SPAM → Ẩn ngay + đưa vào hàng review thủ công. CommentId={Id}", commentId);
                await HideCommentAsync(ev, "Nội dung độc hại / scam");
                PushToManualReviewQueue(ev, spamResult, "Scam/Bot - cần review thủ công");
                return "HIDDEN_MALICIOUS_SPAM";
            }

            // === CASE 2: Spam lặp lại >= 3 lần / 24h ===
            if (spamResult.SpamLevel == SpamLevel.Repeated)
            {
                _logger.LogWarning("[Decision] REPEATED SPAM → Ẩn + đã blacklist {SenderId}", senderId);
                await HideCommentAsync(ev, "Lặp lại nội dung spam");
                // Blacklist đã được xử lý trong SpamDetectionService.DetectSpam()
                return "HIDDEN_BLACKLISTED";
            }

            // === CASE 3: Spam nhẹ (có link) ===
            if (spamResult.SpamLevel == SpamLevel.Mild)
            {
                _logger.LogInformation("[Decision] MILD SPAM → Ẩn bình luận. CommentId={Id}", commentId);
                await HideCommentAsync(ev, "Chứa link đáng ngờ");
                return "HIDDEN_MILD_SPAM";
            }

            // === CASE 4: Không phải spam → Auto-reply theo intent ===
            var replyMessage = BuildAutoReply(aiResult.Intent, aiResult.Sentiment, ev.Body.SenderName);
            await SendReplyAsync(ev, replyMessage);
            return $"REPLIED_INTENT_{aiResult.Intent.ToUpper()}";
        }

        // --------------------------------------------------------
        // Ẩn comment qua Facebook Graph API
        // --------------------------------------------------------
        private async Task HideCommentAsync(NormalizedEvent ev, string reason)
        {
            var commentId = ev.Body.CommentId;
            if (string.IsNullOrEmpty(commentId)) return;

            try
            {
                var url = $"https://graph.facebook.com/v21.0/{commentId}";
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "is_hidden", "true" },
                    { "access_token", _pageAccessToken }
                });

                var response = await _httpClient.PostAsync(url, content);
                var resultText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Facebook] Đã ẩn comment {CommentId}. Lý do: {Reason}", commentId, reason);
                }
                else
                {
                    _logger.LogError("[Facebook] Không thể ẩn comment {CommentId}: {Result}", commentId, resultText);
                    await _failureLogger.LogAsync(ev, "FACEBOOK_API_ERROR", $"Hide comment failed: {resultText}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Facebook] Exception khi ẩn comment {CommentId}", commentId);
                await _failureLogger.LogAsync(ev, "FACEBOOK_API_EXCEPTION", ex.Message);
            }
        }

        // --------------------------------------------------------
        // Gửi auto-reply comment
        // --------------------------------------------------------
        private async Task SendReplyAsync(NormalizedEvent ev, string replyMessage)
        {
            var commentId = ev.Body.CommentId;
            if (string.IsNullOrEmpty(commentId))
            {
                _logger.LogWarning("[Decision] Không có CommentId → bỏ qua auto-reply");
                return;
            }

            try
            {
                var url = $"https://graph.facebook.com/v21.0/{commentId}/comments";
                var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "message", replyMessage },
                    { "access_token", _pageAccessToken }
                });

                var response = await _httpClient.PostAsync(url, content);
                var resultText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[Facebook] Auto-reply thành công cho comment {CommentId}", commentId);
                }
                else
                {
                    _logger.LogError("[Facebook] Auto-reply thất bại {CommentId}: {Result}", commentId, resultText);
                    await _failureLogger.LogAsync(ev, "SEND_FAILED", $"Auto-reply failed: {resultText}", requiresManualReview: false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Facebook] Exception khi reply comment {CommentId}", commentId);
                await _failureLogger.LogAsync(ev, "SEND_EXCEPTION", ex.Message);
            }
        }

        // --------------------------------------------------------
        // Tạo nội dung auto-reply theo intent
        // --------------------------------------------------------
        private static string BuildAutoReply(string intent, string sentiment, string? senderName)
        {
            var name = !string.IsNullOrEmpty(senderName) ? $"{senderName} " : "";

            return intent switch
            {
                "hoi_gia" => $"Cảm ơn {name}đã quan tâm! Để biết giá chi tiết, bạn vui lòng nhắn tin cho chúng tôi hoặc gọi hotline nhé 😊",
                "khieu_nai" => $"Xin lỗi {name}vì trải nghiệm chưa tốt! Chúng tôi sẽ liên hệ lại với bạn trong thời gian sớm nhất để hỗ trợ.",
                "khen" => $"Cảm ơn {name}rất nhiều! Sự ủng hộ của bạn là động lực lớn cho chúng tôi ❤️",
                "hoi_thong_tin" => $"Cảm ơn {name}đã hỏi! Bạn có thể nhắn tin trực tiếp cho page để được tư vấn chi tiết nhé 😊",
                "chat_chat" => $"Cảm ơn {name}đã ghé thăm! Hẹn gặp lại bạn 🙌",
                _ => sentiment == "negative"
                    ? $"Cảm ơn {name}đã phản hồi! Chúng tôi sẽ xem xét và cải thiện ngay."
                    : $"Cảm ơn {name}đã bình luận! Rất vui được giao lưu cùng bạn 😊"
            };
        }

        // --------------------------------------------------------
        // Đưa vào hàng chờ review thủ công
        // --------------------------------------------------------
        private void PushToManualReviewQueue(NormalizedEvent ev, AiAnalysisResult spamResult, string reason)
        {
            lock (_lock)
            {
                _manualReviewQueue.Add(new ManualReviewItem
                {
                    EventId   = ev.Header.EventId,
                    CommentId = ev.Body.CommentId ?? string.Empty,
                    SenderId  = ev.Body.SenderId,
                    Message   = ev.Body.Message,
                    Reason    = reason,
                    SpamLevel = spamResult.SpamLevel.ToString(),
                    CreatedAt = DateTime.UtcNow
                });
            }
            _logger.LogWarning("[ManualReview] Đã đẩy event {EventId} vào hàng review thủ công. Lý do: {Reason}",
                ev.Header.EventId, reason);
        }

        /// <summary>Lấy danh sách hàng chờ review thủ công</summary>
        public List<ManualReviewItem> GetManualReviewQueue()
        {
            lock (_lock) { return new List<ManualReviewItem>(_manualReviewQueue); }
        }
    }

    public class ManualReviewItem
    {
        public string EventId   { get; set; } = string.Empty;
        public string CommentId { get; set; } = string.Empty;
        public string? SenderId { get; set; }
        public string? Message  { get; set; }
        public string Reason    { get; set; } = string.Empty;
        public string SpamLevel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
