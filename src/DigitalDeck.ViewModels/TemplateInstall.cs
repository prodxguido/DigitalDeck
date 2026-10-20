using DigitalDeck.Data.Packs;

namespace DigitalDeck.ViewModels;

/// <summary>
/// What the templates picker came back with: the ones ticked, and what they should become.
/// </summary>
/// <remarks>
/// The two travel together because they are one decision, made on one screen. Asking again in a
/// second dialog would be a second thing to cancel out of, for an answer already given.
/// </remarks>
public sealed record TemplateInstall(IReadOnlyList<DeckTemplate> Templates, PackImportTarget Target)
{
    /// <summary>Nothing chosen — what a cancelled picker answers with.</summary>
    public static TemplateInstall None { get; } = new([], PackImportTarget.NewPage);
}
