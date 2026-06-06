using CoreService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register Kafka and AI services
builder.Services.AddScoped<IAiAnalysisService, DummyAiAnalysisService>();
// Uncomment the line below to use OpenAI instead:
// builder.Services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();

builder.Services.AddScoped<IAutomationLogicService, AutomationLogicService>();
builder.Services.AddScoped<IKafkaEventProcessor, KafkaEventProcessor>();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();
builder.Services.AddHostedService<KafkaConsumerWorkerService>();

// Add HttpClient for OpenAI integration
builder.Services.AddHttpClient<OpenAiAnalysisService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

