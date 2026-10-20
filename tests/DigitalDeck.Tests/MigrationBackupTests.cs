using Dapper;
using DigitalDeck.Core;
using DigitalDeck.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The safety copy taken before a schema migration — the one moment an update can eat a deck.
/// </summary>
public class MigrationBackupTests : IDisposable
{
    private readonly string _root;
    private readonly DeckPaths _paths;

    public MigrationBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-backup-tests", Guid.NewGuid().ToString("n"));
        _paths = new DeckPaths(_root);
    }

    [Fact]
    public void AFreshDatabaseIsNotBackedUp()
    {
        new DeckDatabase(_paths).Migrate();

        // Nothing to lose yet, and a folder of empty copies teaches the user nothing.
        Assert.Empty(Directory.GetFiles(_paths.BackupsDirectory));
    }

    [Fact]
    public void MigratingAnExistingDeckLeavesACopyBehindFirst()
    {
        var profileId = GivenADeckOneVersionBehind();

        new DeckDatabase(_paths).Migrate();

        var backup = Assert.Single(Directory.GetFiles(_paths.BackupsDirectory, "deck-v1-*.db"));

        // The copy is the deck as it stood before the migration: openable, and still holding the
        // profile. WAL is why this matters — a plain file copy would have missed that last write.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backup,
            Mode = SqliteOpenMode.ReadOnly
        }.ConnectionString);

        connection.Open();

        Assert.Equal(1, connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(profileId, connection.ExecuteScalar<string>("SELECT id FROM profiles;"));
    }

    [Fact]
    public void ReopeningAnUpToDateDatabaseCopiesNothing()
    {
        GivenADeckOneVersionBehind();

        new DeckDatabase(_paths).Migrate();
        var afterFirst = Directory.GetFiles(_paths.BackupsDirectory).Length;

        new DeckDatabase(_paths).Migrate();

        Assert.Equal(1, afterFirst);
        Assert.Equal(afterFirst, Directory.GetFiles(_paths.BackupsDirectory).Length);
    }

    /// <summary>
    /// A deck with something in it, wound back to the schema before the usage table.
    /// </summary>
    /// <remarks>
    /// Winding <c>user_version</c> back is the only way to make a released migration run again; the
    /// table it adds is dropped too, so the migration finds the database it expects to find.
    /// </remarks>
    private string GivenADeckOneVersionBehind()
    {
        var database = new DeckDatabase(_paths);
        database.Migrate();

        var profileId = Guid.NewGuid().ToString("n");

        using (var connection = database.Open())
        {
            connection.Execute(
                "INSERT INTO profiles (id, name, sort_key, created_at) VALUES (@id, 'Estudio', 'a1', @now);",
                new { id = profileId, now = DateTimeOffset.UtcNow.ToString("o") });

            connection.Execute("DROP TABLE action_uses;");
            connection.Execute("PRAGMA user_version = 1;");
        }

        SqliteConnection.ClearAllPools();

        return profileId;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
