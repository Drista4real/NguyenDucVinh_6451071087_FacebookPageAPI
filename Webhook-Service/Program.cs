using Microsoft.OpenApi.Models;
using Webhook_Service.Options;
using Webhook_Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<FacebookWebhookOptions>()
    .Bind(builder.Configuration.GetSection(FacebookWebhookOptions.SectionName));
builder.Services
    .AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName));

builder.Services.AddSingleton<IFacebookSignatureVerifier, FacebookSignatureVerifier>();
builder.Services.AddSingleton<IFacebookWebhookNormalizer, FacebookWebhookNormalizer>();
builder.Services.AddSingleton<IKafkaEventPublisher, KafkaEventPublisher>();

builder.Services.AddControllers(options =>
{
    options.MaxModelBindingCollectionSize = 10_000;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Facebook Webhook Service",
        Version = "v1",
        Description = "Verifies Meta webhook requests, normalizes events and publishes raw_events to Kafka."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.MapGet("/health", (
    Microsoft.Extensions.Options.IOptions<FacebookWebhookOptions> facebook,
    Microsoft.Extensions.Options.IOptions<KafkaOptions> kafka) =>
{
    var configured =
        !string.IsNullOrWhiteSpace(facebook.Value.VerifyToken) &&
        !string.IsNullOrWhiteSpace(facebook.Value.AppSecret) &&
        !string.IsNullOrWhiteSpace(kafka.Value.BootstrapServers);

    return Results.Ok(new
    {
        status = configured ? "healthy" : "degraded",
        service = "webhook-service",
        configured,
        timestamp = DateTimeOffset.UtcNow
    });
});

app.Run();

public partial class Program;
