using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Sunduk.Core;
using Sunduk.Infrastructure;

if (args.Length == 2 && args[0] == "--desktop-smoke")
{
    try { DesktopSmoke(System.IO.Path.GetFullPath(args[1])); return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}

var tests = new (string Name, Action Run)[]
{
    ("New database stays empty and keeps workspace on reopen", FreshDatabase),
    ("Legacy migration preserves IDs, text, timestamps and sequence", LegacyMigration),
    ("Pre-migration backup includes committed WAL rows", WalBackup),
    ("Invalid legacy timestamp rolls back the entire migration", MigrationRollback),
    ("Unknown database and future schema are refused", UnknownSchemas),
    ("Stable identity, revision and UTC survive reopen", Reopen),
    ("Stale update cannot overwrite a newer revision", UpdateConflict),
    ("Concurrent writers cannot both win the same revision", ConcurrentUpdates),
    ("Workspace reads, counts and writes stay isolated", WorkspaceIsolation),
    ("Deletion leaves a tombstone and rejects stale writes", Tombstone),
    ("Cyrillic search is case-insensitive and wildcard characters are literal", Search),
    ("Identity is immutable and incomplete legacy inserts are rejected", IdentityGuards),
    ("Drafts and unsafe query limits are rejected", InputValidation),
    ("Migration retains custom index and trigger", CustomSchemaObjects),
    ("Concurrent initialization keeps one workspace and stable IDs", ConcurrentInitialization),
    ("Existing trigger cannot silently alter migrated content", TriggerRollback),
    ("A partially extended unknown schema is not adopted", PartialSchema)
};
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} checks passed");
return failed == 0 ? 0 : 1;

static void FreshDatabase()
{
    using var f = new Fixture();
    var first = f.Database.Initialize();
    Assert(first.IsNewDatabase && first.BackupPath is null && first.LocalWorkspaceId != Guid.Empty);
    Assert(f.Repository.List(first.LocalWorkspaceId).Count == 0);
    var second = f.Database.Initialize();
    Assert(!second.IsNewDatabase && second.BackupPath is null && first.LocalWorkspaceId == second.LocalWorkspaceId);
    Assert(!Directory.Exists(Path.Combine(f.Directory, "Backups")));
}

static void LegacyMigration()
{
    using var f = new Fixture();
    using (var connection = Legacy(f.Path))
    {
        Sql(connection, """
            INSERT INTO Items(Id,Kind,Title,Category,Note,CreatedAt)
            VALUES(42,'Контакт','  Иван Петров  ','Работа','Примечание; %_''','2026-09-01T13:00:00+03:00');
            """);
    }
    var result = f.Database.Initialize();
    Assert(!result.IsNewDatabase && File.Exists(result.BackupPath));
    var item = f.Repository.List(result.LocalWorkspaceId).Single();
    Assert(item.LocalId == 42 && item.Title == "  Иван Петров  " && item.Note == "Примечание; %_'");
    Assert(item.CreatedAt == DateTimeOffset.Parse("2026-09-01T10:00:00Z") && item.UpdatedAt == item.CreatedAt);
    Assert(item.Revision == 1 && item.Id != Guid.Empty);
    var next = f.Repository.Create(result.LocalWorkspaceId, new("Товар", "Следующий"));
    Assert(next.LocalId == 43 && next.Id != item.Id);
    f.Database.Initialize();
    Assert(f.Repository.List(result.LocalWorkspaceId).Single(x => x.LocalId == 42).Id == item.Id);
    using var backup = Open(result.BackupPath!);
    Assert(Scalar(backup, "SELECT Title FROM Items WHERE Id=42;") == "  Иван Петров  ");
    Assert(Scalar(backup, "PRAGMA user_version;") == "0");
}

