namespace Sunduk.Core;

/// <summary>Local persistence contract; this is not a public network protocol.</summary>
public interface IItemRepository
{
    CatalogItem Create(Guid workspaceId, ItemDraft draft);
    CatalogItem Update(Guid workspaceId, Guid itemId, long expectedRevision, ItemDraft draft);
    void Delete(Guid workspaceId, Guid itemId, long expectedRevision);
    IReadOnlyList<CatalogItem> List(Guid workspaceId, string? search = null, int limit = 200);
    int CountByKind(Guid workspaceId, string kind);
}

/// <summary>The item was changed, deleted, absent, or outside the selected workspace.</summary>
public sealed class ItemConflictException() : InvalidOperationException(
    "Запись уже изменена или недоступна. Обновите список перед сохранением.");
