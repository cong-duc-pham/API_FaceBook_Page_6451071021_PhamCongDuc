# Facebook Page API

Project ASP.NET Core 8.0 tich hop Facebook Page Graph API, Facebook Webhook, Kafka va pipeline xu ly comment tu dong.

Ung dung nhan su kien comment tu Facebook, chuan hoa payload, day vao Kafka topic `raw_events`, sau do background service consume event de phan tich spam, phan tich intent/sentiment bang Gemini AI va thuc hien quyet dinh auto-reply hoac an comment.

## Thong tin project

- Sinh vien: Pham Cong Duc
- MSSV: 6451071021
- Framework: ASP.NET Core 8.0
- Kieu ung dung: MVC/API + Background Service
- Port mac dinh: `3001`
- Swagger UI: `http://localhost:3001`
- Kafka topic dau vao: `raw_events`

## Chuc nang chinh

- Xac thuc Facebook Webhook qua `GET /webhook`.
- Nhan event comment Facebook qua `POST /webhook`.
- Chuan hoa event theo schema noi bo.
- Push event vao Kafka topic `raw_events`.
- Background worker consume Kafka lien tuc.
- Phat hien spam bang rule:
  - Comment chua URL.
  - Noi dung scam/bot ro rang.
  - Lap lai cung noi dung tu mot user trong 24h.
- Phan tich intent va sentiment bang Gemini AI, co fallback khi chua cau hinh API key.
- Tu dong quyet dinh:
  - Reply comment hop le.
  - An comment spam.
  - Dua spam nguy hiem vao hang cho review thu cong.
  - Dua user spam lap lai vao blacklist noi bo.
- API monitoring de xem stats, events, failures, review queue va blacklist.
- Swagger UI de test API.

## Kien truc xu ly

```text
Facebook Page
    |
    | Webhook event
    v
POST /webhook
    |
    | Normalize payload
    v
Kafka topic: raw_events
    |
    | CoreProcessingService consume
    v
SpamDetectionService
    |
    v
AiAnalysisService
    |
    v
DecisionService
    |
    +--> Facebook Graph API: reply comment
    +--> Facebook Graph API: hide comment
    +--> Manual review queue
    +--> Internal blacklist
```

## Cong nghe su dung

- ASP.NET Core 8.0
- MVC Controllers
- Hosted Background Service
- Confluent.Kafka
- Newtonsoft.Json
- Swashbuckle.AspNetCore / Swagger
- Facebook Graph API v21.0
- Google Gemini API
- Docker Compose
- Kafka, Zookeeper, Kafka UI
- ngrok cho public webhook URL

## Cau truc thu muc

```text
.
|-- docker-compose.yml
|-- FacebookPageAPI.sln
|-- FacebookPageAPI/
|   |-- BackgroundServices/
|   |   `-- CoreProcessingService.cs
|   |-- Controllers/
|   |   |-- FacebookController.cs
|   |   |-- HomeController.cs
|   |   |-- MonitoringController.cs
|   |   `-- WebhookController.cs
|   |-- Models/
|   |   |-- AuthModels.cs
|   |   |-- ErrorViewModel.cs
|   |   `-- NormalizedEvent.cs
|   |-- Services/
|   |   |-- AiAnalysisService.cs
|   |   |-- DecisionService.cs
|   |   |-- EventStateTracker.cs
|   |   |-- FailureLoggerService.cs
|   |   `-- SpamDetectionService.cs
|   |-- Program.cs
|   |-- appsettings.json
|   `-- appsettings.Development.json
`-- ngrok.exe
```

## Yeu cau cai dat

- .NET SDK 8.0 tro len
- Docker Desktop
- Facebook Page va Facebook App co cau hinh Webhook
- Page Access Token co quyen phu hop de doc/ghi Page
- Gemini API key neu muon dung AI thuc te
- ngrok neu muon nhan webhook tu Facebook tren may local

Kiem tra moi truong:

```powershell
dotnet --version
docker --version
docker compose version
```

## Cau hinh ung dung

