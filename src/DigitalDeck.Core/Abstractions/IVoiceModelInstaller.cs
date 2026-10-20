namespace DigitalDeck.Core.Abstractions;

/// <param name="Location">Where the model ended up, when it worked.</param>
public sealed record VoiceModelInstallResult(bool Success, string Message, string? Location = null);

/// <summary>
/// Puts the speech model where the voice assistant will find it.
/// </summary>
/// <remarks>
/// Vosk needs a folder of model files on disk before it can recognise a word, and that folder is
/// about sixty megabytes — too much to create at build time as loose files and too much to ask
/// someone handed an .exe to download and unzip by hand into the right place. So the model travels
/// inside the app as a single compressed resource, and this unpacks it into the voice directory the
/// first time someone says yes to the assistant.
/// <para>
/// Separate from turning the feature on: installing the model is a one-time copy, and it is offered
/// on first run before the assistant has ever been switched on.
/// </para>
/// </remarks>
public interface IVoiceModelInstaller
{
    /// <summary>True when this build carries a model to install. False in a trimmed or test build.</summary>
    bool HasBundle { get; }

    /// <summary>The name of the model folder the bundle unpacks to, for messages.</summary>
    string BundledModelName { get; }

    /// <summary>True when a usable model is already unpacked under <paramref name="voiceDirectory"/>.</summary>
    bool IsInstalled(string voiceDirectory);

    /// <summary>
    /// Unpacks the bundled model into <paramref name="voiceDirectory"/>, replacing a half-finished
    /// unpack from a previous attempt. Never throws — a problem comes back in the result.
    /// </summary>
    /// <param name="progress">Fraction unpacked, 0 to 1. Called on the calling thread.</param>
    VoiceModelInstallResult Install(string voiceDirectory, IProgress<double>? progress = null);
}
