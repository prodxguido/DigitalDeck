using System.Globalization;
using DigitalDeck.Core;
using Microsoft.Data.Sqlite;

namespace DigitalDeck.Data;

/// <summary>
/// Owns the SQLite connection string and the schema. SQLite is used rather than a single JSON
/// file because the app is edited live: a drag-and-drop move is one transactional row update, so
/// a crash mid-gesture loses that gesture rather than corrupting the whole configuration.
/// </summary>
public sealed class DeckDatabase
{
    /// <summary>Bump this and add a case to <see cref="Migrate"/> when the schema changes.</summary>
    private const int SchemaVersion = 2;

    /// <summary>
    /// How many pre-migration copies are kept.
    /// </summary>
    /// <remarks>
    /// The copy that matters is almost always the last one — the one taken by the update that
    /// broke something. Ten is enough to cover a run of releases without the folder quietly
    /// growing a copy of the deck for every version the user ever installed.
    /// </remarks>
    private const int BackupsToKeep = 10;

    private readonly string _connectionString;
    private readonly DeckPaths _paths;

    public DeckDatabase(DeckPaths paths)
    {
        paths.EnsureCreated();
        _paths = paths;

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabaseFile,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true
        }.ConnectionString;
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    /// <summary>Creates or upgrades the schema. Safe to call on every start.</summary>
    public void Migrate()
    {
        using var connection = Open();

        Execute(connection, "PRAGMA journal_mode = WAL;");

        var version = GetSchemaVersion(connection);
        if (version >= SchemaVersion) return;

        // Taken before a single statement runs, and only for a database that already holds
        // something. This is the one moment an update can destroy a deck: the exe lives apart from
        // the data and can be replaced freely, but a migration writes to the data itself, and a
        // migration with a bug in it writes to it wrongly. A .deckpack the user exported months
        // ago is not an answer to that.
        if (version >= 1) BackUp(connection, version);

        using var transaction = connection.BeginTransaction();

        if (version < 1) CreateInitialSchema(connection, transaction);
        if (version < 2) CreateUsageTable(connection, transaction);

        Execute(connection, $"PRAGMA user_version = {SchemaVersion};", transaction);
        transaction.Commit();
    }

    /// <summary>
    /// Writes a consistent copy of the database into the backups folder.
    /// </summary>
    /// <remarks>
    /// <c>VACUUM INTO</c> rather than a file copy, and the difference is not cosmetic: the database
    /// runs in WAL mode, so recent commits live in <c>deck.db-wal</c> and not yet in <c>deck.db</c>.
    /// Copying the one file would produce a backup missing exactly the most recent work — the work
    /// the user would most notice losing. This asks SQLite for the copy instead, and gets one file
    /// with everything in it.
    /// <para>
    /// A failure here stops the migration. That looks harsh for something as mundane as a full
    /// disk, but the alternative is to run the one operation that can destroy the deck with the
    /// safety net missing, which is precisely the case this exists for. Startup catches it and
    /// says so.
    /// </para>
    /// </remarks>
    private void BackUp(SqliteConnection connection, int version)
    {
        Directory.CreateDirectory(_paths.BackupsDirectory);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var file = Path.Combine(_paths.BackupsDirectory, $"deck-v{version}-{stamp}.db");

        // VACUUM INTO refuses to overwrite, which is the behaviour we want; two migrations inside
        // the same second are the only way to collide, and then the first copy is the one to keep.
        if (File.Exists(file)) return;

        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $file;";
        command.Parameters.AddWithValue("$file", file);
        command.ExecuteNonQuery();

        Prune();
    }

    /// <summary>Drops the oldest copies. Never lets tidying up fail a startup.</summary>
    private void Prune()
    {
        try
        {
            var stale = new DirectoryInfo(_paths.BackupsDirectory)
                .GetFiles("deck-v*.db")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(BackupsToKeep);

            foreach (var file in stale) file.Delete();
        }
        catch (IOException)
        {
            // A backup that could not be deleted is a backup that is still there. Harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static int GetSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void CreateInitialSchema(SqliteConnection connection, SqliteTransaction transaction) =>
        Execute(connection, """
            CREATE TABLE profiles (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                sort_key    TEXT NOT NULL,
                created_at  TEXT NOT NULL
            );

            CREATE TABLE pages (
                id          TEXT PRIMARY KEY,
                profile_id  TEXT NOT NULL REFERENCES profiles(id) ON DELETE CASCADE,
                name        TEXT NOT NULL,
                sort_key    TEXT NOT NULL,
                col_count   INTEGER NOT NULL,
                row_count   INTEGER NOT NULL
            );

            CREATE INDEX ix_pages_profile ON pages (profile_id, sort_key);

            CREATE TABLE items (
                id          TEXT PRIMARY KEY,
                page_id     TEXT NOT NULL REFERENCES pages(id) ON DELETE CASCADE,
                parent_id   TEXT NULL REFERENCES items(id) ON DELETE CASCADE,
                type        INTEGER NOT NULL,
                sort_key    TEXT NOT NULL,
                grid_col    INTEGER NOT NULL,
                grid_row    INTEGER NOT NULL,
                grid_w      INTEGER NOT NULL,
                grid_h      INTEGER NOT NULL,
                label       TEXT NOT NULL,
                icon_path   TEXT NULL,
                icon_glyph  TEXT NULL,
                color       TEXT NOT NULL,
                data        TEXT NULL
            );

            CREATE INDEX ix_items_page_parent ON items (page_id, parent_id, sort_key);
            CREATE INDEX ix_items_parent ON items (parent_id);

            CREATE TABLE settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """, transaction);

    /// <summary>
    /// What gets pressed, keyed by what the button does rather than by which tile did it.
    /// </summary>
    /// <remarks>
    /// One row per action, updated in place, instead of one row per press: the ranking only ever
    /// asks for the top handful, and a deck that is pressed all day would otherwise grow an event
    /// log forever to answer the same question. The cost is that history cannot be re-weighted
    /// after the fact — <c>score</c> decays as it is written, so the half-life is baked into the
    /// numbers already stored.
    /// <para>
    /// The payload is copied in so the ranking survives the tile it was learned from: deleting a
    /// button does not erase what it taught the deck.
    /// </para>
    /// </remarks>
    private static void CreateUsageTable(SqliteConnection connection, SqliteTransaction transaction) =>
        Execute(connection, """
            CREATE TABLE action_uses (
                signature    TEXT PRIMARY KEY,
                label        TEXT NOT NULL,
                icon         TEXT NULL,
                color        TEXT NOT NULL,
                data         TEXT NOT NULL,
                uses         INTEGER NOT NULL,
                score        REAL NOT NULL,
                last_used_at TEXT NOT NULL
            );

            CREATE INDEX ix_action_uses_score ON action_uses (score DESC);
            """, transaction);

    private static void Execute(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        command.ExecuteNonQuery();
    }
}