static void WalBackup()
{
    using var f = new Fixture();
    using var original = Legacy(f.Path);
    Sql(original, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0;");
    InsertLegacy(original, "Запись только в журнале WAL", "2026-09-01T00:00:00Z");
    Assert(File.Exists(f.Path + "-wal"));
    var result = f.Database.Initialize();
    using var backup = Open(result.BackupPath!);
    Assert(Scalar(backup, "SELECT Title FROM Items;") == "Запись только в журнале WAL");
    Assert(f.Repository.List(result.LocalWorkspaceId).Count == 1);
}

static void MigrationRollback()
{
    using var f = new Fixture();
    using (var original = Legacy(f.Path))
    {
        InsertLegacy(original, "Сохранить меня", "2026-09-01T00:00:00Z");
        InsertLegacy(original, "Некорректная дата", "not-a-date");
    }
    Throws<FormatException>(() => f.Database.Initialize());
    using var connection = Open(f.Path);
    Assert(Scalar(connection, "SELECT COUNT(*) FROM Items;") == "2");
    Assert(Scalar(connection, "SELECT count(*) FROM pragma_table_info('Items');") == "6");
    Assert(Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE name='Workspaces';") == "0");
    Assert(Scalar(connection, "PRAGMA user_version;") == "0");
    Assert(System.IO.Directory.GetFiles(Path.Combine(f.Directory, "Backups")).Length == 1);
}

static void UnknownSchemas()
{
    using var f = new Fixture();
    using (var connection = Open(f.Path)) Sql(connection, "CREATE TABLE Other(Value TEXT); INSERT INTO Other VALUES('keep');");
    Throws<InvalidOperationException>(() => f.Database.Initialize());
    using (var connection = Open(f.Path))
    {
        Assert(Scalar(connection, "SELECT Value FROM Other;") == "keep");
        Sql(connection, "PRAGMA user_version=999;");
    }
    Throws<InvalidOperationException>(() => f.Database.Initialize());
    using var future = Open(f.Path);
    Assert(Scalar(future, "PRAGMA user_version;") == "999");
}

static void Reopen()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    var item = f.Repository.Create(w, new("Товар", "Образец"));
    var database = new SqliteDatabase(f.Path);
    Assert(database.Initialize().LocalWorkspaceId == w);
    var reopened = new SqliteItemRepository(database).List(w).Single();
    Assert(item == reopened && item.CreatedAt.Offset == TimeSpan.Zero);
}

static void UpdateConflict()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    var item = f.Repository.Create(w, new("Товар", "Первый"));
    var updated = f.Repository.Update(w, item.Id, 1, new("Товар", "Правильное изменение"));
    Assert(updated.Revision == 2 && updated.CreatedAt == item.CreatedAt && updated.LocalId == item.LocalId);
    Throws<ItemConflictException>(() => f.Repository.Update(w, item.Id, 1, new("Товар", "Устаревшее изменение")));
    Assert(f.Repository.List(w).Single().Title == "Правильное изменение");
}

static void ConcurrentUpdates()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    var item = f.Repository.Create(w, new("Товар", "Начало"));
    using var gate = new Barrier(2);
    var results = Enumerable.Range(0, 2).Select(i => Task.Run(() =>
    {
        gate.SignalAndWait();
        try { f.Repository.Update(w, item.Id, 1, new("Товар", $"Редактор {i}")); return true; }
        catch (ItemConflictException) { return false; }
    })).ToArray();
    Task.WaitAll(results);
    Assert(results.Count(x => x.Result) == 1 && f.Repository.List(w).Single().Revision == 2);
}

static void WorkspaceIsolation()
{
    using var f = new Fixture();
    var a = f.Database.Initialize().LocalWorkspaceId;
    var b = Guid.NewGuid();
    using (var connection = Open(f.Path))
        Sql(connection, $"INSERT INTO Workspaces VALUES('{b:D}','Другая команда','2026-09-01T00:00:00Z');");
    var itemA = f.Repository.Create(a, new("Товар", "Личное"));
    var itemB = f.Repository.Create(b, new("Товар", "Командное"));
    Assert(f.Repository.List(a).Single().Id == itemA.Id && f.Repository.List(b).Single().Id == itemB.Id);
    Assert(f.Repository.List(a, "Командное").Count == 0 && f.Repository.CountByKind(a, "Товар") == 1);
    Throws<ItemConflictException>(() => f.Repository.Update(a, itemB.Id, 1, new("Товар", "Чужая запись")));
    Throws<ItemConflictException>(() => f.Repository.Delete(b, itemA.Id, 1));
    Throws<SqliteException>(() => f.Repository.Create(Guid.NewGuid(), new("Товар", "Неизвестное пространство")));
}

static void Tombstone()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    var item = f.Repository.Create(w, new("Товар", "Удалить"));
    f.Repository.Delete(w, item.Id, 1);
    Assert(f.Repository.List(w).Count == 0 && f.Repository.CountByKind(w, "Товар") == 0);
    using var connection = Open(f.Path);
    Assert(Scalar(connection, "SELECT Revision FROM Items;") == "2");
    Assert(Scalar(connection, "SELECT DeletedAt IS NOT NULL FROM Items;") == "1");
    Throws<ItemConflictException>(() => f.Repository.Update(w, item.Id, 1, new("Товар", "Вернуть устаревшее")));
    Throws<ItemConflictException>(() => f.Repository.Delete(w, item.Id, 2));
    f.Database.Initialize();
    Assert(f.Repository.List(w).Count == 0);
}

