using System.Collections.Concurrent;
using System.Windows;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>A settings store that never touches disk.</summary>
public sealed class MemorySettingsStore : IAppSettingsStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();

    public int Writes { get; private set; }

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Writes++;
        _values[key] = value;

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _values.TryRemove(key, out _);

        return Task.CompletedTask;
    }
}

/// <summary>
/// That a window put somewhere comes back there, against the real monitors of this machine.
/// </summary>
/// <remarks>
/// Written after two bugs that a unit test over the arithmetic could not have found, because
/// neither was in the arithmetic. One saved device-independent units into a store whose reader
/// hands them to Win32 as physical pixels — invisible at 100% scaling and a window that walks
/// towards the corner at any other. The other saved on Closed, by which point the native handle is
/// gone and there is nothing left to measure, so nothing was ever written at all.
/// <para>
/// Both are only reachable by driving a real window on a real screen, which is what this does.
/// </para>
/// </remarks>
public sealed class WindowPlacementTests : IClassFixture<WpfThread>
{
    private readonly WpfThread _ui;

    public WindowPlacementTests(WpfThread ui) => _ui = ui;

    private static WindowPlacementService Service(IAppSettingsStore settings) =>
        new(new WindowsMonitors(), settings);

    /// <summary>A plain window of a known size, shown so it has a handle to measure.</summary>
    private static Window Open(double left, double top)
    {
        var window = new Window
        {
            Width = 620,
            Height = 400,
            Left = left,
            Top = top,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual
        };

        window.Show();

        return window;
    }

    [Fact]
    public void APositionSavedBeforeTheWindowClosesIsWrittenDown()
    {
        // The bug this catches: saving from Closed instead of Closing. By then the handle is zero,
        // the service gives up early, and the position is silently never stored.
        var settings = new MemorySettingsStore();

        _ui.Run(() =>
        {
            var window = Open(180, 160);
            var placement = Service(settings);

            try
            {
                window.Closing += (_, _) =>
                    placement.SaveAsync(window, false, null, SettingKeys.MixerPlacement).Wait();

                window.Close();
            }
            catch
            {
                window.Close();
                throw;
            }

            return true;
        });

        Assert.Equal(1, settings.Writes);
    }

    [Fact]
    public void SavingFromClosedWritesNothing()
    {
        // The other half of the same fact, stated so the reason Closing matters cannot be quietly
        // undone later by someone tidying the two handlers into one.
        var settings = new MemorySettingsStore();

        _ui.Run(() =>
        {
            var window = Open(180, 160);
            var placement = Service(settings);

            window.Closed += (_, _) =>
                placement.SaveAsync(window, false, null, SettingKeys.MixerPlacement).Wait();

            window.Close();

            return true;
        });

        Assert.Equal(0, settings.Writes);
    }

    [Fact]
    public void AWindowComesBackWhereItWasLeft()
    {
        // The DPI bug lives here: at 125% the saved numbers are four fifths of the real ones, so a
        // window restored from them lands short and shrinks, a little more each session.
        var settings = new MemorySettingsStore();

        var (before, after) = _ui.Run(() =>
        {
            var placement = Service(settings);

            var first = Open(240, 200);
            MonitorRect? saved;

            try
            {
                saved = placement.GetWindowBounds(first);
                first.Closing += (_, _) =>
                    placement.SaveAsync(first, false, null, SettingKeys.MixerPlacement).Wait();
                first.Close();
            }
            catch
            {
                first.Close();
                throw;
            }

            // A different window entirely, the way the next session's would be.
            var second = Open(900, 700);

            try
            {
                placement.RestoreAsync(second, SettingKeys.MixerPlacement).Wait();
                second.UpdateLayout();

                return (saved, placement.GetWindowBounds(second));
            }
            finally
            {
                second.Close();
            }
        });

        Assert.NotNull(before);
        Assert.NotNull(after);

        // Within a pixel or two: the round trip goes through integers in both directions.
        Assert.True(Math.Abs(before!.Value.Left - after!.Value.Left) <= 2, $"{before} vs {after}");
        Assert.True(Math.Abs(before.Value.Top - after.Value.Top) <= 2, $"{before} vs {after}");
        Assert.True(Math.Abs(before.Value.Width - after.Value.Width) <= 2, $"{before} vs {after}");
        Assert.True(Math.Abs(before.Value.Height - after.Value.Height) <= 2, $"{before} vs {after}");
    }

    [Fact]
    public void TheDeckAndTheMixerRememberSeparatePlaces()
    {
        // They are meant to live on different screens. One shared key would stack them.
        var settings = new MemorySettingsStore();

        var (deck, mixer) = _ui.Run(() =>
        {
            var placement = Service(settings);

            foreach (var (left, key) in new[]
                     {
                         (200.0, SettingKeys.WindowPlacement),
                         (700.0, SettingKeys.MixerPlacement)
                     })
            {
                var window = Open(left, 150);
                window.Closing += (_, _) => placement.SaveAsync(window, false, null, key).Wait();
                window.Close();
            }

            return (
                settings.GetAsync(SettingKeys.WindowPlacement).Result,
                settings.GetAsync(SettingKeys.MixerPlacement).Result);
        });

        Assert.NotNull(deck);
        Assert.NotNull(mixer);
        Assert.NotEqual(deck, mixer);
    }
}
