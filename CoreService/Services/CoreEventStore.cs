using CoreService.Models;
using Npgsql;
using NpgsqlTypes;

namespace CoreService.Services;

public interface ICoreEventStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> TryStartAsync(
        RawEvent rawEvent,
        TimeSpan processingLease,
        CancellationToken cancellationToken);
    Task<int> CountRecentUserEventsAsync(
        string userId,
        TimeSpan window,
        CancellationToken cancellationToken);
    Task<int> CountRecentUserSpamAsync(
        string userId,
        TimeSpan window,
        CancellationToken cancellationToken);
    Task<bool> IsBlacklistedAsync(string userId, CancellationToken cancellationToken);
    Task MarkBlacklistedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken);
    Task MarkCommandPublishedAsync(
        string eventId,
        AiAnalysisResult analysis,
        AutomationAction action,
        CancellationToken cancellationToken);
    Task MarkPendingReviewAsync(
        string eventId,
        AiAnalysisResult? analysis,
        string reason,
        CancellationToken cancellationToken);
    Task MarkSkippedAsync(
        string eventId,
        string reason,
        CancellationToken cancellationToken);
    Task MarkFailedAsync(
        string eventId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed class PostgresCoreEventStore : ICoreEventStore
{
    private readonly string? _connectionString;

    public PostgresCoreEventStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            CREATE TABLE IF NOT EXISTS core_event_processing (
                event_id VARCHAR(200) PRIMARY KEY,
                event_type VARCHAR(50) NOT NULL,
                page_id VARCHAR(100),
                user_id VARCHAR(100),
                target_id VARCHAR(200),
                status VARCHAR(30) NOT NULL,
                intent VARCHAR(50),
                sentiment VARCHAR(20),
                is_spam BOOLEAN NOT NULL DEFAULT FALSE,
                automation_action VARCHAR(50),
                failure_reason TEXT,
                received_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_core_event_processing_user_received
                ON core_event_processing (user_id, received_at);

            CREATE INDEX IF NOT EXISTS ix_core_event_processing_user_spam_received
                ON core_event_processing (user_id, is_spam, received_at);

            CREATE TABLE IF NOT EXISTS core_user_blacklist (
                user_id VARCHAR(100) PRIMARY KEY,
                reason TEXT NOT NULL,
                blacklisted_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryStartAsync(
        RawEvent rawEvent,
        TimeSpan processingLease,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO core_event_processing (
                event_id, event_type, page_id, user_id, target_id,
                status, received_at, updated_at)
            VALUES (
                @eventId, @eventType, @pageId, @userId, @targetId,
                'processing', @receivedAt, CURRENT_TIMESTAMP)
            ON CONFLICT (event_id) DO UPDATE
            SET status = 'processing',
                failure_reason = NULL,
                updated_at = CURRENT_TIMESTAMP
            WHERE core_event_processing.status = 'failed'
               OR (
                    core_event_processing.status = 'processing'
                    AND core_event_processing.updated_at < @staleBefore
               )
            RETURNING event_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", rawEvent.EventId);
        command.Parameters.AddWithValue("eventType", rawEvent.EventType);
        command.Parameters.AddWithValue("pageId", DbValue(rawEvent.PageId));
        command.Parameters.AddWithValue("userId", DbValue(rawEvent.UserId));
        command.Parameters.AddWithValue("targetId", DbValue(rawEvent.TargetId));
        command.Parameters.AddWithValue(
            "receivedAt",
            rawEvent.ReceivedAt == default ? DateTimeOffset.UtcNow : rawEvent.ReceivedAt);
        command.Parameters.AddWithValue(
            "staleBefore",
            DateTimeOffset.UtcNow.Subtract(processingLease));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    public Task<int> CountRecentUserEventsAsync(
        string userId,
        TimeSpan window,
        CancellationToken cancellationToken) =>
        CountAsync(
            """
            SELECT COUNT(*)
            FROM core_event_processing
            WHERE user_id = @userId
              AND received_at >= @since
              AND status <> 'failed';
            """,
            userId,
            window,
            cancellationToken);

    public Task<int> CountRecentUserSpamAsync(
        string userId,
        TimeSpan window,
        CancellationToken cancellationToken) =>
        CountAsync(
            """
            SELECT COUNT(*)
            FROM core_event_processing
            WHERE user_id = @userId
              AND received_at >= @since
              AND is_spam = TRUE
              AND status <> 'failed';
            """,
            userId,
            window,
            cancellationToken);

    public async Task<bool> IsBlacklistedAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT 1 FROM core_user_blacklist WHERE user_id = @userId;",
            connection);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task MarkBlacklistedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO core_user_blacklist (user_id, reason)
            VALUES (@userId, @reason)
            ON CONFLICT (user_id) DO UPDATE
            SET reason = EXCLUDED.reason,
                updated_at = CURRENT_TIMESTAMP;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task MarkCommandPublishedAsync(
        string eventId,
        AiAnalysisResult analysis,
        AutomationAction action,
        CancellationToken cancellationToken) =>
        MarkTerminalAsync(
            eventId,
            "command_published",
            analysis,
            action.ActionType,
            action.Reason,
            cancellationToken);

    public Task MarkPendingReviewAsync(
        string eventId,
        AiAnalysisResult? analysis,
        string reason,
        CancellationToken cancellationToken) =>
        MarkTerminalAsync(
            eventId,
            "pending_review",
            analysis,
            "pending_review",
            reason,
            cancellationToken);

    public Task MarkSkippedAsync(
        string eventId,
        string reason,
        CancellationToken cancellationToken) =>
        MarkTerminalAsync(
            eventId,
            "skipped",
            null,
            null,
            reason,
            cancellationToken);

    public Task MarkFailedAsync(
        string eventId,
        string reason,
        CancellationToken cancellationToken) =>
        MarkTerminalAsync(
            eventId,
            "failed",
            null,
            null,
            reason,
            cancellationToken);

    private async Task<int> CountAsync(
        string sql,
        string userId,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        command.Parameters.AddWithValue("since", DateTimeOffset.UtcNow.Subtract(window));

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private async Task MarkTerminalAsync(
        string eventId,
        string status,
        AiAnalysisResult? analysis,
        string? automationAction,
        string reason,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE core_event_processing
            SET status = @status,
                intent = COALESCE(@intent, intent),
                sentiment = COALESCE(@sentiment, sentiment),
                is_spam = COALESCE(@isSpam, is_spam),
                automation_action = COALESCE(@automationAction, automation_action),
                failure_reason = @reason,
                updated_at = CURRENT_TIMESTAMP
            WHERE event_id = @eventId;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("intent", DbValue(analysis?.Intent));
        command.Parameters.AddWithValue("sentiment", DbValue(analysis?.Sentiment));
        command.Parameters.Add(new NpgsqlParameter("isSpam", NpgsqlDbType.Boolean)
        {
            Value = analysis is null ? DBNull.Value : analysis.IsSpam
        });
        command.Parameters.AddWithValue("automationAction", DbValue(automationAction));
        command.Parameters.AddWithValue("reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "Missing ConnectionStrings:Postgres for Core Service state.");
        }
    }

    private static object DbValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}

public sealed class CoreDatabaseInitializerService : BackgroundService
{
    private readonly ICoreEventStore _store;
    private readonly ILogger<CoreDatabaseInitializerService> _logger;

    public CoreDatabaseInitializerService(
        ICoreEventStore store,
        ILogger<CoreDatabaseInitializerService> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _store.InitializeAsync(stoppingToken);
                _logger.LogInformation("Core Service PostgreSQL tables are ready");
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Cannot initialize Core Service PostgreSQL tables; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
