using DigitalDeck.Integrations.Windows;
using Microsoft.Win32;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Opening at login, driven against a real registry key.
/// </summary>
/// <remarks>
/// A scratch key of the app's own under HKEY_CURRENT_USER, never the real Run key: that one is
/// shared with every other program on the machine, and a test that wrote to it would be changing
/// what the user's computer does at login. Everything below is deleted afterwards.
/// </remarks>
public sealed class WindowsStartupRegistrationTests : IDisposable
{
    private const string Scratch = @"Software\DigitalDeck\Tests";

    /// <summary>One key per test: xUnit runs these in parallel and they must not share a value.</summary>
    private readonly string _key = $@"{Scratch}\{Guid.NewGuid():n}";

    private WindowsStartupRegistration For(string? executable = @"C:\Program Files\Deck\DigitalDeck.exe") =>
        new(_key, () => executable);

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);

            // And the branch it hung from, so a test run leaves the registry as it found it. Only
            // while empty, which is what makes this safe against the test running beside this one:
            // if that one still has its key, the parent is not empty and nothing happens here.
            DeleteIfEmpty(Scratch);
            DeleteIfEmpty(@"Software\DigitalDeck");
        }
        catch (Exception) { /* A leftover scratch key is not worth failing a run over. */ }
    }

    private static void DeleteIfEmpty(string path)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(path))
        {
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0) return;
        }

        Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    private string? Written()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_key);

        return key?.GetValue(WindowsStartupRegistration.ValueName) as string;
    }

    [Fact]
    public void ItStartsOff()
    {
        var state = For().Read();

        Assert.False(state.Enabled);
        Assert.Null(state.Problem);
    }

    [Fact]
    public void TurningItOnRegistersTheRunningExecutable()
    {
        var state = For().Set(true);

        Assert.True(state.Enabled);
        Assert.Null(state.Problem);
        Assert.Contains("DigitalDeck.exe", Written()!);
    }

    [Fact]
    public void ThePathIsQuoted()
    {
        // The folder this app actually ships in has spaces in its name. Unquoted, Windows would
        // take the first word of the path as the program and the rest as arguments to it — an
        // autostart entry that exists, looks right in the Task Manager, and silently never runs.
        For().Set(true);

        var written = Written()!;

        Assert.StartsWith("\"", written);
        Assert.EndsWith("\"", written);
    }

    [Fact]
    public void TurningItOffRemovesTheEntryRatherThanBlankingIt()
    {
        var startup = For();

        startup.Set(true);
        var state = startup.Set(false);

        Assert.False(state.Enabled);
        Assert.Null(Written());
    }

    [Fact]
    public void TurningItOffWhenItWasNeverOnIsNotAnError()
    {
        var state = For().Set(false);

        Assert.False(state.Enabled);
        Assert.Null(state.Problem);
    }

    [Fact]
    public void TurningItOnTwiceLeavesOneEntry()
    {
        var startup = For();

        startup.Set(true);
        startup.Set(true);

        using var key = Registry.CurrentUser.OpenSubKey(_key);

        Assert.Equal(
            new[] { WindowsStartupRegistration.ValueName },
            key!.GetValueNames());
    }

    [Fact]
    public void ReEnablingFromAnotherCopyRepointsTheEntry()
    {
        // Three builds of this app live side by side in the repository. Turning the setting on is
        // how the user says which one should open, so it has to overwrite rather than refuse.
        new WindowsStartupRegistration(_key, () => @"C:\uno\DigitalDeck.exe").Set(true);
        new WindowsStartupRegistration(_key, () => @"C:\dos\DigitalDeck.exe").Set(true);

        Assert.Contains(@"C:\dos", Written()!);
    }

    [Fact]
    public void AnAppThatCannotSayWhereItIsRefusesInsteadOfWritingNonsense()
    {
        var state = For(executable: null).Set(true);

        Assert.False(state.Enabled);
        Assert.NotNull(state.Problem);
        Assert.Null(Written());
    }

    [Fact]
    public void AnEntryPointingSomewhereElseIsSpotted()
    {
        For().Set(true);

        var state = For().Read();

        Assert.True(state.Enabled);
        Assert.True(state.PointsElsewhere(@"C:\otra\ruta\DigitalDeck.exe"));
        Assert.False(state.PointsElsewhere(@"C:\Program Files\Deck\DigitalDeck.exe"));
    }
}
