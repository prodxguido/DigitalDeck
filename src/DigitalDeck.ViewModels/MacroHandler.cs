using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalDeck.ViewModels;

/// <summary>
/// Runs the steps of a <see cref="MacroAction"/> one after the other, through the same dispatcher
/// a single button press goes through — so every action type works inside a macro for free.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher is resolved on each press rather than injected: the dispatcher is built from the
/// list of handlers, so asking for it in the constructor would be a cycle.
/// </para>
/// <para>
/// This handler lives in App, not Core, for the same reason the panel handlers do — a macro can
/// contain a step that opens a window, and those handlers only work on the UI thread. Nothing here
/// uses <c>ConfigureAwait(false)</c> on purpose: the awaits resume on the caller's context, which
/// keeps every step running on the thread the press arrived on.
/// </para>
/// </remarks>
public sealed class MacroHandler : ActionHandler<MacroAction>
{
    private readonly IServiceProvider _services;

    public MacroHandler(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    protected override async Task ExecuteAsync(MacroAction payload, CancellationToken ct)
    {
        var dispatcher = _services.GetRequiredService<IActionDispatcher>();
        var failures = new List<string>();

        for (var i = 0; i < payload.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var step = payload.Steps[i];

            if (step.DelayMs > 0)
                await Task.Delay(step.DelayMs, ct);

            var result = await dispatcher.DispatchAsync(step.Action, ct);

            if (result.Success) continue;

            var problem = $"paso {i + 1} ({step.Action.Summary}): {result.Error}";

            if (!payload.ContinueOnError)
                throw new InvalidOperationException($"Se cortó la secuencia en el {problem}");

            failures.Add(problem);
        }

        // Reported at the end rather than swallowed: the rest of the routine already ran, and the
        // grid still has to say which part of it did not.
        if (failures.Count > 0)
            throw new InvalidOperationException("Falló el " + string.Join("; el ", failures));
    }
}
