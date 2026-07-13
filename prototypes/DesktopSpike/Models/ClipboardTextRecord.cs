namespace DesktopSpike.Models;

public sealed record ClipboardTextRecord(
    Guid Id,
    string Text,
    string Fingerprint,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastCopiedAt,
    bool IsPinned);
