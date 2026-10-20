using DigitalDeck.Core.Services;

namespace DigitalDeck.Core.Voice;

/// <summary>A name that more than one tile answers to, and the tiles that share it.</summary>
public sealed record NameCollision(string Name, IReadOnlyList<DeckSearchHit> Tiles);

/// <summary>
/// Finds the buttons the voice assistant cannot tell apart.
/// </summary>
/// <remarks>
/// The assistant refuses to fire when two tiles reduce to the same spoken form rather than guess
/// between them, so a duplicate name silently disables a button. This groups the tiles the same way
/// the assistant compares them — <see cref="VoicePhrase.Normalise"/> — and reports every group with
/// a tie in it, so the duplicates can be found and renamed.
/// </remarks>
public static class NameCollisions
{
    public static IReadOnlyList<NameCollision> Find(IReadOnlyList<DeckSearchHit> hits)
    {
        return hits
            .Select(hit => (Name: VoicePhrase.Normalise(hit.Item.Label), Hit: hit))
            .Where(entry => entry.Name.Length > 0)
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => new NameCollision(
                group.Key,
                group.Select(entry => entry.Hit).ToList()))
            .ToList();
    }
}
