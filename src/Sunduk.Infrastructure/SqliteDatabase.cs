using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Sunduk.Infrastructure;

public sealed record DatabaseInitialization(Guid LocalWorkspaceId, bool IsNewDatabase, string? BackupPath);

/// <summary>Versioned, additive migration of the original SUNDUK 0.3 database.</summary>
public sealed class SqliteDatabase
{
    public const int SchemaVersion = 1;
    private readonly string _path;
    private readonly string _connectionString;

    public SqliteDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 10,
            Pooling = false
        }.ToString();
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public DatabaseInitialization Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        using var connection = Open();
        int version;
        bool hasItems;
        string? backupPath = null;
        // Keep the version and legacy schema check in one consistent read snapshot.
        using (var snapshot = connection.BeginTransaction(deferred: true))
        {
            version = ReadVersion(connection, snapshot);
            if (version > SchemaVersion)
                throw new InvalidOperationException("База создана более новой версией SUNDUK. Используйте новую программу.");
            if (version == SchemaVersion)
                return new(ReadWorkspace(connection, snapshot), false, null);

            var tables = ReadNames(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';", snapshot);
            hasItems = tables.Contains("Items", StringComparer.Ordinal);
            if (tables.Count > 0 && (!hasItems || tables.Count != 1))
                throw new InvalidOperationException("Неизвестная схема базы. Автоматическое обновление отменено.");
            if (hasItems) ValidateLegacy(connection, snapshot);

            // SQLite's backup API includes committed data in an existing WAL journal.
            // A raw File.Copy of sunduk.db could omit that data.
            if (hasItems)
            {
                var directory = Path.Combine(Path.GetDirectoryName(_path)!, "Backups");
                Directory.CreateDirectory(directory);
                backupPath = Path.Combine(directory,
                    $"sunduk-before-schema-1-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.db");
                using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
                }.ToString());
                backup.Open();
                connection.BackupDatabase(backup);
            }
        }

        using var transaction = connection.BeginTransaction(deferred: false);
        // Another instance may have completed initialization while the backup was made.
        version = ReadVersion(connection, transaction);
        if (version == SchemaVersion)
        {
            var existing = ReadWorkspace(connection, transaction);
            transaction.Commit();
            return new(existing, false, backupPath);
        }
        if (version != 0) throw new InvalidOperationException("Версия базы изменилась во время обновления.");
        if (!hasItems)
            Execute(connection, transaction, """
                CREATE TABLE Items (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Kind TEXT NOT NULL, Title TEXT NOT NULL,
                    Category TEXT NOT NULL DEFAULT '', Note TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL
                );
                """);

        Execute(connection, transaction, """
            CREATE TABLE Workspaces (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE LocalState (
                Key TEXT NOT NULL PRIMARY KEY,
                Value TEXT NOT NULL
            );
            ALTER TABLE Items ADD COLUMN GlobalId TEXT;
            ALTER TABLE Items ADD COLUMN WorkspaceId TEXT REFERENCES Workspaces(Id);
            ALTER TABLE Items ADD COLUMN Revision INTEGER NOT NULL DEFAULT 1 CHECK (Revision > 0);
            ALTER TABLE Items ADD COLUMN UpdatedAt TEXT;
            ALTER TABLE Items ADD COLUMN DeletedAt TEXT;
            """);
        var workspaceId = Guid.NewGuid();
        using (var workspace = connection.CreateCommand())
        {
            workspace.Transaction = transaction;
            workspace.CommandText = """
                INSERT INTO Workspaces(Id, Name, CreatedAt) VALUES($id, 'Личное пространство', $created);
                INSERT INTO LocalState(Key, Value) VALUES('LocalWorkspaceId', $id);
                """;
            workspace.Parameters.AddWithValue("$id", workspaceId.ToString("D"));
            workspace.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            workspace.ExecuteNonQuery();
        }

        var legacyRows = ReadLegacyRows(connection, transaction);
        foreach (var row in legacyRows)
        {
            // Invalid timestamps must not be silently replaced with today's date.
            _ = ParseTimestamp(row.CreatedAt);
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE Items SET GlobalId = $global, WorkspaceId = $workspace, UpdatedAt = $created WHERE Id = $id;
                """;
            update.Parameters.AddWithValue("$global", Guid.NewGuid().ToString("D"));
            update.Parameters.AddWithValue("$workspace", workspaceId.ToString("D"));
            update.Parameters.AddWithValue("$created", row.CreatedAt);
            update.Parameters.AddWithValue("$id", row.Id);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Не удалось перенести идентификатор записи. Обновление отменено.");
        }
        // Existing user triggers are retained. Refuse a migration if one changed original data.
        if (!legacyRows.SequenceEqual(ReadLegacyRows(connection, transaction)))
            throw new InvalidOperationException("При переносе изменились исходные записи. Обновление отменено.");
        Execute(connection, transaction, """
            CREATE UNIQUE INDEX UX_Items_GlobalId ON Items(GlobalId);
            CREATE INDEX IF NOT EXISTS IX_Items_Kind ON Items(Kind);
            CREATE INDEX IF NOT EXISTS IX_Items_Title ON Items(Title);
            CREATE INDEX IX_Items_Workspace_Active ON Items(WorkspaceId, DeletedAt, Id);
            CREATE TRIGGER Items_Identity_Insert BEFORE INSERT ON Items
            WHEN NEW.GlobalId IS NULL OR trim(NEW.GlobalId) = ''
              OR NEW.WorkspaceId IS NULL OR trim(NEW.WorkspaceId) = ''
              OR NEW.UpdatedAt IS NULL OR trim(NEW.UpdatedAt) = ''
            BEGIN SELECT RAISE(ABORT, 'SUNDUK item identity is required'); END;
            CREATE TRIGGER Items_Identity_Update BEFORE UPDATE ON Items
            WHEN NEW.GlobalId IS NOT OLD.GlobalId OR NEW.WorkspaceId IS NOT OLD.WorkspaceId
              OR NEW.CreatedAt IS NOT OLD.CreatedAt
              OR NEW.UpdatedAt IS NULL OR trim(NEW.UpdatedAt) = ''
            BEGIN SELECT RAISE(ABORT, 'SUNDUK item identity is immutable'); END;
            PRAGMA user_version = 1;
            """);
        transaction.Commit();
        return new(workspaceId, !hasItems, backupPath);
    }

    internal static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static int ReadVersion(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static Guid ReadWorkspace(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT w.Id FROM Workspaces w JOIN LocalState s ON s.Value = w.Id WHERE s.Key = 'LocalWorkspaceId';
            """;
        return Guid.TryParse(command.ExecuteScalar() as string, out var id) && id != Guid.Empty
            ? id : throw new InvalidOperationException("В базе отсутствует локальное рабочее пространство.");
    }

    private static List<string> ReadNames(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var result = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(reader.GetString(0));
        return result;
    }

    private static void ValidateLegacy(SqliteConnection connection, SqliteTransaction transaction)
    {
        var names = ReadNames(connection, "SELECT name FROM pragma_table_info('Items');", transaction);
        string[] expected = ["Id", "Kind", "Title", "Category", "Note", "CreatedAt"];
        if (!names.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Схема Items отличается от SUNDUK 0.3. Обновление отменено.");
    }

    private sealed record LegacyRow(long Id, string Kind, string Title, string Category, string Note, string CreatedAt);

    private static List<LegacyRow> ReadLegacyRows(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, Kind, Title, Category, Note, CreatedAt FROM Items ORDER BY Id;";
        var result = new List<LegacyRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        return result;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
