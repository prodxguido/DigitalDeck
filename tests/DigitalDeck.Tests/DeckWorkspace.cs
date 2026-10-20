using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Microsoft.Data.Sqlite;

namespace DigitalDeck.Tests;

/// <summary>
/// A throwaway deck on disk: real SQLite file, real icon store, deleted when the test finishes.
/// The storage layer is where the tricky parts live (recursive queries, cascades, transactions),
/// so these tests exercise the actual database rather than a stand-in.
/// </summary>
public sealed class DeckWorkspace : IDisposable
{
    public DeckWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "digitaldeck-tests", Guid.NewGuid().ToString("n"));

        Paths = new DeckPaths(Root);
        Database = new DeckDatabase(Paths);
        Database.Migrate();

        Repository = new SqliteDeckRepository(Database);
        Settings = new SqliteSettingsStore(Database);
        Icons = new IconStore(Paths);
        Packs = new DeckPackService(Repository, Icons);
    }

    public string Root { get; }
    public DeckPaths Paths { get; }
    public DeckDatabase Database { get; }
    public IDeckRepository Repository { get; }
    public IAppSettingsStore Settings { get; }
    public IconStore Icons { get; }
    public DeckPackService Packs { get; }

    /// <summary>A valid 1×1 transparent PNG, for exercising the icon store end to end.</summary>
    public static byte[] SamplePng { get; } =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    };

    public string WriteSampleImage(string fileName = "icon.png")
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllBytes(path, SamplePng);
        return path;
    }

    public void Dispose()
    {
        // SQLite pools connections, which keeps the file handle open and blocks the delete.
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
