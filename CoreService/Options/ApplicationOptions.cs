namespace CoreService.Options;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";
    public string ConsumerGroupId { get; set; } = "core-service-group";
    public KafkaTopicsOptions Topics { get; set; } = new();
}

public sealed class KafkaTopicsOptions
{
    public string RawEvents { get; set; } = "raw_events";
    public string ReplyCommands { get; set; } = "reply_commands";
}

public sealed class AiApiOptions
{
    public const string SectionName = "AiApi";

    public string Provider { get; set; } = "OpenAI";
    public OpenAiOptions OpenAi { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5.4-mini";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxOutputTokens { get; set; } = 200;
}

public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    public int ProcessingLeaseMinutes { get; set; } = 5;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int RateLimitMaxComments { get; set; } = 20;
    public int BlacklistWindowHours { get; set; } = 24;
    public int BlacklistSpamThreshold { get; set; } = 3;
}
