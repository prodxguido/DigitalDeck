using System.Windows.Threading;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Services;

/// <inheritdoc cref="IUiThread"/>
/// <remarks>
/// Bound to the dispatcher of whichever thread built it, which in this app is the one that builds
/// the container — the same thread WPF runs the windows on. Resolving it later from somewhere else
/// would capture a dispatcher nobody pumps, and every posted action would sit there forever.
/// </remarks>
public sealed class WpfUiThread : IUiThread
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public bool IsCurrent => _dispatcher.CheckAccess();

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}
