# Retry Service

## Overview
The Retry Service is a critical component in the Facebook Page Management System that handles retry logic for failed message deliveries. It implements exponential backoff strategy and manages the Dead Letter Queue (DLQ) for messages that exceed maximum retry attempts.

## Features
- **Exponential Backoff Retry**: Automatically retries failed messages with exponential backoff (1s × 2^retry_count)
- **Configurable Retry Policy**: Maximum attempts, initial backoff, and backoff multiplier
- **Dead Letter Queue (DLQ)**: Routes messages to DLQ after max retries exceeded
- **Kafka Integration**: Consumes from `send_failed` topic and publishes to `send_retry` or `dead_letter`
- **Health Checks**: Endpoints for service health and readiness monitoring
- **Structured Logging**: Comprehensive logging for debugging and monitoring

## Architecture

```
Backend API
    │
    ├─ (Success) ─ Service complete
    │
    └─ (Failure) ─ Publish to send_failed topic
                      │
                      ▼
                Retry Service
                      │
        ┌─────────────┴─────────────┐
        │                           │
    (Retry < Max)             (Retry >= Max)
        │                           │
        ▼                           ▼
   send_retry topic           dead_letter topic
        │                           │
        └─ Backend API             Prometheus Alert
           (retry again)            Alertmanager
                                    (manual review)
```

## Project Structure

```
Retry-Service/
├── Controllers/
│   └── HealthController.cs          # Health and readiness endpoints
├── Models/
│   └── RetryModels.cs               # Message and policy models
├── Services/
│   ├── RetryLogicService.cs         # Retry decision logic
│   └── KafkaService.cs              # Kafka consumer/producer
├── Program.cs                        # Application entry point
├── appsettings.json                 # Configuration
├── appsettings.Development.json     # Development configuration
├── Retry-Service.csproj             # Project file
└── README.md                         # This file
```

## Models

### SendFailedMessage
Consumed from `send_failed` topic. Contains the command that failed to execute.
```csharp
public class SendFailedMessage
{
    public string CommandId { get; set; }      // Unique command identifier
    public string EventId { get; set; }         // Original event ID
    public string Action { get; set; }          // Action type (e.g., "reply", "hide")
    public string ReplyText { get; set; }       // Reply text (if action is reply)
    public DateTime Timestamp { get; set; }     // Original timestamp
    public int RetryCount { get; set; }         // Current retry count
    public string LastError { get; set; }       // Last error message
}
```

### SendRetryMessage
Published to `send_retry` topic for Backend API to retry.
```csharp
public class SendRetryMessage
{
    // Same as SendFailedMessage plus:
    public DateTime NextRetryTime { get; set; }  // When to retry next
}
```

### DeadLetterMessage
Published to `dead_letter` topic when max retries exceeded.
```csharp
public class DeadLetterMessage
{
    public string CommandId { get; set; }        // Unique command identifier
    public DateTime FailedAt { get; set; }       // When finally failed
    public int TotalRetries { get; set; }        // Total retry attempts
    public string Reason { get; set; }           // Why it was moved to DLQ
    // ... other fields
}
```

### RetryPolicy
Defines retry behavior.
```csharp
public class RetryPolicy
{
    public int MaxRetryAttempts { get; set; } = 5;        // Max 5 retries
    public int InitialBackoffSeconds { get; set; } = 1;   // Start with 1 second
    public double BackoffMultiplier { get; set; } = 2.0;  // Double each time
    public int MaxBackoffSeconds { get; set; } = 300;     // Cap at 5 minutes
}
```

## Exponential Backoff Algorithm

The retry delays follow this pattern:
```
Retry 1: 1 second     (1 × 2^0)
Retry 2: 2 seconds    (1 × 2^1)
Retry 3: 4 seconds    (1 × 2^2)
Retry 4: 8 seconds    (1 × 2^3)
Retry 5: 16 seconds   (1 × 2^4)

If max > 5: Capped at 300 seconds (5 minutes)
```

## Configuration

### appsettings.json
```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ConsumerGroupId": "retry-service-group",
    "Topics": {
      "SendFailed": "send_failed",
      "SendRetry": "send_retry",
      "DeadLetter": "dead_letter"
    }
  },
  "RetryPolicy": {
    "MaxRetryAttempts": 5,
    "InitialBackoffSeconds": 1,
    "BackoffMultiplier": 2.0,
    "MaxBackoffSeconds": 300
  }
}
```

