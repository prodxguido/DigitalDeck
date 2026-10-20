using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Services;

/// <summary>
/// Looks up the handler registered for a payload type and runs it, turning every failure into an
/// <see cref="ActionResult"/> the UI can display.
/// </summary>
public sealed class ActionDispatcher : IActionDispatcher
{
    private readonly Dictionary<Type, IActionHandler> _handlers;

    public ActionDispatcher(IEnumerable<IActionHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.PayloadType);
    }

    public async Task<ActionResult> DispatchAsync(ActionPayload? payload, CancellationToken ct = default)
    {
        if (payload is null)
            return ActionResult.Ok(); // A button with nothing configured yet is not an error.

        if (payload.Validate() is { } problem)
            return ActionResult.Failed(problem);

        if (payload.IsPotentiallyDangerous && !payload.Acknowledged)
            return ActionResult.Failed("Esta acción no fue confirmada en el editor.");

        if (!_handlers.TryGetValue(payload.GetType(), out var handler))
            return ActionResult.Failed($"No hay nada registrado para ejecutar {payload.GetType().Name}.");

        try
        {
            await handler.ExecuteAsync(payload, ct).ConfigureAwait(false);
            return ActionResult.Ok();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ActionResult.Failed(ex.Message);
        }
    }
}