File cau hinh chinh nam tai:

```text
FacebookPageAPI/appsettings.json
```

Mau cau hinh:

```json
{
  "FacebookConfig": {
    "PageId": "YOUR_PAGE_ID",
    "AccessToken": "YOUR_PAGE_ACCESS_TOKEN"
  },
  "KafkaConfig": {
    "BootstrapServers": "localhost:9092"
  },
  "GeminiConfig": {
    "ApiKey": "YOUR_GEMINI_API_KEY"
  }
}
```

Luu y bao mat:

- Khong nen commit Page Access Token, Gemini API Key hoac webhook verify token len repository public.
- Neu token da bi dua len public, nen thu hoi token cu va tao token moi.
- Trong moi truong that, nen cau hinh secret bang User Secrets, bien moi truong hoac secret manager.

Vi du cau hinh bang User Secrets:

```powershell
cd .\FacebookPageAPI
dotnet user-secrets init
dotnet user-secrets set "FacebookConfig:PageId" "YOUR_PAGE_ID"
dotnet user-secrets set "FacebookConfig:AccessToken" "YOUR_PAGE_ACCESS_TOKEN"
dotnet user-secrets set "GeminiConfig:ApiKey" "YOUR_GEMINI_API_KEY"
```

## Chay Kafka bang Docker Compose

Tai thu muc goc project:

```powershell
docker compose up -d
```

Sau khi chay thanh cong:

- Kafka: `localhost:9092`
- Kafka UI: `http://localhost:8080`
- Zookeeper: `localhost:2181`

Kiem tra container:

```powershell
docker compose ps
```

Dung Kafka:

```powershell
docker compose down
```

## Chay ung dung

Tai thu muc goc project:

```powershell
dotnet restore
dotnet run --project .\FacebookPageAPI\FacebookPageAPI.csproj
```

Ung dung se lang nghe tai:

```text
http://localhost:3001
```

Mo Swagger UI:

```text
http://localhost:3001
```

## Cau hinh Facebook Webhook bang ngrok

Chay ung dung local o port `3001`, sau do mo terminal khac:

```powershell
.\ngrok.exe http 3001
```

Lay HTTPS forwarding URL tu ngrok, vi du:

```text
https://abc-123.ngrok-free.app
```

Trong Facebook Developer Dashboard, cau hinh Webhook:

- Callback URL: `https://abc-123.ngrok-free.app/webhook`
- Verify Token: `6451071021_Duc_Secret`
- Subscribe field phu hop voi Page comment/feed theo yeu cau bai lam.

Sau khi Facebook verify thanh cong, endpoint `POST /webhook` se nhan event comment thuc te.

## Danh sach API

### Webhook

| Method | Endpoint | Mo ta |
| --- | --- | --- |
| GET | `/webhook` | Xac thuc webhook voi Facebook |
| POST | `/webhook` | Nhan event Facebook va day vao Kafka |

### Facebook Page API

| Method | Endpoint | Mo ta |
| --- | --- | --- |
| GET | `/api/page/{pageId}` | Lay thong tin Page |
| GET | `/api/page/{pageId}/posts` | Lay danh sach bai viet |
| POST | `/api/page/{pageId}/posts` | Dang bai viet moi |
| DELETE | `/api/page/post/{postId}` | Xoa bai viet |
| GET | `/api/page/post/{postId}/comments` | Lay comment cua bai viet |
| GET | `/api/page/post/{postId}/likes` | Lay likes cua bai viet |
| GET | `/api/page/{pageId}/insights` | Lay insight `page_views_total` |

Body tao bai viet:

```json
{
  "message": "Noi dung bai viet"
}
```

### Monitoring API

