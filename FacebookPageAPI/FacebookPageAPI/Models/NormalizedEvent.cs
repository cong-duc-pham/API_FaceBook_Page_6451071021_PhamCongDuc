namespace FacebookPageAPI.Models
{
    // ==========================================
    // MODEL: Cấu trúc event chuẩn từ Kafka topic raw_events
    // ==========================================
    public class NormalizedEvent
    {
        public EventHeader Header { get; set; } = new();
        public EventBody Body { get; set; } = new();
    }

    public class EventHeader
    {
        public string EventId { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string StudentId { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class EventBody
    {
        public string Type { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? PageId { get; set; }
        public string? PostId { get; set; }
        public string? CommentId { get; set; }
        public string? Message { get; set; }
        public string? SenderName { get; set; }
        public string? SenderId { get; set; }
    }

    // ==========================================
    // MODEL: Kết quả phân tích AI (Intent + Sentiment)
    // ==========================================
    public class AiAnalysisResult
    {
        public string Intent { get; set; } = "unknown";       // hỏi_giá, khiếu_nại, khen, hỏi_thông_tin, ...
        public string Sentiment { get; set; } = "neutral";    // positive, negative, neutral
        public double Confidence { get; set; } = 0.0;
        public bool IsSpam { get; set; } = false;
        public string SpamReason { get; set; } = string.Empty;
        public SpamLevel SpamLevel { get; set; } = SpamLevel.None;
    }

    // ==========================================
    // MODEL: Trạng thái xử lý của một event
    // ==========================================
    public enum EventState
    {
        Received,    // Vừa nhận được từ Kafka
        Processing,  // Đang phân tích
        Processed,   // Đã phân tích xong
        Replied,     // Đã auto-reply thành công
        Hidden,      // Comment đã bị ẩn
        Failed       // Xử lý thất bại
    }

    public enum SpamLevel
    {
        None,        // Không phải spam
        Mild,        // Spam nhẹ (chứa link nhỏ)
        Repeated,    // Lặp lại >= 3 lần / 24h
        Malicious    // Độc hại, scam, bot rõ ràng
    }

    // ==========================================
    // MODEL: Bản ghi trạng thái event
    // ==========================================
    public class EventStateRecord
    {
        public string EventId { get; set; } = string.Empty;
        public string CommentId { get; set; } = string.Empty;
        public string? SenderId { get; set; }
        public string? Message { get; set; }
        public EventState State { get; set; } = EventState.Received;
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public DateTime? RepliedAt { get; set; }
        public string? FailureReason { get; set; }
        public AiAnalysisResult? Analysis { get; set; }
        public List<EventStateTransition> Transitions { get; set; } = new();
    }

    public class EventStateTransition
    {
        public EventState FromState { get; set; }
        public EventState ToState { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
        public string? Note { get; set; }
    }

    // ==========================================
    // MODEL: Bản ghi thất bại để phân tích
    // ==========================================
    public class FailureRecord
    {
        public string EventId { get; set; } = string.Empty;
        public string FailureType { get; set; } = string.Empty; // AI_TIMEOUT, FACEBOOK_API_ERROR, ...
        public string ErrorMessage { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public int RetryCount { get; set; } = 0;
        public bool RequiresManualReview { get; set; } = false;
        public string? RawPayload { get; set; }
    }
}
