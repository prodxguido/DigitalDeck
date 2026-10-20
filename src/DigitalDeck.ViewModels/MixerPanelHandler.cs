using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.ViewModels;

/// <summary>
/// Opens the mixer window when a button configured for it is pressed.
/// </summary>
/// <remarks>
/// In the App project rather than Integrations because opening a window is UI work, and
/// Integrations has no WPF.
/// </remarks>
public sealed class MixerPanelHandler : ActionHandler<MixerPanelAction>
{
    private readonly IDeckDialogs _dialogs;

    public MixerPanelHandler(IDeckDialogs dialogs)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    protected override Task ExecuteAsync(MixerPanelAction payload, CancellationToken ct)
    {
        _dialogs.ShowMixer();
        return Task.CompletedTask;
    }
}
