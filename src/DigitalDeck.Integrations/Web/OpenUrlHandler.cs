using System.Diagnostics;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Integrations.Web;

/// <summary>
/// Opens a web address in the default browser. The scheme is re-checked here rather than trusting
/// what was stored, so an edited database or an imported pack cannot make a button launch some
/// other protocol handler.
/// </summary>
public sealed class OpenUrlHandler : ActionHandler<OpenUrlAction>
{
    protected override Task ExecuteAsync(OpenUrlAction payload, CancellationToken ct)
    {
        if (!OpenUrlAction.TryParse(payload.Url, out var uri) || uri is null)
            throw new InvalidOperationException("Sólo se pueden abrir direcciones http o https.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
