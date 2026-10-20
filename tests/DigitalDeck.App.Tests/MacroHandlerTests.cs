using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// That a routine runs its steps in the order they were written, and reports what did not work.
/// </summary>
public sealed class MacroHandlerTests
{
    /// <summary>Records the urls it was asked to open; the one starting with «falla» throws.</summary>
    private sealed class RecordingUrlHandler : ActionHandler<OpenUrlAction>
    {
        public List<string> Opened { get; } = new();

        protected override Task ExecuteAsync(OpenUrlAction payload, CancellationToken ct)
        {
            if (payload.Url.Contains("falla", StringComparison.Ordinal))
                throw new InvalidOperationException("no abrió");

            Opened.Add(payload.Url);
            return Task.CompletedTask;
        }
    }

    private static (IActionDispatcher Dispatcher, RecordingUrlHandler Urls) Build()
    {
        var urls = new RecordingUrlHandler();

        var services = new ServiceCollection();
        services.AddSingleton<IActionHandler>(urls);
        services.AddSingleton<IActionHandler, MacroHandler>();
        services.AddSingleton<IActionDispatcher, ActionDispatcher>();

        return (services.BuildServiceProvider().GetRequiredService<IActionDispatcher>(), urls);
    }

    private static MacroStep Step(string url) =>
        new() { Action = new OpenUrlAction { Url = url } };

    [Fact]
    public async Task TheStepsRunInOrder()
    {
        var (dispatcher, urls) = Build();

        var result = await dispatcher.DispatchAsync(new MacroAction
        {
            Steps = { Step("https://uno.test"), Step("https://dos.test"), Step("https://tres.test") }
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal(new[] { "https://uno.test", "https://dos.test", "https://tres.test" }, urls.Opened);
    }

    /// <summary>
    /// The point of the default: Spotify missing should not cost you the playlist and the diario.
    /// </summary>
    [Fact]
    public async Task AFailedStepDoesNotStopTheRestButIsReported()
    {
        var (dispatcher, urls) = Build();

        var result = await dispatcher.DispatchAsync(new MacroAction
        {
            Steps = { Step("https://uno.test"), Step("https://falla.test"), Step("https://tres.test") },
            ContinueOnError = true
        });

        Assert.False(result.Success);
        Assert.Contains("paso 2", result.Error);
        Assert.Equal(new[] { "https://uno.test", "https://tres.test" }, urls.Opened);
    }

    [Fact]
    public async Task WithoutContinueOnErrorTheRoutineStopsAtTheFailure()
    {
        var (dispatcher, urls) = Build();

        var result = await dispatcher.DispatchAsync(new MacroAction
        {
            Steps = { Step("https://uno.test"), Step("https://falla.test"), Step("https://tres.test") },
            ContinueOnError = false
        });

        Assert.False(result.Success);
        Assert.Equal(new[] { "https://uno.test" }, urls.Opened);
    }
}