## Running the Service

### Prerequisites
- .NET 8.0 SDK
- Kafka broker running on localhost:9092
- Kafka topics created: `send_failed`, `send_retry`, `dead_letter`

### Startup
```bash
cd Retry-Service
dotnet restore
dotnet run
```

The service will:
1. Start on port 3003
2. Connect to Kafka bootstrap server
3. Subscribe to `send_failed` topic
4. Begin consuming messages

### Health Checks
```bash
# Check service health
curl http://localhost:3003/api/health/health

# Check readiness
curl http://localhost:3003/api/health/ready
```

### Swagger UI
Access the API documentation at: `http://localhost:3003/swagger`

## Processing Flow

1. **Message Received**: Retry Service consumes message from `send_failed` topic
2. **Retry Decision**: 
   - If `retryCount < MaxRetryAttempts`: Proceed to retry
   - Otherwise: Move to Dead Letter Queue
3. **Backoff Calculation**: Calculate wait time: `1s × 2^retryCount` (capped at 300s)
4. **Publish Action**:
   - If retry: Publish to `send_retry` topic with `NextRetryTime`
   - If DLQ: Publish to `dead_letter` topic with failure reason
5. **Backend API**: Consumes `send_retry` messages and retries at `NextRetryTime`

## Monitoring

### Metrics to Track
- Total messages received from `send_failed`
- Messages retried to `send_retry`
- Messages moved to `dead_letter`
- Average processing time per message
- Consumer lag

### Prometheus Integration
The dead_letter topic is monitored by Prometheus:
- Alert triggers when messages appear in dead_letter
- Alertmanager sends notifications to Slack/Email
- Admin can review and handle manually via Kafka UI

### Key Logs
- `INFO`: Message consumption and retry decisions
- `WARNING`: Max retries reached, moving to DLQ
- `ERROR`: Messages moved to dead letter, processing errors

## Integration with Backend API

### Consuming send_retry messages
Backend API should:
1. Subscribe to `send_retry` topic
2. Wait until `NextRetryTime` has passed
3. Attempt to execute the command again
4. On failure: Publish back to `send_failed` topic
5. On success: Complete processing

## Development

### Adding Custom Retry Logic
Extend `IRetryLogicService`:
```csharp
public class CustomRetryLogicService : IRetryLogicService
{
    public (bool ShouldRetry, DateTime NextRetryTime, int BackoffSeconds) 
        DetermineRetryAction(SendFailedMessage message, RetryPolicy policy)
    {
        // Custom implementation
    }
}
```

### Testing
1. Publish test message to `send_failed` topic
2. Monitor logs for retry processing
3. Verify message appears in `send_retry` topic
4. Verify Prometheus alert fires when moving to DLQ

## Error Handling

- **Kafka Connection Lost**: Service logs error and retries connection every 5 seconds
- **Message Deserialization Error**: Message is skipped with warning log
- **Producer Failure**: Error is logged and exception is thrown
- **Timeout**: Handled by Kafka client configuration

## Performance Considerations

- **Consumer Group**: Runs in parallel with other instances if scaled
- **Batch Processing**: Processes one message at a time for clarity
- **Backoff Enforcement**: Relies on Backend API to respect `NextRetryTime`
- **Resource Usage**: Low memory footprint, mainly I/O bound

## Troubleshooting

### Service won't start
```bash
# Check if Kafka is running
docker ps | grep kafka

# Check logs
dotnet run --verbose
```

### Messages not being processed
1. Verify Kafka topics exist
2. Check consumer group in Kafka UI
3. Verify bootstrap servers in appsettings.json
4. Check application logs

### Messages stuck in send_failed
1. Verify Backend API is running
2. Check Backend API error logs
3. Ensure send_retry topic exists
4. Review the error message in send_failed messages

## Dependencies

- **Confluent.Kafka 2.4.0**: Kafka client library
- **Microsoft.AspNetCore.OpenApi**: API documentation support
- **Swashbuckle.AspNetCore 6.6.2**: Swagger UI
- **.NET 8.0**: Target framework

## License
Part of Facebook Page Management System

## Author
Backend Development Team
