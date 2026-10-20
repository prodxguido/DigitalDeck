using Dapper;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Data;

/// <inheritdoc cref="IAppSettingsStore"/>
public sealed class SqliteSettingsStore : IAppSettingsStore
{
    private readonly DeckDatabase _database;

    public SqliteSettingsStore(DeckDatabase database) => _database = database;

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @key;", new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO settings (key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            new { key, value }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM settings WHERE key = @key;", new { key }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
