# Core Service - Facebook Page Management System

## Tổng quan
Core Service là dịch vụ xử lý sự kiện theo thời gian thực từ Facebook. Nó thực hiện hai bước chính:
1. **Bước AI**: Phân loại intent (hỏi giá, khiếu nại, spam, v.v.) và phân tích sentiment (tích cực, trung tính, tiêu cực)
2. **Bước Automation**: Áp dụng rule engine để quyết định hành động tự động (reply, ẩn bình luận, chuyển review thủ công)

## Kiến trúc

```
Kafka Topic: raw_events
       ↓
KafkaConsumerWorkerService (Consume events)
       ↓
KafkaEventProcessor (Orchestrates processing)
       ├→ IAiAnalysisService (Analyze sentiment & intent)
       ├→ IAutomationLogicService (Determine action)
       └→ IKafkaProducerService (Publish reply_commands)
       ↓
Kafka Topic: reply_commands (Publish to Backend API)
```

## Các Component

### 1. Models (CoreService/Models/)
- **KafkaModels.cs**: RawEvent (input), ReplyCommand (output)
- **AiModels.cs**: AiAnalysisResult, AiAnalysisRequest, AutomationAction

### 2. Services (CoreService/Services/)

#### IAiAnalysisService
Phân tích bình luận và trả về sentiment, intent, spam detection:
- **DummyAiAnalysisService**: Pattern matching đơn giản (mặc định)
- **OpenAiAnalysisService**: Gọi OpenAI API (tùy chọn)

```csharp
// Input
var analysis = await aiService.AnalyzeCommentAsync("Shop ơi giá bao nhiêu?");

// Output
{
  "sentiment": "neutral",
  "intent": "ask_price",
  "isSpam": false,
  "spamType": "",
  "confidence": 0.85
}
```

#### IAutomationLogicService
Rule-based logic để quyết định hành động dựa trên kết quả AI:
- Spam → Hide comment
- Negative + complaint → Reply with apology
- Price inquiry → Reply with generic message
- Positive feedback → Reply with thank you

#### KafkaConsumerWorkerService
Background service consume từ topic `raw_events`:
- Tự động khởi động khi ứng dụng start
- Xử lý error và reconnect logic

#### KafkaProducerService
Publish ReplyCommand đến topic `reply_commands`:
- Sử dụng Acks.All để đảm bảo message đó được commit
- Retry logic built-in

## Cấu hình (appsettings.json)

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ConsumerGroupId": "core-service-group",
    "Topics": {
      "RawEvents": "raw_events",
      "ReplyCommands": "reply_commands"
    }
  },
  "AiApi": {
    "Provider": "Dummy",
    "OpenAi": {
      "ApiKey": "your-key-here",
      "Model": "gpt-3.5-turbo"
    }
  }
}
```

## Cách sử dụng

### 1. Khởi động Service
```bash
# Trong folder CoreService
dotnet restore
dotnet build
dotnet run
```

Service sẽ chạy ở port 3002 (được cấu hình trong launchSettings.json)

### 2. Health Check
```bash
curl http://localhost:3002/api/health/health
curl http://localhost:3002/api/health/ready
```

### 3. Chuyển từ Dummy sang OpenAI

Trong Program.cs, sửa dòng:
```csharp
// From:
builder.Services.AddScoped<IAiAnalysisService, DummyAiAnalysisService>();

// To:
builder.Services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();
```

Sau đó, cập nhật appsettings.json với OpenAI API key:
```json
"AiApi": {
  "Provider": "OpenAi",
  "OpenAi": {
    "ApiKey": "sk-...",
    "Model": "gpt-3.5-turbo"
  }
}
```

## Luồng xử lý chi tiết

### 1. Nhận Raw Event từ Kafka
```json
{
  "event_id": "evt_123",
  "timestamp": "2026-04-25T10:30:00Z",
  "comment_id": "cmt_456",
  "post_id": "post_789",
  "message": "Shop ơi giá bao nhiêu?",
  "user_id": "user_111",
  "user_name": "Khách hàng A"
}
```

### 2. Phân tích AI
```json
{
  "sentiment": "neutral",
  "intent": "ask_price",
  "isSpam": false,
  "confidence": 0.85
}
```

### 3. Rule Engine quyết định hành động
```
Rule: ask_price
→ Action: auto_reply
→ Message: "Cảm ơn bạn quan tâm! Vui lòng xem thông tin chi tiết..."
```

### 4. Publish Reply Command
```json
{
  "command_id": "cmd_999",
  "event_id": "evt_123",
  "comment_id": "cmt_456",
  "action": "auto_reply",
  "reply_text": "Cảm ơn bạn quan tâm! Vui lòng xem thông tin chi tiết...",
  "reason": "Price inquiry",
  "retry_count": 0,
  "timestamp": "2026-04-25T10:30:05Z"
}
```

## Error Handling

1. **AI Service lỗi**: Fallback sang DummyAiAnalysisService
2. **Kafka Producer lỗi**: Log error và retry
3. **Invalid message**: Log error, continue processing
4. **Kafka Consumer disconnect**: Tự động reconnect

## Metrics & Monitoring

Để theo dõi:
- Số events processed
- Số commands published
- Error rates
- Latency

Thêm health check endpoint: `/api/health/health` (port 3002)

## Integration với các Services

### Từ Webhook Service
Webhook Service publish event vào `raw_events` topic sau khi:
- Verify HMAC-SHA256 signature
- Normalize payload

### Tới Backend API
Backend API consume `reply_commands` từ topic để:
- Verify idempotency
- Call Facebook Graph API
- Handle failures

## Dependencies

```xml
<PackageReference Include="Confluent.Kafka" Version="2.3.0" />
<PackageReference Include="System.Text.Json" Version="4.7.2" />
<PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
```

## Troubleshooting

### Service không nhận events
1. Kiểm tra Kafka broker có chạy: `docker compose ps`
2. Kiểm tra topic `raw_events` có tồn tại
3. Kiểm tra logs: `docker compose logs core-service`

### AI analysis chậm
- Nếu dùng OpenAI: Kiểm tra API key
- Nếu OpenAI không hoạt động: Tự động fallback sang DummyAiAnalysisService

### Commands không được publish
- Kiểm tra Kafka producer connection
- Xem logs để tìm error
- Kiểm tra topic `reply_commands` có tồn tại

## Next Steps

1. Chạy service: `dotnet run`
2. Kích hoạt webhook event từ Facebook
3. Kiểm tra logs xem event được xử lý không
4. Kiểm tra Kafka UI (port 8080) để xem messages
5. Kiểm tra Backend API nhận reply_commands
