using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// Runs an Ableton action by handing it to the bridge.
/// </summary>
/// <remarks>
/// Thin on purpose: the dispatcher expects a handler to throw on failure and turns that into an
/// <see cref="ActionResult"/>, but the client already returns one with a far better message than
/// an exception would carry, so it is passed straight through by throwing only what the dispatcher
/// would have had to invent.
/// <para>
/// The one branch: commands Live has no API for — Export, Save, Save As — never touch the bridge.
/// They go to <see cref="ILiveKeyboard"/>, which presses Live's factory shortcut. That path works
/// with any user and needs only Live open, not the Remote Script.
/// </para>
/// </remarks>
public sealed class AbletonActionHandler : ActionHandler<AbletonAction>
{
    private readonly IAbletonClient _client;
    private readonly ILiveKeyboard _keyboard;

    public AbletonActionHandler(IAbletonClient client, ILiveKeyboard keyboard)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
    }

    protected override async Task ExecuteAsync(AbletonAction payload, CancellationToken ct)
    {
        if (payload.Info.ViaKeyboard)
        {
            await _keyboard.SendAsync(AbletonCommandMap.ToShortcut(payload.Command), ct).ConfigureAwait(false);
            return;
        }

        var result = await _client.ExecuteAsync(payload, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "No se pudo ejecutar la acción en Live.");
    }
}
