using FacebookPageAPI.Models;
using System.Text.RegularExpressions;

namespace FacebookPageAPI.Services
{
    /// <summary>
    /// Phát hiện spam dựa trên quy tắc cục bộ (không cần AI):
    ///   - Chứa link độc hại / URL rõ ràng
    ///   - Lặp lại nội dung >= 3 lần trong 24h từ cùng một user
    /// </summary>
    public class SpamDetectionService
    {
        private readonly ILogger<SpamDetectionService> _logger;

        // In-memory store: senderId → list<(message, time)>
        // Trong production nên dùng Redis
        private static readonly Dictionary<string, List<(string Message, DateTime At)>> _spamHistory = new();
        private static readonly object _lock = new();

        // Danh sách blacklist nội bộ (senderId bị cấm auto-reply)
        private static readonly HashSet<string> _blacklist = new();

        // Regex phát hiện URL (http/https/www)
        private static readonly Regex UrlRegex = new(
            @"(https?://|www\.)\S+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Regex phát hiện scam / bot patterns
        private static readonly Regex ScamRegex = new(
            @"(free money|click here|win \$|lottery|crypto giveaway|đầu tư sinh lời|kiếm tiền online nhanh)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public SpamDetectionService(ILogger<SpamDetectionService> logger)
        {
            _logger = logger;
        }

        /// <summary>Kiểm tra xem senderId có nằm trong blacklist không</summary>
        public bool IsBlacklisted(string senderId) => _blacklist.Contains(senderId);

        /// <summary>Thêm senderId vào blacklist nội bộ</summary>
        public void AddToBlacklist(string senderId)
        {
            _blacklist.Add(senderId);
            _logger.LogWarning("[Blacklist] Đã thêm user {SenderId} vào blacklist nội bộ", senderId);
        }

        /// <summary>
        /// Phân tích một message để xác định mức độ spam
        /// </summary>
        public AiAnalysisResult DetectSpam(string? senderId, string? message)
        {
            var result = new AiAnalysisResult { IsSpam = false, SpamLevel = SpamLevel.None };

            if (string.IsNullOrWhiteSpace(message))
                return result;

            // --- 1. Kiểm tra scam / bot rõ ràng ---
            if (ScamRegex.IsMatch(message))
            {
                result.IsSpam = true;
                result.SpamLevel = SpamLevel.Malicious;
                result.SpamReason = "Chứa nội dung scam / giveaway giả mạo";
                _logger.LogWarning("[Spam] Phát hiện nội dung scam từ {SenderId}: {Message}", senderId, message);
                return result;
            }

            // --- 2. Kiểm tra URL đáng ngờ ---
            if (UrlRegex.IsMatch(message))
            {
                result.IsSpam = true;
                result.SpamLevel = SpamLevel.Mild;
                result.SpamReason = "Chứa link URL";
                _logger.LogInformation("[Spam] Message chứa link từ {SenderId}", senderId);
            }

            // --- 3. Kiểm tra lặp nội dung 3 lần / 24h ---
            if (!string.IsNullOrEmpty(senderId))
            {
                lock (_lock)
                {
                    // Dọn dẹp history cũ hơn 24h
                    if (_spamHistory.ContainsKey(senderId))
                    {
                        _spamHistory[senderId] = _spamHistory[senderId]
                            .Where(x => x.At >= DateTime.UtcNow.AddHours(-24))
                            .ToList();
                    }
                    else
                    {
                        _spamHistory[senderId] = new List<(string, DateTime)>();
                    }

                    // Đếm số lần gửi cùng nội dung trong 24h
                    var sameMessageCount = _spamHistory[senderId]
                        .Count(x => x.Message.Equals(message, StringComparison.OrdinalIgnoreCase));

                    _spamHistory[senderId].Add((message, DateTime.UtcNow));

                    if (sameMessageCount >= 2) // >= 3 lần (lần này là lần 3+)
                    {
                        result.IsSpam = true;
                        result.SpamLevel = SpamLevel.Repeated;
                        result.SpamReason = $"Lặp lại nội dung {sameMessageCount + 1} lần trong 24h";
                        _logger.LogWarning("[Spam] User {SenderId} lặp message {Count} lần trong 24h",
                            senderId, sameMessageCount + 1);

                        // Tự động đưa vào blacklist nội bộ
                        AddToBlacklist(senderId);
                    }
                }
            }

            return result;
        }

        /// <summary>Lấy danh sách blacklist hiện tại</summary>
        public IReadOnlyCollection<string> GetBlacklist() => _blacklist;

        /// <summary>Lấy thống kê spam history của một user</summary>
        public int GetSpamCount(string senderId)
        {
            lock (_lock)
            {
                return _spamHistory.TryGetValue(senderId, out var history)
                    ? history.Count(x => x.At >= DateTime.UtcNow.AddHours(-24))
                    : 0;
            }
        }
    }
}
