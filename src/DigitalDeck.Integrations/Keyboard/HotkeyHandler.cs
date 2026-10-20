using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Integrations.Keyboard;

/// <summary>
/// Presses a key combination in another program when a button configured for it is pressed.
/// </summary>
/// <remarks>
/// Nothing platform-specific in here: it reads the button and hands the combination to
/// <see cref="IHotkeySender"/>. Failures are thrown rather than swallowed so the dispatcher turns
/// them into the message on the status bar — "no encontré la ventana de Photoshop" is the whole
/// answer somebody needs, and a button that silently did nothing is the one thing worse.
/// </remarks>
public sealed class HotkeyHandler : ActionHandler<HotkeyAction>
{
    private readonly IHotkeySender _sender;

    public HotkeyHandler(IHotkeySender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    protected override Task ExecuteAsync(HotkeyAction payload, CancellationToken ct)
    {
        if (payload.Validate() is { } problem) throw new InvalidOperationException(problem);

        return _sender.SendAsync(payload.Spec!.Value, payload.Program, ct);
    }
}
