namespace DesktopSpike.Models;

public sealed record ClipboardTextEditResult(
    ClipboardTextRecord Record,
    Guid? RemovedRecordId,
    bool WasMerged);
