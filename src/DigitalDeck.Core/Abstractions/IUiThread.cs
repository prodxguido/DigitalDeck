namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// The one thread that is allowed to touch what is on screen.
/// </summary>
/// <remarks>
/// Every UI toolkit has this rule and every one of them spells it differently. The view models need
/// it because most of what they react to does not arrive on that thread: the bridge with Live
/// raises its events on the socket's thread, and a collection changed from there throws.
/// <para>
/// Two members rather than one because "post it" is not always right. A change raised on the UI
/// thread has to be applied before the caller looks at the result, and posting it would leave the
/// caller reading the old value.
/// </para>
/// </remarks>
public interface IUiThread
{
    /// <summary>True when the calling code is already on the UI thread.</summary>
    bool IsCurrent { get; }

    /// <summary>Queues something to run on the UI thread, and returns without waiting.</summary>
    void Post(Action action);
}
