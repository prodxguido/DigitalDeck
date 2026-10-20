using System.Text.Json;
using Dapper;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Core.Serialization;
using Microsoft.Data.Sqlite;

namespace DigitalDeck.Data;

/// <inheritdoc cref="IDeckRepository"/>
public sealed class SqliteDeckRepository : IDeckRepository
{
    private const string ItemColumns = """
        id AS Id, page_id AS PageId, parent_id AS ParentId, type AS Type, sort_key AS SortKey,
        grid_col AS GridCol, grid_row AS GridRow, grid_w AS GridW, grid_h AS GridH,
        label AS Label, icon_path AS IconPath, icon_glyph AS IconGlyph, color AS Color, data AS Data
        """;

    private const string InsertItemSql = """
        INSERT INTO items (id, page_id, parent_id, type, sort_key, grid_col, grid_row, grid_w, grid_h,
                           label, icon_path, icon_glyph, color, data)
        VALUES (@Id, @PageId, @ParentId, @Type, @SortKey, @GridCol, @GridRow, @GridW, @GridH,
                @Label, @IconPath, @IconGlyph, @Color, @Data);
        """;

    private readonly DeckDatabase _database;

    public SqliteDeckRepository(DeckDatabase database) => _database = database;

    // ---------- Profiles ----------

