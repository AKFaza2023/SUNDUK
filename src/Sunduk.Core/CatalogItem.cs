namespace Sunduk.Core;

/// <summary>Storage-independent item identity. LocalId is never a synchronization key.</summary>
public sealed record CatalogItem(
    long LocalId,
    Guid Id,
    Guid WorkspaceId,
    string Kind,
    string Title,
    string Category,
    string Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    DateTimeOffset? DeletedAt);

public sealed record ItemDraft
{
    public string Kind { get; }
    public string Title { get; }
    public string Category { get; }
    public string Note { get; }

    public ItemDraft(string kind, string title, string category = "", string note = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(note);
        Kind = kind.Trim();
        Title = title.Trim();
        Category = category;
        Note = note;
    }
}
