using System.Globalization;
using System.Text.Json;
using Dapper;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Serialization;
using DigitalDeck.Core.Services;

namespace DigitalDeck.Data;

/// <inheritdoc cref="IUsageTracker"/>
public sealed class SqliteUsageTracker : IUsageTracker
{
    /// <summary>
    /// How long it takes an untouched action to count half as much as it did.
    /// </summary>
    /// <remarks>
    /// Without decay the ranking would be a lifetime tally, and a lifetime tally is a list of what
    /// the user was doing the month they installed the app. Thirty days is short enough that
    /// switching from writing to mixing shows up within a project, and long enough that a week off
    /// does not empty the folder.
    /// </remarks>
    private const double HalfLifeDays = 30;

    private readonly DeckDatabase _database;

    public SqliteUsageTracker(DeckDatabase database) => _database = database;

    public async Task RecordAsync(DeckItem item, CancellationToken ct = default)
    {
        var signature = ActionSignature.For(item.Action);
        if (signature is null || item.Action is null) return;

        var now = DateTimeOffset.UtcNow;

        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        // The decay is computed here rather than in SQL on purpose: POWER() only exists in SQLite
        // builds compiled with the math functions, and whether the bundled native library has them
        // is not something a deck should depend on.
        var previous = await connection.QuerySingleOrDefaultAsync<UsageRow>(new CommandDefinition(
            "SELECT score, last_used_at AS lastUsedAt FROM action_uses WHERE signature = @signature;",
            new { signature }, cancellationToken: ct)).ConfigureAwait(false);

        var score = 1.0;

        if (previous is { } row)
        {
            var age = DateTimeOffset.TryParse(
                row.LastUsedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
                ? (now - last).TotalDays
                : 0;

            // A clock that went backwards must not inflate the score; the oldest it can be is now.
            score += row.Score * Math.Pow(0.5, Math.Max(age, 0) / HalfLifeDays);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO action_uses (signature, label, icon, color, data, uses, score, last_used_at)
            VALUES (@signature, @label, @icon, @color, @data, 1, @score, @now)
            ON CONFLICT(signature) DO UPDATE SET
                label        = excluded.label,
                icon         = excluded.icon,
                color        = excluded.color,
                data         = excluded.data,
                uses         = action_uses.uses + 1,
                score        = excluded.score,
                last_used_at = excluded.last_used_at;
            """,
            new
            {
                signature,
                label = item.Label,
                icon = item.IconGlyph,
                color = item.ColorHex,
                data = JsonSerializer.Serialize(item.Action, DeckJson.Compact),
                score,
                now = now.ToString("o", CultureInfo.InvariantCulture)
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<UsageEntry>> GetTopAsync(int limit, CancellationToken ct = default)
    {
        if (limit <= 0) return Array.Empty<UsageEntry>();

        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<UsageRow>(new CommandDefinition(
            """
            SELECT signature, label, icon, color, data, uses, score, last_used_at AS lastUsedAt
            FROM action_uses
            ORDER BY score DESC, last_used_at DESC
            LIMIT @limit;
            """,
            new { limit }, cancellationToken: ct)).ConfigureAwait(false);

        var entries = new List<UsageEntry>();

        foreach (var row in rows)
        {
            var action = Deserialise(row.Data);

            // A row whose payload no longer parses is one this version of the app cannot rebuild a
            // tile from. Skipping it costs one place in the ranking; failing would cost the folder.
            if (action is null) continue;

            entries.Add(new UsageEntry(
                row.Signature,
                row.Label,
                row.Icon,
                row.Color,
                action,
                row.Uses,
                row.Score,
                DateTimeOffset.TryParse(
                    row.LastUsedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                    ? when
                    : DateTimeOffset.MinValue));
        }

        return entries;
    }

    private static ActionPayload? Deserialise(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<ActionPayload>(data, DeckJson.Compact);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private sealed class UsageRow
    {
        public string Signature { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public string Color { get; set; } = DeckItem.DefaultColor;
        public string Data { get; set; } = string.Empty;
        public int Uses { get; set; }
        public double Score { get; set; }
        public string LastUsedAt { get; set; } = string.Empty;
    }
}
