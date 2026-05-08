using Microsoft.AspNetCore.Mvc;
using FacebookPageAPI.Services;
using FacebookPageAPI.Models;

namespace FacebookPageAPI.Controllers
{
    /// <summary>
    /// API Dashboard để giám sát toàn bộ pipeline xử lý:
    ///   GET /api/monitor/stats       - Thống kê tổng quan
    ///   GET /api/monitor/events      - Danh sách events và trạng thái
    ///   GET /api/monitor/failures    - Danh sách lỗi cần xử lý
    ///   GET /api/monitor/review      - Hàng chờ review thủ công
    ///   GET /api/monitor/blacklist   - Danh sách blacklist nội bộ
    ///   POST /api/monitor/blacklist/{senderId}  - Thêm vào blacklist
    ///   DELETE /api/monitor/review/{commentId} - Xử lý xong review
    /// </summary>
    [ApiController]
    [Route("api/monitor")]
    public class MonitoringController : ControllerBase
    {
        private readonly EventStateTracker _stateTracker;
        private readonly FailureLoggerService _failureLogger;
        private readonly SpamDetectionService _spamDetector;
        private readonly DecisionService _decisionService;

        public MonitoringController(
            EventStateTracker stateTracker,
            FailureLoggerService failureLogger,
            SpamDetectionService spamDetector,
            DecisionService decisionService)
        {
            _stateTracker   = stateTracker;
            _failureLogger  = failureLogger;
            _spamDetector   = spamDetector;
            _decisionService = decisionService;
        }

        // ── GET /api/monitor/stats ────────────────────────────────────────
        /// <summary>Thống kê tổng quan pipeline</summary>
        [HttpGet("stats")]
        public IActionResult GetStats()
        {
            var eventStats   = _stateTracker.GetStats();
            var failureStats = _failureLogger.GetStatsByType();
            var blacklistCount = _spamDetector.GetBlacklist().Count;

            return Ok(new
            {
                Student     = new { Id = "6451071021", Name = "Pham Cong Duc" },
                UpdatedAt   = DateTime.UtcNow,
                EventStats  = eventStats,
                FailureStats = failureStats,
                BlacklistCount = blacklistCount,
                ManualReviewQueue = _decisionService.GetManualReviewQueue().Count,
                RetryQueue  = _failureLogger.GetRetryQueue().Count
            });
        }

        // ── GET /api/monitor/events ────────────────────────────────────────
        /// <summary>Danh sách 200 events gần nhất kèm trạng thái</summary>
        [HttpGet("events")]
        public IActionResult GetEvents([FromQuery] string? state = null)
        {
            var events = _stateTracker.GetAll();

            if (!string.IsNullOrEmpty(state) &&
                Enum.TryParse<EventState>(state, true, out var filterState))
            {
                events = events.Where(e => e.State == filterState).ToList();
            }

            return Ok(new
            {
                Total  = events.Count,
                Events = events.Select(e => new
                {
                    e.EventId,
                    e.CommentId,
                    e.SenderId,
                    Message    = e.Message?.Length > 100
                        ? e.Message[..100] + "..."
                        : e.Message,
                    State      = e.State.ToString(),
                    e.ReceivedAt,
                    e.ProcessedAt,
                    e.RepliedAt,
                    e.FailureReason,
                    Analysis   = e.Analysis == null ? null : new
                    {
                        e.Analysis.Intent,
                        e.Analysis.Sentiment,
                        Confidence = $"{e.Analysis.Confidence:P0}",
                        e.Analysis.IsSpam,
                        SpamLevel = e.Analysis.SpamLevel.ToString()
                    },
                    TransitionCount = e.Transitions.Count
                })
            });
        }

        // ── GET /api/monitor/events/{eventId} ─────────────────────────────
        /// <summary>Chi tiết một event kèm lịch sử chuyển trạng thái</summary>
        [HttpGet("events/{eventId}")]
        public IActionResult GetEvent(string eventId)
        {
            var record = _stateTracker.Get(eventId);
            if (record == null)
                return NotFound(new { Error = $"Không tìm thấy event {eventId}" });

            return Ok(record);
        }

        // ── GET /api/monitor/failures ─────────────────────────────────────
        /// <summary>Danh sách tất cả lỗi và trường hợp thất bại</summary>
        [HttpGet("failures")]
        public IActionResult GetFailures()
        {
            var failures = _failureLogger.GetAll();
            return Ok(new
            {
                Total          = failures.Count,
                StatsByType    = _failureLogger.GetStatsByType(),
                RetryQueue     = _failureLogger.GetRetryQueue().Count,
                Failures       = failures
            });
        }

        // ── GET /api/monitor/review ────────────────────────────────────────
        /// <summary>Hàng chờ review thủ công (scam, bot rõ ràng)</summary>
        [HttpGet("review")]
        public IActionResult GetManualReviewQueue()
        {
            var queue = _decisionService.GetManualReviewQueue();
            return Ok(new
            {
                Total = queue.Count,
                Items = queue
            });
        }

        // ── GET /api/monitor/blacklist ─────────────────────────────────────
        /// <summary>Danh sách blacklist nội bộ</summary>
        [HttpGet("blacklist")]
        public IActionResult GetBlacklist()
        {
            var list = _spamDetector.GetBlacklist();
            return Ok(new
            {
                Total     = list.Count,
                SenderIds = list
            });
        }

        // ── POST /api/monitor/blacklist/{senderId} ─────────────────────────
        /// <summary>Thêm thủ công một senderId vào blacklist</summary>
        [HttpPost("blacklist/{senderId}")]
        public IActionResult AddToBlacklist(string senderId)
        {
            _spamDetector.AddToBlacklist(senderId);
            return Ok(new
            {
                Message  = $"Đã thêm {senderId} vào blacklist nội bộ",
                SenderId = senderId,
                At       = DateTime.UtcNow
            });
        }

        // ── GET /api/monitor/spam/{senderId} ──────────────────────────────
        /// <summary>Kiểm tra spam history của một user</summary>
        [HttpGet("spam/{senderId}")]
        public IActionResult GetSpamHistory(string senderId)
        {
            var count       = _spamDetector.GetSpamCount(senderId);
            var blacklisted = _spamDetector.IsBlacklisted(senderId);
            return Ok(new
            {
                SenderId    = senderId,
                SpamCount24h = count,
                IsBlacklisted = blacklisted
            });
        }
    }
}