| Method | Endpoint | Mo ta |
| --- | --- | --- |
| GET | `/api/monitor/stats` | Thong ke tong quan pipeline |
| GET | `/api/monitor/events` | Danh sach 200 event gan nhat |
| GET | `/api/monitor/events?state=Replied` | Loc event theo trang thai |
| GET | `/api/monitor/events/{eventId}` | Chi tiet mot event |
| GET | `/api/monitor/failures` | Danh sach loi/failure |
| GET | `/api/monitor/review` | Hang cho review thu cong |
| GET | `/api/monitor/blacklist` | Danh sach blacklist noi bo |
| POST | `/api/monitor/blacklist/{senderId}` | Them sender vao blacklist |
| GET | `/api/monitor/spam/{senderId}` | Kiem tra spam history cua sender |

## Test nhanh webhook local

Neu khong co event thuc tu Facebook, co the test bang PowerShell:

```powershell
$body = @{
  entry = @(
    @{
      id = "1056183227582621"
      changes = @(
        @{
          value = @{
            post_id = "post_001"
            comment_id = "comment_001"
            message = "Cho minh hoi gia san pham nay?"
            from = @{
              id = "user_001"
              name = "Nguyen Van A"
            }
          }
        }
      )
    }
  )
} | ConvertTo-Json -Depth 10

Invoke-RestMethod -Method Post -Uri "http://localhost:3001/webhook" -Body $body -ContentType "application/json"
```

Sau do xem trang thai:

```powershell
Invoke-RestMethod "http://localhost:3001/api/monitor/stats"
Invoke-RestMethod "http://localhost:3001/api/monitor/events"
```

## Quy tac phan loai spam

| Loai | Dieu kien | Hanh dong |
| --- | --- | --- |
| `None` | Comment binh thuong | Auto-reply theo intent |
| `Mild` | Co URL `http`, `https`, `www` | An comment |
| `Repeated` | Cung sender gui cung noi dung tu 3 lan/24h | An comment va blacklist |
| `Malicious` | Chua pattern scam/bot | An comment va dua vao manual review |

## Intent va auto-reply

AI hoac fallback co the phan tich cac intent:

- `hoi_gia`
- `khieu_nai`
- `khen`
- `hoi_thong_tin`
- `chat_chat`
- `spam`
- `khac`

Neu `GeminiConfig:ApiKey` chua duoc cau hinh, service se dung fallback theo tu khoa de ung dung van chay duoc.

## Trang thai event

Pipeline su dung cac trang thai:

- `Received`: vua nhan event tu Kafka.
- `Processing`: dang xu ly spam/AI.
- `Processed`: da phan tich xong.
- `Replied`: da gui auto-reply.
- `Hidden`: da an comment hoac bo qua vi blacklist.
- `Failed`: xu ly that bai.

## Build va kiem tra

Build project:

```powershell
dotnet build .\FacebookPageAPI.sln
```

Chay app:

```powershell
dotnet run --project .\FacebookPageAPI\FacebookPageAPI.csproj
```

Kiem tra Swagger:

```text
http://localhost:3001
```

## Loi thuong gap

### Khong ket noi duoc Kafka

- Dam bao da chay `docker compose up -d`.
- Kiem tra Kafka dang expose port `9092`.
- Kiem tra `KafkaConfig:BootstrapServers` la `localhost:9092`.

### Facebook khong verify webhook

- Dam bao app dang chay o port `3001`.
- Dam bao ngrok URL con active.
- Callback URL phai ket thuc bang `/webhook`.
- Verify Token tren Facebook phai dung `6451071021_Duc_Secret`.

### Khong reply/an duoc comment

- Kiem tra Page Access Token.
- Kiem tra token co quyen quan ly Page/comment phu hop.
- Kiem tra `comment_id` trong event co hop le hay khong.

### Gemini khong hoat dong

- Kiem tra `GeminiConfig:ApiKey`.
- Neu API key chua cau hinh, he thong se tu dong dung fallback keyword.

## Ghi chu phat trien

- Cac queue/state hien tai duoc luu in-memory, nen se mat khi restart app.
- Moi truong production nen thay in-memory store bang Redis, database hoac Kafka topic rieng.
- Consumer hien tai tach luong consume va processing bang bounded channel de giam nguy co mat event khi luong comment tang dot bien.
- Nen bo sung authentication cho cac endpoint monitoring neu deploy public.
