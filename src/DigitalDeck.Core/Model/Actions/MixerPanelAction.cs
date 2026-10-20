namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Opens the mixer window: one fader per channel of the open Live set. Carries no data of its own —
/// which channels exist is Live's to say, and it changes while the window is open.
/// </summary>
public sealed class MixerPanelAction : ActionPayload
{
    public override string Summary => "Abrir el mixer de Ableton";
}
