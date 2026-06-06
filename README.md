# Facebook Page API System

Hệ thống ASP.NET Core 8 gồm Backend API và Webhook Service theo yêu cầu trong
`fb_pages.pdf`.

## Chức năng

- `GET /posts`, `POST /post`, `GET /comments`
- API chi tiết dưới `/api/page`
- Đăng/xóa bài, đọc comment/reaction/insight, reply và ẩn comment
- Bảo vệ dashboard bằng header `X-API-Key`
- Retry exponential backoff cho lệnh đọc Facebook
- Circuit breaker: mặc định mở sau 5 lỗi tạm thời liên tiếp, half-open sau 30 giây
- Kafka consumer idempotent bằng bảng PostgreSQL `idempotency_keys`
- Publish lỗi sang `send_failed` để `retry-service` xử lý
- Swagger, Kafka UI, Prometheus và Alertmanager

## Webhook Service

`webhook-service` chạy ở port `3001` và cung cấp:

- `GET /webhook`: Meta callback verification qua `hub.mode`,
  `hub.verify_token` và `hub.challenge`
- `POST /webhook`: xác thực header `X-Hub-Signature-256` bằng HMAC-SHA256
- Normalize Page comment và Messenger message về cùng schema
- Bỏ qua Messenger echo để tránh vòng lặp phản hồi
- Publish event vào Kafka topic `raw_events` với `event_id` ổn định làm message key
- Giới hạn payload mặc định 2 MB và trả `503` khi Kafka lỗi để Meta retry

Secret không được lưu trong source. Cấu hình bằng:

```text
FACEBOOK_APP_SECRET=...
FACEBOOK_WEBHOOK_VERIFY_TOKEN=...
```

Callback URL khi deploy phải là HTTPS public:

```text
https://your-domain/webhook
```

## Cấu hình Facebook

Facebook App cần được cấp các quyền phù hợp với tính năng sử dụng, thường gồm
`pages_show_list`, `pages_read_engagement`, `pages_manage_posts`,
`pages_manage_engagement` và quyền insights nếu dashboard đọc insight. App ở
production cần App Review cho các quyền nâng cao.

Không ghi token vào `appsettings.json`. Tạo file `.env` từ `.env.example`:

```powershell
Copy-Item .env.example .env
```

Điền Page Access Token, Page ID và một API key đủ dài. Graph API version mặc định
là `v25.0` và có thể đổi bằng `Facebook__ApiVersion`.

## Chạy bằng Docker

```powershell
docker compose up -d --build
docker compose ps
```

- Swagger: http://localhost:3000/swagger
- Health: http://localhost:3000/health
- Webhook Swagger: http://localhost:3001/swagger
- Webhook Health: http://localhost:3001/health
- Facebook callback: http://localhost:3001/webhook
- Kafka UI: http://localhost:8080
- Prometheus: http://localhost:9090
- Alertmanager: http://localhost:9093

Thay Slack webhook trong `alertmanager/alertmanager.yml` trước khi dùng cảnh báo
thật.

## Chạy riêng backend

Kafka mặc định tắt trong `appsettings.json`, vì vậy có thể chạy REST API độc lập:

```powershell
$env:Facebook__PageAccessToken = "your-token"
$env:Facebook__DefaultPageId = "your-page-id"
$env:Dashboard__ApiKey = "your-api-key"
dotnet run --project BackendAPI
```

Gửi `X-API-Key` ở mọi endpoint trừ `/health` và `/swagger`. File
`BackendAPI/BackendAPI.http` có request mẫu.

## Kafka command

`reply_commands` và `send_retry` dùng cùng schema:

```json
{
  "command_id": "cmd-001",
  "event_id": "event-001",
  "action": "reply_comment",
  "target_id": "facebook-comment-id",
  "page_id": "facebook-page-id",
  "message": "Cảm ơn bạn đã liên hệ!",
  "retry_count": 0
}
```

Action hỗ trợ: `reply_comment`, `hide_comment`, `unhide_comment`,
`create_post`, `delete_post`. Lỗi được publish vào `send_failed` với command gốc,
`retry_count`, cờ `retryable`, thông báo lỗi và thời điểm lỗi.

## Kiểm tra

```powershell
dotnet build BackendAPI/BackendAPI.csproj
dotnet test Webhook-Service.Tests/Webhook-Service.Tests.csproj
docker compose config
```
