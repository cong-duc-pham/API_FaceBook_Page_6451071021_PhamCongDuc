using FacebookPageAPI.Models;

namespace FacebookPageAPI.Services
{
    /// <summary>
    /// Theo dõi trạng thái xử lý của từng event theo pipeline:
    ///   received → processing → processed → replied / hidden / failed
    /// 
    /// Lưu in-memory (trong production dùng Redis hoặc DB).
    /// </summary>
    public class EventStateTracker
    {
        private readonly ILogger<EventStateTracker> _logger;

        // In-memory store: eventId → EventStateRecord
        private static readonly Dictionary<string, EventStateRecord> _store = new();
        private static readonly object _lock = new();

        public EventStateTracker(ILogger<EventStateTracker> logger)
        {
            _logger = logger;
        }

        /// <summary>Khởi tạo một record mới khi nhận event</summary>
        public EventStateRecord Initialize(NormalizedEvent ev)
        {
            var record = new EventStateRecord
            {
                EventId    = ev.Header.EventId,
                CommentId  = ev.Body.CommentId ?? string.Empty,
                SenderId   = ev.Body.SenderId,
                Message    = ev.Body.Message,
                State      = EventState.Received,
                ReceivedAt = DateTime.UtcNow
            };

            lock (_lock)
            {
                _store[ev.Header.EventId] = record;
            }

            _logger.LogInformation("[State] Event {EventId} → {State}", record.EventId, record.State);
            return record;
        }

        /// <summary>Chuyển trạng thái của event</summary>
        public void Transition(string eventId, EventState toState, string? note = null)
        {
            lock (_lock)
            {
                if (!_store.TryGetValue(eventId, out var record))
                {
                    _logger.LogWarning("[State] Không tìm thấy event {EventId} để chuyển trạng thái", eventId);
                    return;
                }

                var transition = new EventStateTransition
                {
                    FromState = record.State,
                    ToState   = toState,
                    At        = DateTime.UtcNow,
                    Note      = note
                };

                record.Transitions.Add(transition);
                record.State = toState;

                if (toState == EventState.Processed || toState == EventState.Hidden)
                    record.ProcessedAt = DateTime.UtcNow;

                if (toState == EventState.Replied)
                    record.RepliedAt = DateTime.UtcNow;

                if (toState == EventState.Failed)
                    record.FailureReason = note;

                _logger.LogInformation("[State] Event {EventId}: {From} → {To}{Note}",
                    eventId,
                    transition.FromState,
                    transition.ToState,
                    note != null ? $" ({note})" : "");
            }
        }

        /// <summary>Gắn kết quả phân tích AI vào record</summary>
        public void AttachAnalysis(string eventId, AiAnalysisResult analysis)
        {
            lock (_lock)
            {
                if (_store.TryGetValue(eventId, out var record))
                    record.Analysis = analysis;
            }
        }

        /// <summary>Lấy trạng thái hiện tại của một event</summary>
        public EventStateRecord? Get(string eventId)
        {
            lock (_lock)
            {
                return _store.TryGetValue(eventId, out var r) ? r : null;
            }
        }

        /// <summary>Lấy tất cả các records (để hiển thị trên dashboard)</summary>
        public List<EventStateRecord> GetAll()
        {
            lock (_lock)
            {
                return _store.Values
                    .OrderByDescending(r => r.ReceivedAt)
                    .Take(200) // Giới hạn 200 bản ghi mới nhất
                    .ToList();
            }
        }

        /// <summary>Thống kê số lượng theo trạng thái</summary>
        public Dictionary<string, int> GetStats()
        {
            lock (_lock)
            {
                return _store.Values
                    .GroupBy(r => r.State.ToString())
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }
    }
}
