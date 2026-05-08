using FacebookPageAPI.Models;
using Newtonsoft.Json;

namespace FacebookPageAPI.Services
{
    /// <summary>
    /// Phân tích Intent + Sentiment bằng Gemini AI (Google).
    /// 
    /// Gọi Gemini API với prompt ngắn gọn, kết quả trả về JSON.
    /// Retry tối đa 2 lần nếu timeout hoặc JSON parse lỗi.
    /// 
    /// Thay API_KEY trong appsettings.json:
    ///   "GeminiConfig": { "ApiKey": "YOUR_KEY" }
    /// </summary>
    public class AiAnalysisService
    {
        private readonly ILogger<AiAnalysisService> _logger;
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const int MaxRetries = 2;
        private const int TimeoutSeconds = 10;

        public AiAnalysisService(
            ILogger<AiAnalysisService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration config)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("gemini");
            _apiKey = config["GeminiConfig:ApiKey"] ?? string.Empty;
        }

        /// <summary>
        /// Phân tích một comment để xác định Intent và Sentiment.
        /// Trả về AiAnalysisResult dù AI lỗi (fallback an toàn).
        /// </summary>
        public async Task<AiAnalysisResult> AnalyzeAsync(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new AiAnalysisResult { Intent = "empty", Sentiment = "neutral" };

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_KEY")
            {
                _logger.LogWarning("[AI] Chưa cấu hình GeminiConfig:ApiKey → dùng fallback");
                return FallbackAnalysis(message);
            }

            for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
            {
                try
                {
                    return await CallGeminiAsync(message);
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("[AI] Gemini timeout (lần {Attempt}/{Max})", attempt, MaxRetries + 1);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[AI] Gemini lỗi lần {Attempt}: {Msg}", attempt, ex.Message);
                }

                if (attempt <= MaxRetries)
                    await Task.Delay(500 * attempt); // Back-off: 500ms, 1000ms
            }

            _logger.LogError("[AI] Gemini thất bại sau {Max} lần thử → dùng fallback", MaxRetries + 1);
            return FallbackAnalysis(message);
        }

        // --------------------------------------------------------
        // Gọi Gemini API thực sự
        // --------------------------------------------------------
        private async Task<AiAnalysisResult> CallGeminiAsync(string message)
        {
            var prompt = "Phân tích bình luận mạng xã hội sau và trả về JSON hợp lệ (không kèm markdown):\n\n" +
                         $"Bình luận: \"{message}\"\n\n" +
                         "Trả về đúng cấu trúc JSON sau:\n" +
                         "{\n" +
                         "  \"intent\": \"<một trong: hoi_gia | khieu_nai | khen | hoi_thong_tin | spam | chat_chat | khac>\",\n" +
                         "  \"sentiment\": \"<positive | negative | neutral>\",\n" +
                         "  \"confidence\": <0.0 đến 1.0>\n" +
                         "}";

            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    temperature = 0.1,
                    maxOutputTokens = 150
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={_apiKey}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
            var response = await _httpClient.PostAsync(url,
                new StringContent(JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json"),
                cts.Token);

            var json = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("[AI] Gemini raw response: {Json}", json);

            var parsed = JsonConvert.DeserializeObject<dynamic>(json);
            var text = (string)parsed!.candidates[0].content.parts[0].text;

            // Làm sạch markdown code block nếu có
            text = text.Trim().TrimStart('`').TrimEnd('`');
            if (text.StartsWith("json")) text = text[4..].Trim();

            var aiResult = JsonConvert.DeserializeObject<dynamic>(text)!;

            return new AiAnalysisResult
            {
                Intent     = (string)aiResult.intent ?? "khac",
                Sentiment  = (string)aiResult.sentiment ?? "neutral",
                Confidence = (double)(aiResult.confidence ?? 0.5),
                IsSpam     = (string)aiResult.intent == "spam"
            };
        }

        // --------------------------------------------------------
        // Fallback: phân tích đơn giản bằng từ khóa nếu AI không có
        // --------------------------------------------------------
        private AiAnalysisResult FallbackAnalysis(string message)
        {
            var lower = message.ToLower();

            string intent = "khac";
            string sentiment = "neutral";

            if (lower.Contains("giá") || lower.Contains("bao nhiêu") || lower.Contains("giá cả"))
                intent = "hoi_gia";
            else if (lower.Contains("chưa nhận") || lower.Contains("không nhận") || lower.Contains("tệ") || lower.Contains("thất vọng"))
                intent = "khieu_nai";
            else if (lower.Contains("hay") || lower.Contains("tuyệt") || lower.Contains("tốt") || lower.Contains("❤") || lower.Contains("👍"))
                intent = "khen";
            else if (lower.Contains("?") || lower.Contains("cho hỏi") || lower.Contains("địa chỉ"))
                intent = "hoi_thong_tin";

            if (lower.Contains("tốt") || lower.Contains("hay") || lower.Contains("tuyệt") || lower.Contains("❤") || lower.Contains("😍"))
                sentiment = "positive";
            else if (lower.Contains("tệ") || lower.Contains("thất vọng") || lower.Contains("lừa") || lower.Contains("chưa nhận"))
                sentiment = "negative";

            _logger.LogInformation("[AI-Fallback] intent={Intent}, sentiment={Sentiment}", intent, sentiment);

            return new AiAnalysisResult
            {
                Intent     = intent,
                Sentiment  = sentiment,
                Confidence = 0.6,
                IsSpam     = false
            };
        }
    }
}
