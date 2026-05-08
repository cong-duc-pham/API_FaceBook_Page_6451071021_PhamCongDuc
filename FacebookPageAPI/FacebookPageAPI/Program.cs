using FacebookPageAPI.BackgroundServices;
using FacebookPageAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// --- DÒNG QUAN TRỌNG NHẤT: ÉP CỔNG 3001 ---
builder.WebHost.UseUrls("http://0.0.0.0:3001");

// ================================================================
// ĐĂNG KÝ SERVICES - Pipeline xử lý comment
// ================================================================

// HttpClient factories
builder.Services.AddHttpClient("gemini");
builder.Services.AddHttpClient("facebook");

// Core pipeline services (Singleton để chia sẻ in-memory state)
builder.Services.AddSingleton<SpamDetectionService>();
builder.Services.AddSingleton<EventStateTracker>();
builder.Services.AddSingleton<FailureLoggerService>();
builder.Services.AddSingleton<AiAnalysisService>();
builder.Services.AddSingleton<DecisionService>();

// Background Service: consume Kafka raw_events và xử lý pipeline
builder.Services.AddHostedService<CoreProcessingService>();

// Add controllers + Swagger
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Facebook Page API - 6451071021_Phạm Công Đức",
        Version = "v1",
        Description = """
            API tích hợp Facebook Page + Kafka Pipeline
            
            📌 Webhook: Nhận comment từ Facebook → push vào Kafka raw_events
            📌 Core Processing: Consume raw_events → Spam Detection → AI Analysis → Decision
            📌 Monitor: Dashboard theo dõi trạng thái xử lý từng event
            
            Sinh viên: Phạm Công Đức - MSSV: 6451071021
            """
    });
});

var app = builder.Build();

// ================================================================
// CONFIGURE MIDDLEWARE PIPELINE
// ================================================================
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Facebook Page API v1");
    c.RoutePrefix = string.Empty; // Swagger là trang chủ
    c.DocumentTitle = "Facebook Page API - 6451071021 Phạm Công Đức";
});

// app.UseHttpsRedirection(); // Comment để dùng với ngrok http

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();