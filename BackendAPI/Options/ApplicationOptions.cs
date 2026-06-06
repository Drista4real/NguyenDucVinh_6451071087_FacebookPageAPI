namespace BackendAPI.Options;

public sealed class FacebookOptions
{
    public const string SectionName = "Facebook";

    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v25.0";
    public string PageAccessToken { get; set; } = string.Empty;
    public string DefaultPageId { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public bool Enabled { get; set; }
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroup { get; set; } = "backend-api";
    public string ReplyCommandsTopic { get; set; } = "reply_commands";
    public string SendRetryTopic { get; set; } = "send_retry";
    public string SendFailedTopic { get; set; } = "send_failed";
}

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    public int FailureThreshold { get; set; } = 5;
    public int OpenSeconds { get; set; } = 30;
    public int ReadRetryCount { get; set; } = 3;
}
