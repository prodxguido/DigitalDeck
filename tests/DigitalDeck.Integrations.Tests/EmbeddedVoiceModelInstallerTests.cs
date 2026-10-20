using DigitalDeck.Integrations.Voice;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Getting the speech model onto a machine that only ever received an .exe.
/// </summary>
/// <remarks>
/// Same failure the Remote Script installer guards against: the feature is offered, the user says
/// yes, and nothing happens because the model was never on disk and there was no way to put it
/// there from inside the app.
/// </remarks>
public class EmbeddedVoiceModelInstallerTests : IDisposable
{
    private readonly string _voice;
    private readonly EmbeddedVoiceModelInstaller _installer = new();

    public EmbeddedVoiceModelInstallerTests()
    {
        _voice = Path.Combine(Path.GetTempPath(), "digitaldeck-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_voice)) Directory.Delete(_voice, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheModelTravelsInsideTheApp()
    {
        Assert.True(_installer.HasBundle, "el modelo de voz no quedó embebido en DigitalDeck.Integrations");
        Assert.StartsWith("vosk-model", _installer.BundledModelName);
    }

    [Fact]
    public void InstallingUnpacksAModelVoskWillLoad()
    {
        Assert.False(_installer.IsInstalled(_voice));

        var progress = new List<double>();
        var result = _installer.Install(_voice, new Progress<double>(progress.Add));

        Assert.True(result.Success, result.Message);
        Assert.True(_installer.IsInstalled(_voice));

        var model = VoskRecogniser.FindModel(_voice);
        Assert.NotNull(model);
        Assert.True(VoskRecogniser.IsModel(model!), "la carpeta desempaquetada no tiene el archivo que la marca como modelo");

        // The top folder is dropped on the way out: <voice>/<model>, not <voice>/<model>/<model>.
        Assert.Equal(_installer.BundledModelName, Path.GetFileName(model));
    }

    [Fact]
    public void InstallingOverAHalfFinishedUnpackReplacesIt()
    {
        var target = Path.Combine(_voice, _installer.BundledModelName);
        Directory.CreateDirectory(Path.Combine(target, "am"));
        File.WriteAllText(Path.Combine(target, "am", "final.mdl"), "truncated");

        var result = _installer.Install(_voice);

        Assert.True(result.Success, result.Message);
        Assert.True(VoskRecogniser.IsModel(target));
        Assert.NotEqual("truncated", File.ReadAllText(Path.Combine(target, "am", "final.mdl")));
    }

    [Fact]
    public void AnUnwritableVoiceDirectoryComesBackAsAMessageRatherThanAnException()
    {
        var result = _installer.Install(Path.Combine(_voice, "un\0nombre\0imposible"));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
