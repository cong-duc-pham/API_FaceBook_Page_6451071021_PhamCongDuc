using FacebookPageAPI.Models;

namespace FacebookPageAPI.Services
{
    /// <summary>
    /// Ghi nhận và phân tích các trường hợp xử lý thất bại.
    /// 
    /// Ví dụ:
    ///   - Gọi Facebook API bị timeout → publish send_failed
    ///   - AI không phản hồi → log AI_TIMEOUT
    ///   - Parse JSON lỗi → log PARSE_ERROR
    /// 
    /// Retry Service sẽ đọc danh sách này để thử lại.
    /// </summary>
    public class FailureLoggerService
    {
        private readonly ILogger<FailureLoggerService> _logger;

        // In-memory store (trong production dùng DB hoặc Kafka dead-letter topic)
        private static readonly List<FailureRecord> _failures = new();
        private static readonly object _lock = new();

        public FailureLoggerService(ILogger<FailureLoggerService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Ghi lại một sự kiện thất bại.
        /// </summary>
        public Task LogAsync(
            NormalizedEvent ev,
            string failureType,
            string errorMessage,
            bool requiresManualReview = false,
            int retryCount = 0)
        {
            var record = new FailureRecord
            {
                EventId             = ev.Header.EventId,
                FailureType         = failureType,
                ErrorMessage        = errorMessage,
                OccurredAt          = DateTime.UtcNow,
                RetryCount          = retryCount,
                RequiresManualReview = requiresManualReview,
                RawPayload          = $"CommentId={ev.Body.CommentId}, Message={ev.Body.Message}"
            };

            lock (_lock)
            {
                _failures.Add(record);
            }

            // Phân loại log level theo loại lỗi
            if (requiresManualReview || failureType.Contains("MALICIOUS"))
                _logger.LogCritical("[FailureLogger] {Type} | EventId={EventId} | {Error} | CẦN REVIEW THỦ CÔNG",
                    failureType, ev.Header.EventId, errorMessage);
            else
                _logger.LogError("[FailureLogger] {Type} | EventId={EventId} | {Error}",
                    failureType, ev.Header.EventId, errorMessage);

            return Task.CompletedTask;
        }

        /// <summary>Lấy tất cả failure records</summary>
        public List<FailureRecord> GetAll()
        {
            lock (_lock)
            {
                return new List<FailureRecord>(_failures.OrderByDescending(f => f.OccurredAt));
            }
        }

        /// <summary>Thống kê số lỗi theo loại</summary>
        public Dictionary<string, int> GetStatsByType()
        {
            lock (_lock)
            {
                return _failures
                    .GroupBy(f => f.FailureType)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }

        /// <summary>Lấy danh sách cần retry (retryCount < 3)</summary>
        public List<FailureRecord> GetRetryQueue()
        {
            lock (_lock)
            {
                return _failures
                    .Where(f => f.RetryCount < 3 && !f.RequiresManualReview)
                    .OrderBy(f => f.OccurredAt)
                    .ToList();
            }
        }

        /// <summary>Đánh dấu đã retry thành công (xóa khỏi queue)</summary>
        public void MarkRetrySuccess(string eventId)
        {
            lock (_lock)
            {
                _failures.RemoveAll(f => f.EventId == eventId);
            }
            _logger.LogInformation("[FailureLogger] Retry thành công, xóa failure record cho EventId={EventId}", eventId);
        }
    }
}
