using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// Executes one kind of action. Each integration (shell, browser, and later Ableton, keystrokes,
/// MIDI) contributes handlers; nothing else in the app knows how an action is carried out.
/// </summary>
public interface IActionHandler
{
    Type PayloadType { get; }
    Task ExecuteAsync(ActionPayload payload, CancellationToken ct = default);
}

/// <summary>Typed base that saves every handler from repeating the cast.</summary>
public abstract class ActionHandler<TPayload> : IActionHandler
    where TPayload : ActionPayload
{
    public Type PayloadType => typeof(TPayload);

    public Task ExecuteAsync(ActionPayload payload, CancellationToken ct = default) =>
        payload is TPayload typed
            ? ExecuteAsync(typed, ct)
            : throw new ArgumentException(
                $"{GetType().Name} handles {typeof(TPayload).Name}, but got {payload.GetType().Name}.",
                nameof(payload));

    protected abstract Task ExecuteAsync(TPayload payload, CancellationToken ct);
}

/// <summary>Routes a button press to the handler that knows how to carry it out.</summary>
public interface IActionDispatcher
{
    Task<ActionResult> DispatchAsync(ActionPayload? payload, CancellationToken ct = default);
}

/// <summary>
/// Outcome of a button press. Failures are returned rather than thrown so the grid can show the
/// reason on the tile instead of the app dying on a bad path.
/// </summary>
public readonly record struct ActionResult(bool Success, string? Error = null)
{
    public static ActionResult Ok() => new(true);
    public static ActionResult Failed(string error) => new(false, error);
}
