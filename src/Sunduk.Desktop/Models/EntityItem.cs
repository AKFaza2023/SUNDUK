namespace Sunduk.Desktop.Models;

public sealed class EntityItem
{
    public long Id { get; init; }
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
