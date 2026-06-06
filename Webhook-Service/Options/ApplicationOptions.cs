namespace Webhook_Service.Options;

public sealed class FacebookWebhookOptions
{
    public const string SectionName = "Facebook";

    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public long MaxPayloadBytes { get; set; } = 2 * 1024 * 1024;
}

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string RawEventsTopic { get; set; } = "raw_events";
    public int PublishTimeoutSeconds { get; set; } = 10;
}
