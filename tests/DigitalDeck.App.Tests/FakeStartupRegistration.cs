using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Tests;

/// <summary>Opening at login, with no Windows behind it.</summary>
public sealed class FakeStartupRegistration : IStartupRegistration
{
    private bool _enabled;

    /// <summary>Set to make every change fail, the way a locked-down account would.</summary>
    public string? Refuse { get; set; }

    public int Writes { get; private set; }

    public FakeStartupRegistration(bool enabled = false) => _enabled = enabled;

    public StartupState Read() => new(_enabled, _enabled ? "\"C:\\deck\\DigitalDeck.exe\"" : null);

    public StartupState Set(bool enabled)
    {
        if (Refuse is { } problem) return Read() with { Problem = problem };

        Writes++;
        _enabled = enabled;

        return Read();
    }
}
