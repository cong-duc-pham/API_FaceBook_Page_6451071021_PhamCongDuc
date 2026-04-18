using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace YourProjectName.Controllers
{
    [Route("api/page")]
    [ApiController]
    public class FacebookController : ControllerBase
    {
        private readonly string _pageId;
        private readonly string _accessToken;
        private readonly HttpClient _httpClient;

        public FacebookController(IConfiguration config)
        {
            _pageId = config["FacebookConfig:PageId"];
            _accessToken = config["FacebookConfig:AccessToken"];
            _httpClient = new HttpClient { BaseAddress = new Uri("https://graph.facebook.com/v21.0/") };
        }

        // 1. GET /api/page/{pageId}
        [HttpGet("{pageId}")]
        public async Task<IActionResult> GetPageInfo(string pageId)
        {
            var response = await _httpClient.GetAsync($"{pageId}?access_token={_accessToken}");
            var result = await response.Content.ReadAsStringAsync();
            return Ok(result);
        }

        // 2. GET /api/page/{pageId}/posts
        [HttpGet("{pageId}/posts")]
        public async Task<IActionResult> GetPosts(string pageId)
        {
            var response = await _httpClient.GetAsync($"{pageId}/feed?access_token={_accessToken}");
            var result = await response.Content.ReadAsStringAsync();
            return Ok(result);
        }

        // 3. POST /api/page/{pageId}/posts (Đăng bài viết)
        [HttpPost("{pageId}/posts")]
        public async Task<IActionResult> CreatePost(string pageId, [FromBody] PostRequest request)
        {
            var parameters = new Dictionary<string, string>
            {
                { "message", request.Message },
                { "access_token", _accessToken }
            };
            var content = new FormUrlEncodedContent(parameters);
            var response = await _httpClient.PostAsync($"{pageId}/feed", content);
            return Ok(await response.Content.ReadAsStringAsync());
        }

        // 4. DELETE /api/page/post/{postId}
        [HttpDelete("post/{postId}")]
        public async Task<IActionResult> DeletePost(string postId)
        {
            var response = await _httpClient.DeleteAsync($"{postId}?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        // 5. GET /api/page/post/{postId}/comments
        [HttpGet("post/{postId}/comments")]
        public async Task<IActionResult> GetComments(string postId)
        {
            var response = await _httpClient.GetAsync($"{postId}/comments?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        // 6. GET /api/page/post/{postId}/likes
        [HttpGet("post/{postId}/likes")]
        public async Task<IActionResult> GetLikes(string postId)
        {
            var response = await _httpClient.GetAsync($"{postId}/likes?summary=true&access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        // 7. GET /api/page/{pageId}/insights
        [HttpGet("{pageId}/insights")]
        public async Task<IActionResult> GetInsights(string pageId)
        {
            // Lấy chỉ số lượt view page (page_views_total)
            var response = await _httpClient.GetAsync($"{pageId}/insights?metric=page_views_total&period=day&access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }
    }

    public class PostRequest
    {
        public string Message { get; set; }
    }
}