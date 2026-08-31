using Microsoft.Data.Sqlite;
using Sunduk.Desktop.Models;

namespace Sunduk.Desktop.Services;

public static class DatabaseService
{
    private static readonly string DataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SUNDUK", "Data");

    private static readonly string DatabasePath = Path.Combine(DataDirectory, "sunduk.db");

    private static string ConnectionString => $"Data Source={DatabasePath}";

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDirectory);

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Items (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Kind TEXT NOT NULL,
                Title TEXT NOT NULL,
                Category TEXT NOT NULL DEFAULT '',
                Note TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Items_Kind ON Items(Kind);
            CREATE INDEX IF NOT EXISTS IX_Items_Title ON Items(Title);
            """;
        command.ExecuteNonQuery();

        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM Items;";
        var count = Convert.ToInt32(countCommand.ExecuteScalar());

        if (count == 0)
        {
            AddItem("Товар", "Пример товара", "Каталог", "Карточка для проверки каталога.");
            AddItem("Контакт", "Иван Петров", "Контакты", "Пример контакта.");
            AddItem("Закладка", "ставдок.рф", "Ссылки", "Сайт разработчика.");
            AddItem("Заметка", "Добро пожаловать в SUNDUK", "Заметки", "Локальные данные сохраняются автоматически.");
        }
    }

    public static void AddItem(string kind, string title, string category, string note)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Items (Kind, Title, Category, Note, CreatedAt)
            VALUES ($kind, $title, $category, $note, $createdAt);
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$note", note);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public static int CountByKind(string kind)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Items WHERE Kind = $kind;";
        command.Parameters.AddWithValue("$kind", kind);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public static List<EntityItem> GetItems(string? search = null, int limit = 200)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        var command = connection.CreateCommand();
        if (string.IsNullOrWhiteSpace(search))
        {
            command.CommandText = """
                SELECT Id, Kind, Title, Category, Note, CreatedAt
                FROM Items
                ORDER BY Id DESC
                LIMIT $limit;
                """;
        }
        else
        {
            command.CommandText = """
                SELECT Id, Kind, Title, Category, Note, CreatedAt
                FROM Items
                WHERE Title LIKE $search OR Category LIKE $search OR Note LIKE $search OR Kind LIKE $search
                ORDER BY Id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$search", $"%{search.Trim()}%");
        }

        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<EntityItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new EntityItem
            {
                Id = reader.GetInt64(0),
                Kind = reader.GetString(1),
                Title = reader.GetString(2),
                Category = reader.GetString(3),
                Note = reader.GetString(4),
                CreatedAt = DateTime.TryParse(reader.GetString(5), out var dt) ? dt.ToLocalTime() : DateTime.Now
            });
        }

        return result;
    }
}
