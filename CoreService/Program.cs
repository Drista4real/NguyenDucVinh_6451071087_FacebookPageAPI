using CoreService.Options;
using CoreService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services
    .AddOptions<AiApiOptions>()
    .Bind(builder.Configuration.GetSection(AiApiOptions.SectionName));
builder.Services
    .AddOptions<ModerationOptions>()
    .Bind(builder.Configuration.GetSection(ModerationOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ICoreEventStore, PostgresCoreEventStore>();
builder.Services.AddHostedService<CoreDatabaseInitializerService>();

builder.Services.AddSingleton<RuleBasedAiAnalysisService>();
builder.Services.AddHttpClient<OpenAiAnalysisService>((services, client) =>
{
    var options = services
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<AiApiOptions>>()
        .Value
        .OpenAi;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddScoped<IAiAnalysisService, FallbackAiAnalysisService>();

builder.Services.AddScoped<IAutomationLogicService, AutomationLogicService>();
builder.Services.AddScoped<IKafkaEventProcessor, KafkaEventProcessor>();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddHostedService<KafkaConsumerWorkerService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
