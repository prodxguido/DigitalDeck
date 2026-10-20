using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Getting the Remote Script onto a machine that only ever received an .exe.
/// </summary>
/// <remarks>
/// The failure this guards against is silent and total: the app looks fine, the indicator says to
/// pick DigitalDeck in Control Surface, and it is not in the list because nothing ever copied it
/// there. Every assertion below is about that not happening.
/// </remarks>
public class RemoteScriptInstallerTests : IDisposable
{
    private readonly string _library;
    private readonly RemoteScriptInstaller _installer;

    public RemoteScriptInstallerTests()
    {
        _library = Path.Combine(Path.GetTempPath(), "digitaldeck-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_library);

        _installer = new RemoteScriptInstaller(
            liveIsRunning: () => false,
            userLibraries: new[] { _library });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_library)) Directory.Delete(_library, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Target => Path.Combine(_library, "Remote Scripts", "DigitalDeck");

    // ---------- What travels inside the app ----------

    [Fact]
    public void EveryModuleOfTheScriptIsCarriedInsideTheApp()
    {
        // A module added to the script and not embedded is an ImportError inside Live, reported in
        // a log nobody handed an .exe knows about. The same reason the command map has a test that
        // walks the whole enum.
        var source = Path.Combine(RepositoryRoot(), "src", "DigitalDeck.RemoteScript", "DigitalDeck");

        Assert.True(Directory.Exists(source), $"no encontré el script en {source}");

        var expected = Directory
            .EnumerateFiles(source, "*.py", SearchOption.AllDirectories)
            .Where(file => !file.Contains("__pycache__", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(source, file).Replace('\\', '/'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var bundled = _installer.BundledFiles.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, bundled);
    }

    [Fact]
    public void TheScriptCarriedInsideIsTheOneThisBuildSpeaks()
    {
        // Shipping an .exe that expects 2.2.0 while carrying 2.1.0 would install a mismatch by
        // hand and then complain about it.
        Assert.Equal(AbletonAdapter.ExpectedScriptVersion, _installer.BundledVersion);
    }

    [Fact]
    public void TheEntryPointLiveLooksForIsInThere()
    {
        Assert.Contains("__init__.py", _installer.BundledFiles.Keys);
        Assert.Contains("handlers/browser.py", _installer.BundledFiles.Keys);
    }

    // ---------- Looking at a machine ----------

    [Fact]
    public void AUserLibraryWithoutTheScriptSaysSoAndSaysWhereItWouldGo()
    {
        var state = _installer.Inspect();

        Assert.Equal(RemoteScriptStatus.NotInstalled, state.Status);
        Assert.True(state.NeedsAttention);
        Assert.Equal(Target, state.Location);
        Assert.Null(state.InstalledVersion);
    }

    [Fact]
    public void WithNoAbletonAnywhereItSaysItCouldNotTellRatherThanGuessing()
    {
        var blind = new RemoteScriptInstaller(() => false, Array.Empty<string>());
        var state = blind.Inspect();

        Assert.Equal(RemoteScriptStatus.Unknown, state.Status);
        Assert.False(state.NeedsAttention);
        Assert.False(string.IsNullOrWhiteSpace(state.Problem));
    }

    [Fact]
    public void OnlyFoldersThatAreReallyThereAreOffered()
    {
        // The window shows a folder picker whenever there is more than one candidate. A guess that
        // exists nowhere would turn that into a choice with a wrong answer in it.
        var real = new RemoteScriptInstaller();

        Assert.All(real.UserLibraries.Skip(1), library => Assert.True(Directory.Exists(library)));
        Assert.True(real.UserLibraries.Count <= 1 || real.UserLibraries.All(Directory.Exists));
    }

    // ---------- Installing ----------

    [Fact]
    public void InstallingPutsEveryFileWhereLiveLooksForIt()
    {
        var result = _installer.Install();

        Assert.True(result.Success, result.Message);
        Assert.Equal(Target, result.Location);

        foreach (var (relative, source) in _installer.BundledFiles)
        {
            var written = Path.Combine(Target, relative.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(written), $"falta {relative}");
            Assert.Equal(source, File.ReadAllText(written));
        }
    }

    [Fact]
    public void AfterInstallingItReportsItselfInstalled()
    {
        _installer.Install();

        var state = _installer.Inspect();

        Assert.Equal(RemoteScriptStatus.Installed, state.Status);
        Assert.False(state.NeedsAttention);
        Assert.Equal(_installer.BundledVersion, state.InstalledVersion);
    }

    [Fact]
    public void AnOlderInstallIsRecognisedAsOutdatedRatherThanLeftAlone()
    {
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "version.py"), "SCRIPT_VERSION = \"1.4.0\"\n");

        var before = _installer.Inspect();

        Assert.Equal(RemoteScriptStatus.Outdated, before.Status);
        Assert.Equal("1.4.0", before.InstalledVersion);
        Assert.True(before.NeedsAttention);

        _installer.Install();

        Assert.Equal(RemoteScriptStatus.Installed, _installer.Inspect().Status);
    }

    [Fact]
    public void InstallingClearsOutWhateverTheLastVersionLeftBehind()
    {
        // A module from an older version is one Live would still import, and the error it causes
        // points nowhere near a file nobody remembers putting there.
        Directory.CreateDirectory(Target);
        var stale = Path.Combine(Target, "handlers_that_no_longer_exist.py");
        File.WriteAllText(stale, "raise Exception('should be gone')");

        _installer.Install();

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void AnUnwritableTargetComesBackAsAMessageRatherThanAnException()
    {
        // The picker convention: an ordinary problem is text for the user, not a crash.
        var blocked = new RemoteScriptInstaller(
            () => false,
            new[] { Path.Combine(_library, "un\0nombre\0imposible") });

        var result = blocked.Install();

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>Walks up from the test binaries until the folder holding the solution turns up.</summary>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "DigitalDeck.sln")))
            folder = folder.Parent;

        Assert.True(folder is not null, "no encontré la raíz del repositorio desde los binarios de test");
        return folder!.FullName;
    }
}
