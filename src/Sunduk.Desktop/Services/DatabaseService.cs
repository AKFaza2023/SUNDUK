using System.IO;
using Sunduk.Core;
using Sunduk.Infrastructure;
using Sunduk.Desktop.Models;

namespace Sunduk.Desktop.Services;

// WPF compatibility adapter. Data access and migration live outside the desktop project.
public static class DatabaseService
{
    private static IItemRepository? _repository;
    private static Guid _workspaceId;
    public static string? LastBackupPath { get; private set; }

    public static void Initialize()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SUNDUK", "Data", "sunduk.db");
        var database = new SqliteDatabase(path);
        var result = database.Initialize();
        _workspaceId = result.LocalWorkspaceId;
        LastBackupPath = result.BackupPath;
        _repository = new SqliteItemRepository(database);
    }

    private static IItemRepository Repository => _repository
        ?? throw new InvalidOperationException("Локальная база SUNDUK не подключена.");

    public static void AddItem(string kind, string title, string category, string note) =>
        Repository.Create(_workspaceId, new ItemDraft(kind, title, category, note));

    public static int CountByKind(string kind) => Repository.CountByKind(_workspaceId, kind);

    public static List<EntityItem> GetItems(string? search = null, int limit = 200) =>
        Repository.List(_workspaceId, search, limit).Select(item => new EntityItem
        {
            Id = item.LocalId,
            GlobalId = item.Id,
            WorkspaceId = item.WorkspaceId,
            Revision = item.Revision,
            Kind = item.Kind,
            Title = item.Title,
            Category = item.Category,
            Note = item.Note,
            CreatedAt = item.CreatedAt.LocalDateTime
        }).ToList();
}
