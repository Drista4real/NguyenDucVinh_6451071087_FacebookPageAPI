using BackendAPI.Infrastructure;
using BackendAPI.Options;
using BackendAPI.Services;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<FacebookOptions>()
    .Bind(builder.Configuration.GetSection(FacebookOptions.SectionName));
builder.Services
    .AddOptions<KafkaOptions>()
    .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services
    .AddOptions<DashboardOptions>()
    .Bind(builder.Configuration.GetSection(DashboardOptions.SectionName));
builder.Services
    .AddOptions<CircuitBreakerOptions>()
    .Bind(builder.Configuration.GetSection(CircuitBreakerOptions.SectionName));

builder.Services.AddSingleton<FacebookCircuitBreaker>();
builder.Services.AddHttpClient<IFacebookService, FacebookService>((services, client) =>
{
    var options = services.GetRequiredService<
        Microsoft.Extensions.Options.IOptions<FacebookOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
builder.Services.AddSingleton<IKafkaPublisher, KafkaPublisher>();
builder.Services.AddHostedService<DatabaseInitializerService>();
builder.Services.AddHostedService<FacebookCommandConsumer>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Facebook Page Backend API",
        Version = "v1",
        Description = "Backend proxy for Meta Graph API and Kafka reply commands."
    });
    options.AddSecurityDefinition("DashboardApiKey", new OpenApiSecurityScheme
    {
        Name = "X-API-Key",
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Dashboard API key configured in Dashboard:ApiKey."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "DashboardApiKey"
            }
        }] = Array.Empty<string>()
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<DashboardApiKeyMiddleware>();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "backend-api",
    timestamp = DateTimeOffset.UtcNow
})).AllowAnonymous();

app.Run();

public partial class Program;
