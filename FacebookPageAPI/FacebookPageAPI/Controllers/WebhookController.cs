using Microsoft.AspNetCore.Mvc;
using Confluent.Kafka;
using Newtonsoft.Json;
using System.Text;
using Microsoft.AspNetCore.Authorization; // 1. Cần cái này để cho phép Facebook truy cập

namespace FacebookPageAPI.Controllers
{
    [ApiController]
    [Route("webhook")]
    public class WebhookController : ControllerBase
    {
        private const string VerifyToken = "6451071021_Duc_Secret";
        private readonly ProducerConfig _kafkaConfig;

        public WebhookController()
        {
            _kafkaConfig = new ProducerConfig
            {
                BootstrapServers = "localhost:9092",
                // Sửa lại dòng này để tránh lỗi chính tả nếu chưa cài đủ thư viện
                Acks = Confluent.Kafka.Acks.All
            };
        }

        /// <summary>
        /// Bước 1: Xác thực Webhook với Facebook
        /// </summary>
        [AllowAnonymous] // 2. Mở cửa cho Facebook xác thực
        [HttpGet]
        public IActionResult Verify()
        {
            var mode = Request.Query["hub.mode"].ToString();
            var token = Request.Query["hub.verify_token"].ToString();
            var challenge = Request.Query["hub.challenge"].ToString();

            if (mode == "subscribe" && token == VerifyToken)
            {
                Console.WriteLine("--- Webhook Verified Successfully! ---");
                return Ok(challenge);
            }

            return Forbid();
        }

        /// <summary>
        /// Bước 2: Nhận sự kiện thực tế (Comment) và đẩy vào Kafka
        /// </summary>
        [AllowAnonymous] // 3. Mở cửa cho Facebook bắn JSON vào
        [HttpPost]
        public async Task<IActionResult> ReceiveEvent()
        {
            try
            {
                using var reader = new StreamReader(Request.Body, Encoding.UTF8);
                var rawPayload = await reader.ReadToEndAsync();

                if (string.IsNullOrEmpty(rawPayload)) return BadRequest();

                // Log ra màn hình console để Đức dễ theo dõi dữ liệu thô
                Console.WriteLine("--- New Event Received from Facebook ---");
                Console.WriteLine(rawPayload);

                var fbData = Newtonsoft.Json.Linq.JObject.Parse(rawPayload);

                // --- BƯỚC NORMALIZE THEO SCHEMA CHUẨN ---
                var entry = fbData["entry"]?.FirstOrDefault();
                var change = entry?["changes"]?.FirstOrDefault()?["value"];

                var normalizedEvent = new
                {
                    Header = new
                    {
                        EventId = Guid.NewGuid().ToString(),
                        Source = "FACEBOOK_PLATFORM",
                        StudentId = "6451071021",
                        StudentName = "Pham Cong Duc",
                        Timestamp = DateTime.UtcNow
                    },
                    Body = new
                    {
                        Type = entry?["id"] != null ? "PAGE_EVENT" : "UNKNOWN",
                        Action = "NEW_COMMENT",
                        PageId = entry?["id"]?.ToString(),
                        PostId = change?["post_id"]?.ToString(),
                        CommentId = change?["comment_id"]?.ToString(),
                        Message = change?["message"]?.ToString(),
                        SenderName = change?["from"]?["name"]?.ToString(),
                        SenderId = change?["from"]?["id"]?.ToString()
                    }
                };

                var kafkaMessage = JsonConvert.SerializeObject(normalizedEvent);

                // --- ĐẨY DỮ LIỆU VÀO KAFKA ---
                using var producer = new ProducerBuilder<Null, string>(_kafkaConfig).Build();

                var result = await producer.ProduceAsync("raw_events", new Message<Null, string>
                {
                    Value = kafkaMessage
                });

                Console.WriteLine($"[Kafka] Success: Delivered to {result.TopicPartitionOffset}");

                return Ok();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Webhook failed: {ex.Message}");
                return StatusCode(500);
            }
        }
    }
}