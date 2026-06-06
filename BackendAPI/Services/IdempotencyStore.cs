using Npgsql;

namespace BackendAPI.Services;

public interface IIdempotencyStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> TryStartAsync(string commandId, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string commandId, CancellationToken cancellationToken);
    Task RemoveAsync(string commandId, CancellationToken cancellationToken);
}

public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    private readonly string? _connectionString;

    public PostgresIdempotencyStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Postgres");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            CREATE TABLE IF NOT EXISTS idempotency_keys (
                command_id VARCHAR(100) PRIMARY KEY,
                processed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                status VARCHAR(20) NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_idempotency_keys_processed_at
                ON idempotency_keys (processed_at);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryStartAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO idempotency_keys (command_id, status)
            VALUES (@commandId, 'processing')
            ON CONFLICT (command_id) DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("commandId", commandId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task MarkCompletedAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE idempotency_keys
            SET status = 'completed', processed_at = CURRENT_TIMESTAMP
            WHERE command_id = @commandId;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("commandId", commandId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql =
            "DELETE FROM idempotency_keys WHERE command_id = @commandId;";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("commandId", commandId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException(
                "Thiếu ConnectionStrings:Postgres cho Kafka idempotency.");
        }
    }
}
