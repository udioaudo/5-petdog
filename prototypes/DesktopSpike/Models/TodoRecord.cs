namespace DesktopSpike.Models;

public sealed record TodoRecord(
    Guid Id,
    string Text,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
