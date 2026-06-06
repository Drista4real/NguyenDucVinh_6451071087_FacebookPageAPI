using BackendAPI.Options;
using Microsoft.Extensions.Options;

namespace BackendAPI.Services;

public sealed class DatabaseInitializerService : BackgroundService
{
    private readonly KafkaOptions _kafkaOptions;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly ILogger<DatabaseInitializerService> _logger;

    public DatabaseInitializerService(
        IOptions<KafkaOptions> kafkaOptions,
        IIdempotencyStore idempotencyStore,
        ILogger<DatabaseInitializerService> logger)
    {
        _kafkaOptions = kafkaOptions.Value;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_kafkaOptions.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _idempotencyStore.InitializeAsync(stoppingToken);
                _logger.LogInformation("PostgreSQL idempotency table is ready");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Cannot initialize PostgreSQL; retrying in 5 seconds");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