static void Search()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    f.Repository.Create(w, new("Контакт", "ИВАН Петров", "Работа", "Ёлка, 100%_срок; ' OR 1=1 --"));
    f.Repository.Create(w, new("Товар", "Другой"));
    foreach (var query in new[] { "иван", "ПЕТРОВ", "работа", "контакт", "ёлка", "%_", "' OR 1=1 --" })
        Assert(f.Repository.List(w, query).Count == 1, query);
    Assert(f.Repository.List(w, "не найдено").Count == 0);
    Assert(f.Repository.List(w, limit: 1).Single().Title == "Другой");
}

static void IdentityGuards()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    f.Repository.Create(w, new("Товар", "Запись"));
    using var connection = Open(f.Path);
    Throws<SqliteException>(() => Sql(connection, "UPDATE Items SET GlobalId='other';"));
    Throws<SqliteException>(() => Sql(connection, "UPDATE Items SET CreatedAt='2027-01-01';"));
    Throws<SqliteException>(() => InsertLegacy(connection, "Старый клиент", "2026-09-01T00:00:00Z"));
    Assert(Scalar(connection, "SELECT COUNT(*) FROM Items;") == "1");
}

static void InputValidation()
{
    using var f = new Fixture();
    var w = f.Database.Initialize().LocalWorkspaceId;
    Throws<ArgumentException>(() => new ItemDraft("Товар", " "));
    Throws<ArgumentException>(() => new ItemDraft(" ", "Карточка"));
    Throws<ArgumentNullException>(() => new ItemDraft("Товар", "Карточка", note: null!));
    Throws<ArgumentException>(() => f.Repository.List(Guid.Empty));
    Throws<ArgumentOutOfRangeException>(() => f.Repository.List(w, limit: -1));
    Throws<ArgumentOutOfRangeException>(() => f.Repository.List(w, limit: 1001));
}

static void CustomSchemaObjects()
{
    using var f = new Fixture();
    using (var connection = Legacy(f.Path))
    {
        Sql(connection, """
            CREATE INDEX Legacy_Category ON Items(Category);
            CREATE TRIGGER Legacy_Title BEFORE INSERT ON Items WHEN NEW.Title='blocked'
            BEGIN SELECT RAISE(ABORT, 'legacy title rule'); END;
            """);
    }
    var w = f.Database.Initialize().LocalWorkspaceId;
    using var migrated = Open(f.Path);
    Assert(Scalar(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Legacy_Category','Legacy_Title');") == "2");
    Throws<SqliteException>(() => f.Repository.Create(w, new("Товар", "blocked")));
}

static void ConcurrentInitialization()
{
    using var f = new Fixture();
    using (var old = Legacy(f.Path)) InsertLegacy(old, "До миграции", "2026-09-01T00:00:00Z");
    using var gate = new Barrier(2);
    var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
        gate.SignalAndWait();
        return new SqliteDatabase(f.Path).Initialize();
    })).ToArray();
    Task.WaitAll(tasks);
    Assert(tasks[0].Result.LocalWorkspaceId == tasks[1].Result.LocalWorkspaceId);
    var item = f.Repository.List(tasks[0].Result.LocalWorkspaceId).Single();
    Assert(item.Title == "До миграции" && item.Id != Guid.Empty);
    using var c = Open(f.Path);
    Assert(Scalar(c, "SELECT COUNT(*) FROM Workspaces;") == "1");
}

static void TriggerRollback()
{
    using var f = new Fixture();
    using (var c = Legacy(f.Path))
    {
        InsertLegacy(c, "Сохранить исходное", "2026-09-01T00:00:00Z");
        Sql(c, """
            CREATE TRIGGER Legacy_Mutates AFTER UPDATE ON Items
            BEGIN UPDATE Items SET Note='Unexpected mutation' WHERE Id=NEW.Id; END;
            """);
    }
    Throws<InvalidOperationException>(() => f.Database.Initialize());
    using var connection = Open(f.Path);
    Assert(Scalar(connection, "SELECT Note FROM Items;") == "");
    Assert(Scalar(connection, "PRAGMA user_version;") == "0");
    Assert(Scalar(connection, "SELECT count(*) FROM pragma_table_info('Items');") == "6");
}

