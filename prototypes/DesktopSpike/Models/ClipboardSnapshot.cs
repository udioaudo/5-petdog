using System.Windows.Media.Imaging;

namespace DesktopSpike.Models;

public sealed record ClipboardSnapshot(
    ClipboardContentKind Kind,
    DateTimeOffset CapturedAt,
    string Summary,
    string Fingerprint,
    string? TextPayload = null,
    BitmapSource? ImagePayload = null);
