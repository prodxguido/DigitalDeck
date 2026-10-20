using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// A test that opens a real microphone. Skipped everywhere else, so a normal <c>dotnet test</c>
/// stays green on a build agent with no sound card and nobody to talk into it.
/// </summary>
/// <remarks>
/// This exists to answer one question that no amount of unit testing can: whether the assistant can
/// hear anything <b>while Live holds the audio interface</b>. WASAPI and ASIO are separate paths to
/// the driver and should not collide, but "should not" is a claim about someone else's driver — so
/// run this with Live open, playing, before believing it on any given machine.
/// <code>
/// $env:DIGITALDECK_MIC = "1"
/// dotnet test --filter FullyQualifiedName~VoiceInputSmoke --logger "console;verbosity=detailed"
/// </code>
/// </remarks>
public sealed class MicrophoneFactAttribute : FactAttribute
{
    public const string Switch = "DIGITALDECK_MIC";

    /// <summary>Endpoint id to listen on, when the default is not the one being tested.</summary>
    public const string DeviceSwitch = "DIGITALDECK_MIC_DEVICE";

    public MicrophoneFactAttribute()
    {
        if (!IsOn(Switch))
            Skip = $"Abre el micrófono de verdad. Poné {Switch}=1 para correrlo.";
    }

    private static bool IsOn(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
}
