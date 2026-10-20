using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Tests;

/// <summary>A UI thread that is always the current one, so everything runs inline.</summary>
/// <remarks>
/// Better than a real dispatcher here, and not only because it is simpler. A test builds its view
/// model on the runner's thread and continues on whatever thread an <c>await</c> resumed on, so a
/// dispatcher captured in the constructor is usually a dispatcher nobody is pumping — anything
/// posted to it would never run and the assertion would fail for a reason that has nothing to do
/// with the code under test.
/// </remarks>
public sealed class ImmediateUiThread : IUiThread
{
    public bool IsCurrent => true;

    public void Post(Action action) => action();
}