    public async Task<IReadOnlyList<Profile>> GetProfilesAsync(CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ProfileRow>(new CommandDefinition(
            "SELECT id AS Id, name AS Name, sort_key AS SortKey, created_at AS CreatedAt FROM profiles ORDER BY sort_key;",
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToProfile()).ToList();
    }

    public async Task<Profile> CreateProfileAsync(string name, bool createDefaultPage = true, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var lastKey = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sort_key FROM profiles ORDER BY sort_key DESC LIMIT 1;",
            transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);

        var profile = Profile.New(name, FractionalIndex.Last(lastKey));

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO profiles (id, name, sort_key, created_at) VALUES (@Id, @Name, @SortKey, @CreatedAt);",
            new { profile.Id, profile.Name, profile.SortKey, CreatedAt = profile.CreatedAt.ToString("O") },
            transaction, cancellationToken: ct)).ConfigureAwait(false);

        if (createDefaultPage)
        {
            // A profile with no pages has nothing to show, so give it one to start from.
            var page = Page.New(profile.Id, "Página 1", FractionalIndex.First());
            await InsertPageAsync(connection, transaction, page, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return profile;
    }

    public async Task RenameProfileAsync(string profileId, string name, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE profiles SET name = @name WHERE id = @profileId;",
            new { profileId, name }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteProfileAsync(string profileId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM profiles WHERE id = @profileId;",
            new { profileId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<Profile> DuplicateProfileAsync(string profileId, string newName, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var lastKey = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sort_key FROM profiles ORDER BY sort_key DESC LIMIT 1;",
            transaction: transaction, cancellationToken: ct)).ConfigureAwait(false);

        var copy = Profile.New(newName, FractionalIndex.Last(lastKey));

        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO profiles (id, name, sort_key, created_at) VALUES (@Id, @Name, @SortKey, @CreatedAt);",
            new { copy.Id, copy.Name, copy.SortKey, CreatedAt = copy.CreatedAt.ToString("O") },
            transaction, cancellationToken: ct)).ConfigureAwait(false);

        var pages = await connection.QueryAsync<PageRow>(new CommandDefinition(
            "SELECT id AS Id, profile_id AS ProfileId, name AS Name, sort_key AS SortKey, col_count AS Columns, row_count AS Rows FROM pages WHERE profile_id = @profileId ORDER BY sort_key;",
            new { profileId }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        foreach (var sourcePage in pages)
        {
            var newPage = Page.New(copy.Id, sourcePage.Name, sourcePage.SortKey, sourcePage.Columns, sourcePage.Rows);
            await InsertPageAsync(connection, transaction, newPage, ct).ConfigureAwait(false);

            var items = (await connection.QueryAsync<ItemRow>(new CommandDefinition(
                $"SELECT {ItemColumns} FROM items WHERE page_id = @pageId;",
                new { pageId = sourcePage.Id }, transaction, cancellationToken: ct)).ConfigureAwait(false))
                .Select(r => r.ToItem())
                .ToList();

            foreach (var clone in RemapIds(items, newPage.Id))
                await connection.ExecuteAsync(new CommandDefinition(
                    InsertItemSql, ItemParameters(clone), transaction, cancellationToken: ct)).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return copy;
    }

    // ---------- Pages ----------

    public async Task<IReadOnlyList<Page>> GetPagesAsync(string profileId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<PageRow>(new CommandDefinition(
            "SELECT id AS Id, profile_id AS ProfileId, name AS Name, sort_key AS SortKey, col_count AS Columns, row_count AS Rows FROM pages WHERE profile_id = @profileId ORDER BY sort_key;",
            new { profileId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToPage()).ToList();
    }

    public async Task<Page?> GetPageAsync(string pageId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<PageRow>(new CommandDefinition(
            "SELECT id AS Id, profile_id AS ProfileId, name AS Name, sort_key AS SortKey, col_count AS Columns, row_count AS Rows FROM pages WHERE id = @pageId;",
            new { pageId }, cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToPage();
    }

    public async Task<Page> CreatePageAsync(string profileId, string name, int columns, int rows, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var lastKey = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT sort_key FROM pages WHERE profile_id = @profileId ORDER BY sort_key DESC LIMIT 1;",
            new { profileId }, cancellationToken: ct)).ConfigureAwait(false);

        var page = Page.New(profileId, name, FractionalIndex.Last(lastKey), columns, rows);
        await InsertPageAsync(connection, null, page, ct).ConfigureAwait(false);
        return page;
    }

    public async Task UpdatePageAsync(Page page, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE pages SET name = @Name, col_count = @Columns, row_count = @Rows, sort_key = @SortKey WHERE id = @Id;",
            new { page.Id, page.Name, page.Columns, page.Rows, page.SortKey },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeletePageAsync(string pageId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM pages WHERE id = @pageId;", new { pageId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // ---------- Items ----------

    public async Task<IReadOnlyList<DeckItem>> GetChildrenAsync(string pageId, string? parentId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var sql = parentId is null
            ? $"SELECT {ItemColumns} FROM items WHERE page_id = @pageId AND parent_id IS NULL ORDER BY sort_key;"
            : $"SELECT {ItemColumns} FROM items WHERE page_id = @pageId AND parent_id = @parentId ORDER BY sort_key;";

        var rows = await connection.QueryAsync<ItemRow>(new CommandDefinition(
            sql, new { pageId, parentId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<IReadOnlyList<DeckItem>> GetSubtreeAsync(string itemId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ItemRow>(new CommandDefinition($"""
            WITH RECURSIVE subtree(id) AS (
                SELECT id FROM items WHERE id = @itemId
                UNION ALL
                SELECT child.id FROM items child JOIN subtree ON child.parent_id = subtree.id
            )
            SELECT {ItemColumns} FROM items WHERE id IN (SELECT id FROM subtree) ORDER BY sort_key;
            """, new { itemId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<IReadOnlyList<DeckItem>> GetPageItemsAsync(string pageId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ItemRow>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM items WHERE page_id = @pageId ORDER BY sort_key;",
            new { pageId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<IReadOnlyList<DeckItem>> GetProfileItemsAsync(string profileId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ItemRow>(new CommandDefinition($"""
            SELECT {ItemColumns} FROM items
            WHERE page_id IN (SELECT id FROM pages WHERE profile_id = @profileId)
            ORDER BY sort_key;
            """, new { profileId }, cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(r => r.ToItem()).ToList();
    }

    public async Task<DeckItem?> GetItemAsync(string itemId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        var row = await connection.QuerySingleOrDefaultAsync<ItemRow>(new CommandDefinition(
            $"SELECT {ItemColumns} FROM items WHERE id = @itemId;",
            new { itemId }, cancellationToken: ct)).ConfigureAwait(false);

        return row?.ToItem();
    }

    public async Task AddItemAsync(DeckItem item, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            InsertItemSql, ItemParameters(item), cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task AddItemsAsync(IEnumerable<DeckItem> items, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // parent_id is a foreign key onto this same table, so parents have to land first.
        foreach (var item in DeckTree.ParentsFirst(items))
            await connection.ExecuteAsync(new CommandDefinition(
                InsertItemSql, ItemParameters(item), transaction, cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateItemAsync(DeckItem item, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE items SET
                parent_id = @ParentId, type = @Type, sort_key = @SortKey,
                grid_col = @GridCol, grid_row = @GridRow, grid_w = @GridW, grid_h = @GridH,
                label = @Label, icon_path = @IconPath, icon_glyph = @IconGlyph,
                color = @Color, data = @Data
            WHERE id = @Id;
            """, ItemParameters(item), cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task DeleteItemAsync(string itemId, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        // Descendants go with it via ON DELETE CASCADE on items.parent_id.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM items WHERE id = @itemId;", new { itemId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task MoveItemAsync(string itemId, string? newParentId, int col, int row, string sortKey, CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE items SET parent_id = @newParentId, grid_col = @col, grid_row = @row, sort_key = @sortKey WHERE id = @itemId;",
            new { itemId, newParentId, col, row, sortKey }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SwapPositionsAsync(
        string firstId, int firstCol, int firstRow,
        string secondId, int secondCol, int secondRow,
        CancellationToken ct = default)
    {
        await using var connection = await _database.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        const string sql = "UPDATE items SET grid_col = @col, grid_row = @row WHERE id = @id;";

        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { id = firstId, col = firstCol, row = firstRow }, transaction, cancellationToken: ct)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { id = secondId, col = secondCol, row = secondRow }, transaction, cancellationToken: ct)).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    // ---------- Helpers ----------

    private static Task InsertPageAsync(SqliteConnection connection, SqliteTransaction? transaction, Page page, CancellationToken ct) =>
        connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO pages (id, profile_id, name, sort_key, col_count, row_count) VALUES (@Id, @ProfileId, @Name, @SortKey, @Columns, @Rows);",
            new { page.Id, page.ProfileId, page.Name, page.SortKey, page.Columns, page.Rows },
            transaction, cancellationToken: ct));

    /// <summary>Clones a set of items onto a new page, keeping the tree shape but issuing fresh ids.</summary>
    private static IReadOnlyList<DeckItem> RemapIds(IReadOnlyList<DeckItem> items, string newPageId)
    {
        var idMap = items.ToDictionary(i => i.Id, _ => Guid.NewGuid().ToString("n"));

        var clones = items.Select(source => new DeckItem
        {
            Id = idMap[source.Id],
            PageId = newPageId,
            ParentId = source.ParentId is not null && idMap.TryGetValue(source.ParentId, out var mapped) ? mapped : null,
            Type = source.Type,
            SortKey = source.SortKey,
            Col = source.Col,
            Row = source.Row,
            Width = source.Width,
            Height = source.Height,
            Label = source.Label,
            IconPath = source.IconPath,
            IconGlyph = source.IconGlyph,
            ColorHex = source.ColorHex,
            Action = source.Action
        }).ToList();

        return DeckTree.ParentsFirst(clones);
    }

    private static object ItemParameters(DeckItem item) => new
    {
        item.Id,
        item.PageId,
        item.ParentId,
        Type = (int)item.Type,
        item.SortKey,
        GridCol = item.Col,
        GridRow = item.Row,
        GridW = item.Width,
        GridH = item.Height,
        item.Label,
        item.IconPath,
        item.IconGlyph,
        Color = item.ColorHex,
        Data = item.Action is null ? null : JsonSerializer.Serialize(item.Action, DeckJson.Compact)
    };

    // These are plain classes with settable properties rather than positional records on purpose:
    // SQLite hands integers back as Int64, and Dapper's constructor matching will not narrow them
    // to Int32, so a positional record fails to materialise at runtime. Property setters convert.
    private sealed class ProfileRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;

        public Profile ToProfile() => new()
        {
            Id = Id,
            Name = Name,
            SortKey = SortKey,
            CreatedAt = DateTimeOffset.TryParse(CreatedAt, out var parsed) ? parsed : DateTimeOffset.UtcNow
        };
    }

    private sealed class PageRow
    {
        public string Id { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
        public int Columns { get; set; }
        public int Rows { get; set; }

        public Page ToPage() => new()
        {
            Id = Id,
            ProfileId = ProfileId,
            Name = Name,
            SortKey = SortKey,
            Columns = Columns,
            Rows = Rows
        };
    }

    private sealed class ItemRow
    {
        public string Id { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public string? ParentId { get; set; }
        public int Type { get; set; }
        public string SortKey { get; set; } = string.Empty;
        public int GridCol { get; set; }
        public int GridRow { get; set; }
        public int GridW { get; set; }
        public int GridH { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? IconPath { get; set; }
        public string? IconGlyph { get; set; }
        public string Color { get; set; } = DeckItem.DefaultColor;
        public string? Data { get; set; }

        public DeckItem ToItem() => new()
        {
            Id = Id,
            PageId = PageId,
            ParentId = ParentId,
            Type = (ItemType)Type,
            SortKey = SortKey,
            Col = GridCol,
            Row = GridRow,
            Width = GridW,
            Height = GridH,
            Label = Label,
            IconPath = IconPath,
            IconGlyph = IconGlyph,
            ColorHex = Color,
            Action = DeserialiseAction(Data)
        };

        /// <summary>
        /// A payload written by a newer build can carry an action kind this build has never heard
        /// of. Dropping just that action keeps the rest of the page usable instead of failing the
        /// whole load; the tile shows up unconfigured rather than disappearing.
        /// </summary>
        private static ActionPayload? DeserialiseAction(string? data)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;

            try
            {
                return JsonSerializer.Deserialize<ActionPayload>(data, DeckJson.Compact);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }
}
