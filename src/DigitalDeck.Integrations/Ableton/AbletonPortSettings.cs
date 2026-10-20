using System.Globalization;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// The port pair the bridge is configured for, remembered between runs.
/// </summary>
/// <remarks>
/// Held in one object rather than read where it is needed because three places have to agree on
/// it and none of them can ask the others: the socket the app opens, the socket the Remote Script
/// opens, and the file the installer writes to tell the script which one that is. A disagreement
/// between any two of them looks identical to Ableton not being open.
/// <para>
/// Loaded once at startup, before the transport is built. The socket binds when
/// <see cref="IAbletonClient.Start"/> runs, so anything that changes the pair afterwards only
/// takes effect on the next launch — which is honest, and the same restart the script needs anyway.
/// </para>
/// </remarks>
public sealed class AbletonPortSettings
{
    private readonly IAppSettingsStore _settings;

    public AbletonPortSettings(IAppSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The pair in force for this run.</summary>
    public AbletonPorts Current { get; private set; } = AbletonPorts.Default;

    /// <summary>
    /// The pair the app will use next time it starts.
    /// </summary>
    /// <remarks>Differs from <see cref="Current"/> only between changing it and restarting.</remarks>
    public AbletonPorts Pending { get; private set; } = AbletonPorts.Default;

    public bool RestartNeeded => Pending != Current;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var script = await ReadAsync(SettingKeys.AbletonScriptPort, ct).ConfigureAwait(false);
        var app = await ReadAsync(SettingKeys.AbletonAppPort, ct).ConfigureAwait(false);

        var stored = new AbletonPorts(
            script ?? AbletonPorts.Default.ScriptListensOn,
            app ?? AbletonPorts.Default.ScriptRepliesTo);

        // A stored pair that no longer makes sense — hand-edited, or written by a build that
        // allowed something this one does not — falls back rather than refusing to start.
        Current = stored.Validate() is null ? stored : AbletonPorts.Default;
        Pending = Current;
    }

    /// <summary>Remembers a new pair. Takes effect on the next start; the caller says so.</summary>
    public async Task SaveAsync(AbletonPorts ports, CancellationToken ct = default)
    {
        if (ports.Validate() is { } problem) throw new ArgumentException(problem, nameof(ports));

        await _settings.SetAsync(
            SettingKeys.AbletonScriptPort,
            ports.ScriptListensOn.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);

        await _settings.SetAsync(
            SettingKeys.AbletonAppPort,
            ports.ScriptRepliesTo.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);

        Pending = ports;
    }

    private async Task<int?> ReadAsync(string key, CancellationToken ct)
    {
        var raw = await _settings.GetAsync(key, ct).ConfigureAwait(false);

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
