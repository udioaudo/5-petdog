using System.Windows.Media.Imaging;

namespace DesktopSpike.Models;

public sealed record ClipboardImageRecord(
    Guid Id,
    string Fingerprint,
    string OriginalFileName,
    string ThumbnailFileName,
    int PixelWidth,
    int PixelHeight,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastCopiedAt,
    bool IsPinned,
    BitmapSource? Thumbnail,
    bool PreviewLoadFailed = false);
