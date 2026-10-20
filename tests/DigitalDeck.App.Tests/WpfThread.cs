using System.Windows;
using System.Windows.Threading;

namespace DigitalDeck.App.Tests;

/// <summary>
/// One STA thread with a running dispatcher and the app's real theme, shared by every test that
/// needs a window.
/// </summary>
/// <remarks>
/// Shared rather than one per test, and that is not an optimisation. WPF's <see cref="Application"/>
/// is a single per-process object with thread affinity: tests that each spin up their own STA thread
/// race to create it, and whichever loses ends up building windows against an
/// <see cref="Application"/> that lives somewhere else. The failure that produces is worse than a
/// crash — it looks like the window under test is wrong.
/// </remarks>
public sealed class WpfThread : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Thread _thread;

    public WpfThread()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;

            if (Application.Current is null)
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

                foreach (var source in new[] { "Themes/Theme.xaml", "Themes/Controls.xaml" })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(
                            $"pack://application:,,,/DigitalDeck;component/{source}", UriKind.Absolute)
                    });
                }
            }

            ready.Set();
            Dispatcher.Run();
        });

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Start();

        ready.Wait();

        _dispatcher = dispatcher!;
    }

    /// <summary>Runs <paramref name="body"/> on the UI thread and brings back what it returned.</summary>
    public T Run<T>(Func<T> body) => _dispatcher.Invoke(body);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
