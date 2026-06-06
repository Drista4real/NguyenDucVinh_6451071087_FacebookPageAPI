using RetryService.Models;
using RetryService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services
    .AddOptions<RetryPolicy>()
    .Bind(builder.Configuration.GetSection("RetryPolicy"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IRetryLogicService, RetryLogicService>();
builder.Services.AddSingleton<IBackoffDelay, TaskBackoffDelay>();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddScoped<RetryMessageProcessor>();
builder.Services.AddHostedService<KafkaConsumerWorkerService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
