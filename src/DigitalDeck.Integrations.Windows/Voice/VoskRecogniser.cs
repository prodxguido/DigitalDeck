using System.Text.Json;
using DigitalDeck.Core.Abstractions;
using Vosk;

namespace DigitalDeck.Integrations.Voice;

/// <summary>
/// Speech recognition through Vosk, offline and on this machine.
/// </summary>
/// <remarks>
/// Vosk rather than any of the alternatives for one reason above accuracy: it is Apache 2.0, so it
/// can be handed to whoever installs the app. The obvious competitor for wake words needs an access
/// key tied to an account and a paid plan to redistribute, which turns "download DigitalDeck" into
/// "download DigitalDeck and go make an account somewhere".
/// <para>
/// Everything stays here. No audio leaves the machine, which for an app that lives in a studio is
/// not a small thing to be able to say plainly.
/// </para>
/// </remarks>
public sealed class VoskRecogniser : IVoiceRecogniser
{
    /// <summary>The file that marks a directory as a Vosk model rather than just a folder.</summary>
    private const string ModelMarker = "am/final.mdl";

    /// <summary>
    /// What Vosk calls anything outside the grammar. Filtered out, because "the user said something
    /// that is not one of these buttons" reaches the matcher better as silence than as a word.
    /// </summary>
    private const string Unknown = "[unk]";

    private readonly object _gate = new();
    private readonly Model _model;

    private VoskRecognizer? _recogniser;
    private bool _disposed;

    /// <param name="modelPath">A directory holding an unpacked Vosk model.</param>
    /// <exception cref="DirectoryNotFoundException">There is no model there.</exception>
    public VoskRecogniser(string modelPath)
    {
        if (!IsModel(modelPath))
            throw new DirectoryNotFoundException($"No hay un modelo de voz en {modelPath}.");

        // Vosk writes a page of diagnostics to stderr as it loads. Useful when debugging Vosk
        // itself, noise in a WPF app that has nowhere to show it.
        Vosk.Vosk.SetLogLevel(-1);

        _model = new Model(modelPath);
    }

    /// <summary>
    /// Finds an installed model under <paramref name="voiceDirectory"/>, or returns null.
    /// </summary>
    /// <remarks>
    /// Searched rather than named, so replacing the small model with the large one is a matter of
    /// unpacking it next door — the folders carry their version in the name and hard-coding one
    /// would mean a code change to accept a better model.
    /// </remarks>
    public static string? FindModel(string voiceDirectory)
    {
        if (!Directory.Exists(voiceDirectory)) return null;

        if (IsModel(voiceDirectory)) return voiceDirectory;

        return Directory.EnumerateDirectories(voiceDirectory).FirstOrDefault(IsModel);
    }

    public static bool IsModel(string path) =>
        File.Exists(Path.Combine(path, ModelMarker.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Rebuilds the recogniser around a new set of phrases.
    /// </summary>
    /// <remarks>
    /// A whole new <see cref="VoskRecognizer"/> each time, because a grammar is fixed at
    /// construction and cannot be swapped on a live one. That is cheap — the expensive part is the
    /// <see cref="Model"/>, which is loaded once and shared across every one of these.
    /// </remarks>
    public void Restrict(IReadOnlyList<string> vocabulary)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            _recogniser?.Dispose();

            _recogniser = vocabulary.Count == 0
                ? new VoskRecognizer(_model, IVoiceInput.SampleRate)
                : new VoskRecognizer(_model, IVoiceInput.SampleRate, BuildGrammar(vocabulary));
        }
    }

    public string? Feed(short[] samples)
    {
        lock (_gate)
        {
            if (_disposed) return null;

            _recogniser ??= new VoskRecognizer(_model, IVoiceInput.SampleRate);

            // True means the speaker stopped and the phrase is final; false means they are still
            // going, and the partial result is not worth acting on.
            return _recogniser.AcceptWaveform(samples, samples.Length)
                ? TextOf(_recogniser.Result())
                : null;
        }
    }

    public string? Flush()
    {
        lock (_gate)
        {
            if (_disposed || _recogniser is null) return null;

            return TextOf(_recogniser.FinalResult());
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _recogniser?.Reset();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;

            _recogniser?.Dispose();
            _recogniser = null;

            _model.Dispose();
        }
    }

    /// <summary>
    /// The phrase list as Vosk wants it: a JSON array, with a slot for "none of these".
    /// </summary>
    /// <remarks>
    /// <see cref="Unknown"/> has to be in the list. Without it the grammar has no way to express
    /// that nothing fitted, so every cough and every half-sentence gets forced onto whichever
    /// button sounded least unlike it.
    /// </remarks>
    private static string BuildGrammar(IReadOnlyList<string> vocabulary) =>
        JsonSerializer.Serialize(vocabulary.Append(Unknown));

    /// <summary>Pulls the transcript out of the JSON Vosk answers with.</summary>
    private static string? TextOf(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("text", out var text)) return null;

            var heard = text.GetString();

            if (string.IsNullOrWhiteSpace(heard)) return null;

            // Everything outside the grammar comes back as the unknown token, sometimes several of
            // them. None of it names a button.
            heard = string.Join(' ', heard
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(word => word != Unknown));

            return string.IsNullOrWhiteSpace(heard) ? null : heard;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
