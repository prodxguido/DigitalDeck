using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// That everything the app asks for on launch was actually registered.
/// </summary>
/// <remarks>
/// A missing registration compiles perfectly and fails exactly once: when the window that needs it
/// is resolved, in front of whoever opened the app. The container is inspected rather than built —
/// building it would open the real database and bind a real UDP port, and what is worth checking
/// here is the wiring, not the sockets.
/// </remarks>
public class ContainerTests
{
    private static readonly ServiceCollection Registered = Build();

    private static ServiceCollection Build()
    {
        var services = new ServiceCollection();
        App.ConfigureServices(services);

        return services;
    }

    private static bool Has<T>() => Registered.Any(service => service.ServiceType == typeof(T));

    /// <summary>The two widest constructors in the app, and the ones every launch goes through.</summary>
    public static TheoryData<Type> Resolved => new()
    {
        typeof(DeckDialogs),
        typeof(ViewModels.MainViewModel),
    };

    [Theory]
    [MemberData(nameof(Resolved))]
    public void EveryConstructorArgumentIsRegistered(Type type)
    {
        // A dependency added to one of these and not registered compiles, and then throws the first
        // time the app is opened — which is the last moment anyone wants to find out.
        var missing = type
            .GetConstructors()
            .Single()
            .GetParameters()
            .Where(parameter => Registered.All(service => service.ServiceType != parameter.ParameterType))
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void OpeningAtLoginIsWiredUp()
    {
        Assert.True(Has<IStartupRegistration>());
    }

    [Fact]
    public void TheMixerIsReachableFromAButton()
    {
        Assert.True(Has<IAbletonMixer>());

        var handlers = Registered
            .Where(service => service.ServiceType == typeof(IActionHandler))
            .Select(service => service.ImplementationType)
            .ToList();

        Assert.Contains(typeof(MixerPanelHandler), handlers);
    }

    [Fact]
    public void BothHalvesOfTheBridgeComeFromOneAdapter()
    {
        // The mixer's push subscription and the deck's commands travel over one socket. Two
        // adapters would each be deaf to half of what came back on it — and nothing about that
        // failure would look like a wiring mistake.
        Assert.True(Has<AbletonAdapter>());

        foreach (var face in new[] { typeof(IAbletonClient), typeof(IAbletonMixer) })
        {
            var registration = Registered.Single(service => service.ServiceType == face);

            Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
            Assert.NotNull(registration.ImplementationFactory);
            Assert.Null(registration.ImplementationType);
        }
    }

    [Fact]
    public void TheShellItselfIsRegistered()
    {
        Assert.True(Has<MainWindow>());
        Assert.True(Has<IDeckDialogs>());
    }
}
