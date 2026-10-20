using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>Loads a browser item into Live's selected track when the button is pressed.</summary>
public sealed class AbletonLoadDeviceHandler : ActionHandler<AbletonLoadDeviceAction>
{
    private readonly IAbletonClient _client;

    public AbletonLoadDeviceHandler(IAbletonClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    protected override async Task ExecuteAsync(AbletonLoadDeviceAction payload, CancellationToken ct)
    {
        var result = await _client.LoadDeviceAsync(payload, ct).ConfigureAwait(false);

        if (!result.Success)
            throw new InvalidOperationException(result.Error ?? "No se pudo cargar el dispositivo.");
    }
}
