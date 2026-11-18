using System.Globalization;
using Microsoft.Data.Sqlite;
using Sunduk.Core;

namespace Sunduk.Infrastructure;

public sealed class SqliteItemRepository(SqliteDatabase database, TimeProvider? timeProvider = null) : IItemRepository
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private const string Columns = "Id, GlobalId, WorkspaceId, Kind, Title, Category, Note, CreatedAt, UpdatedAt, Revision, DeletedAt";

    public CatalogItem Create(Guid workspaceId, ItemDraft draft)
    {
        ValidateWorkspace(workspaceId);
        ArgumentNullException.ThrowIfNull(draft);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var id = Guid.NewGuid();
        var now = _time.GetUtcNow();
        command.CommandText = $"""
            INSERT INTO Items(GlobalId, WorkspaceId, Kind, Title, Category, Note, CreatedAt, UpdatedAt, Revision)
            VALUES($id, $workspace, $kind, $title, $category, $note, $now, $now, 1)
            RETURNING {Columns};
            """;
        Identity(command, workspaceId, id);
        Draft(command, draft);
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        CatalogItem item;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Не удалось создать запись.");
            item = ReadItem(reader);
            while (reader.Read()) { }
        }
        transaction.Commit();
        return item;
    }

    public CatalogItem Update(Guid workspaceId, Guid itemId, long expectedRevision, ItemDraft draft)
    {
        ValidateMutation(workspaceId, itemId, expectedRevision);
        ArgumentNullException.ThrowIfNull(draft);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE Items SET Kind = $kind, Title = $title, Category = $category, Note = $note,
                UpdatedAt = $now, Revision = Revision + 1
            WHERE GlobalId = $id AND WorkspaceId = $workspace AND Revision = $expected AND DeletedAt IS NULL
            RETURNING {Columns};
            """;
        Identity(command, workspaceId, itemId);
        Draft(command, draft);
        command.Parameters.AddWithValue("$expected", expectedRevision);
        command.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        CatalogItem item;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) throw new ItemConflictException();
            item = ReadItem(reader);
            while (reader.Read()) { }
        }
        transaction.Commit();
        return item;
    }

    public void Delete(Guid workspaceId, Guid itemId, long expectedRevision)
    {
        ValidateMutation(workspaceId, itemId, expectedRevision);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Items SET DeletedAt = $now, UpdatedAt = $now, Revision = Revision + 1
            WHERE GlobalId = $id AND WorkspaceId = $workspace AND Revision = $expected AND DeletedAt IS NULL;
            """;
        Identity(command, workspaceId, itemId);
        command.Parameters.AddWithValue("$expected", expectedRevision);
        command.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        if (command.ExecuteNonQuery() != 1) throw new ItemConflictException();
        transaction.Commit();
    }

    public IReadOnlyList<CatalogItem> List(Guid workspaceId, string? search = null, int limit = 200)
    {
        ValidateWorkspace(workspaceId);
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        // Managed invariant case folding handles Cyrillic, unlike SQLite's default ASCII-only LIKE.
        connection.CreateFunction<string, string>("sunduk_casefold", value => value.ToUpperInvariant());
        command.CommandText = $"""
            SELECT {Columns} FROM Items
            WHERE WorkspaceId = $workspace AND DeletedAt IS NULL
              AND ($search = '' OR instr(sunduk_casefold(Title), $search) > 0
                OR instr(sunduk_casefold(Category), $search) > 0
                OR instr(sunduk_casefold(Note), $search) > 0
                OR instr(sunduk_casefold(Kind), $search) > 0)
            ORDER BY Id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$search", search?.Trim().ToUpperInvariant() ?? "");
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<CatalogItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(ReadItem(reader));
        return result;
    }

    public int CountByKind(Guid workspaceId, string kind)
    {
        ValidateWorkspace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM Items WHERE WorkspaceId = $workspace AND Kind = $kind AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$kind", kind);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static CatalogItem ReadItem(SqliteDataReader reader) => new(
        reader.GetInt64(0), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)),
        reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        SqliteDatabase.ParseTimestamp(reader.GetString(7)), SqliteDatabase.ParseTimestamp(reader.GetString(8)),
        reader.GetInt64(9), reader.IsDBNull(10) ? null : SqliteDatabase.ParseTimestamp(reader.GetString(10)));

    private static void Draft(SqliteCommand command, ItemDraft draft)
    {
        command.Parameters.AddWithValue("$kind", draft.Kind);
        command.Parameters.AddWithValue("$title", draft.Title);
        command.Parameters.AddWithValue("$category", draft.Category);
        command.Parameters.AddWithValue("$note", draft.Note);
    }

    private static void Identity(SqliteCommand command, Guid workspaceId, Guid id)
    {
        command.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
    }

    private static void ValidateWorkspace(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Рабочее пространство не задано.", nameof(workspaceId));
    }

    private static void ValidateMutation(Guid workspaceId, Guid itemId, long revision)
    {
        ValidateWorkspace(workspaceId);
        if (itemId == Guid.Empty) throw new ArgumentException("Запись не задана.", nameof(itemId));
        if (revision < 1 || revision == long.MaxValue) throw new ArgumentOutOfRangeException(nameof(revision));
    }
}