static void PartialSchema()
{
    using var f = new Fixture();
    using (var c = Legacy(f.Path))
    {
        InsertLegacy(c, "Оставить", "2026-09-01T00:00:00Z");
        Sql(c, "ALTER TABLE Items ADD COLUMN Unexpected TEXT;");
    }
    Throws<InvalidOperationException>(() => f.Database.Initialize());
    using var connection = Open(f.Path);
    Assert(Scalar(connection, "SELECT Title FROM Items;") == "Оставить");
    Assert(Scalar(connection, "SELECT count(*) FROM pragma_table_info('Items');") == "7");
}

static void DesktopSmoke(string executable)
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop smoke requires Windows.");
    var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SUNDUK", "Data");
    if (System.IO.Directory.Exists(directory))
        throw new InvalidOperationException("Desktop smoke refuses to touch an existing SUNDUK data directory.");
    try
    {
        LaunchAndCheck(expectLegacy: false);
        Console.WriteLine("PASS WPF startup with an empty profile");
        System.IO.Directory.Delete(directory, recursive: true);
        System.IO.Directory.CreateDirectory(directory);
        using (var legacy = Legacy(System.IO.Path.Combine(directory, "sunduk.db")))
            InsertLegacy(legacy, "Проверка старой базы", "2026-09-01T00:00:00Z");
        LaunchAndCheck(expectLegacy: true);
        Console.WriteLine("PASS WPF startup with migration of a legacy database");
    }
    finally
    {
        if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
    }

    void LaunchAndCheck(bool expectLegacy)
    {
        using var process = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })
            ?? throw new InvalidOperationException("SUNDUK did not start.");
        try
        {
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(20))
            {
                process.Refresh();
                if (process.HasExited) throw new InvalidOperationException($"SUNDUK exited early: {process.ExitCode}");
                if (process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle == "SUNDUK") break;
                Thread.Sleep(200);
            }
            process.Refresh();
            Assert(process.MainWindowHandle != IntPtr.Zero && process.MainWindowTitle == "SUNDUK", "SUNDUK window was not ready.");
            var database = new SqliteDatabase(System.IO.Path.Combine(directory, "sunduk.db"));
            var state = database.Initialize();
            var items = new SqliteItemRepository(database).List(state.LocalWorkspaceId);
            Assert(expectLegacy ? items.Count == 1 && items[0].Title == "Проверка старой базы" : items.Count == 0);
            if (expectLegacy)
                Assert(System.IO.Directory.GetFiles(System.IO.Path.Combine(directory, "Backups")).Length >= 1);
            Assert(process.CloseMainWindow(), "SUNDUK window did not accept close.");
            Assert(process.WaitForExit(10000), "SUNDUK did not close normally.");
            Assert(process.ExitCode == 0, $"SUNDUK exit code: {process.ExitCode}");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
        }
    }
}

static SqliteConnection Open(string path)
{
    var connection = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path, ForeignKeys = true, Pooling = false
    }.ToString());
    connection.Open();
    return connection;
}

static SqliteConnection Legacy(string path)
{
    var connection = Open(path);
    Sql(connection, """
        CREATE TABLE Items(Id INTEGER PRIMARY KEY AUTOINCREMENT, Kind TEXT NOT NULL, Title TEXT NOT NULL,
          Category TEXT NOT NULL DEFAULT '', Note TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL);
        CREATE INDEX IX_Items_Kind ON Items(Kind);
        CREATE INDEX IX_Items_Title ON Items(Title);
        """);
    return connection;
}

static void InsertLegacy(SqliteConnection connection, string title, string createdAt)
{
    using var command = connection.CreateCommand();
    command.CommandText = "INSERT INTO Items(Kind,Title,Category,Note,CreatedAt) VALUES('Товар',$title,'','',$created);";
    command.Parameters.AddWithValue("$title", title);
    command.Parameters.AddWithValue("$created", createdAt);
    command.ExecuteNonQuery();
}

static void Sql(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
}

static string? Scalar(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar()?.ToString();
}

static void Assert(bool value, string? message = null)
{
    if (!value) throw new Exception(message ?? "Assertion failed");
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}

sealed class Fixture : IDisposable
{
    public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SUNDUK-checks-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Directory, "sunduk.db");
    public SqliteDatabase Database { get; }
    public SqliteItemRepository Repository { get; }
    public Fixture()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Database = new(Path);
        Repository = new(Database);
    }
    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}
