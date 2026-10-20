using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Abstractions;

/// <summary>One ranked action, with everything needed to rebuild a tile for it.</summary>
/// <param name="Signature">See <see cref="Services.ActionSignature"/>.</param>
/// <param name="Score">Time-decayed weight; the ranking sorts by this, not by <paramref name="Uses"/>.</param>
public sealed record UsageEntry(
    string Signature,
    string Label,
    string? Icon,
    string ColorHex,
    ActionPayload Action,
    int Uses,
    double Score,
    DateTimeOffset LastUsed);

/// <summary>
/// Counts what gets pressed, so the deck can offer it back.
/// </summary>
/// <remarks>
/// Only presses that succeeded are recorded. A button pointing at a device this Live does not have
/// would otherwise climb the ranking by being pressed and failing, which is the opposite of what
/// the ranking is for.
/// </remarks>
public interface IUsageTracker
{
    /// <summary>Counts one successful press of <paramref name="item"/>.</summary>
    Task RecordAsync(DeckItem item, CancellationToken ct = default);

    /// <summary>The highest scoring actions, best first.</summary>
    Task<IReadOnlyList<UsageEntry>> GetTopAsync(int limit, CancellationToken ct = default);
}
