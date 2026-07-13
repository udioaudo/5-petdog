using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopSpike.Services;

public sealed record SavedImageFiles(string OriginalFileName, string ThumbnailFileName);

public sealed class ClipboardImageFileStore
{
    public const long MaximumPixelCount = 60_000_000;
    public const int ThumbnailMaximumEdge = 320;
    public const int MaximumEncodedBytes = 80 * 1024 * 1024;
    private const string DeleteMarker = ".delete-";

    private readonly string _imageDirectory;

    public ClipboardImageFileStore(string imageDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageDirectory);
        _imageDirectory = Path.GetFullPath(imageDirectory);
    }

    public SavedImageFiles Save(Guid id, BitmapSource image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateImage(image);
        Directory.CreateDirectory(_imageDirectory);

        var originalFileName = $"{id:N}.png";
        var thumbnailFileName = $"{id:N}.thumb.png";
        var originalPath = ResolveManagedPath(originalFileName);
        var thumbnailPath = ResolveManagedPath(thumbnailFileName);
        var originalTempPath = originalPath + ".tmp";
        var thumbnailTempPath = thumbnailPath + ".tmp";

        try
        {
            var originalBytes = EncodePng(image);
            if (originalBytes.Length > MaximumEncodedBytes)
            {
                throw new InvalidDataException("图片编码后超过安全大小限制。");
            }

            var thumbnail = CreateThumbnail(image);
            var thumbnailBytes = EncodePng(thumbnail);
            File.WriteAllBytes(originalTempPath, originalBytes);
            File.WriteAllBytes(thumbnailTempPath, thumbnailBytes);
            File.Move(originalTempPath, originalPath, true);
            File.Move(thumbnailTempPath, thumbnailPath, true);
            return new SavedImageFiles(originalFileName, thumbnailFileName);
        }
        catch
        {
            TryDelete(originalTempPath);
            TryDelete(thumbnailTempPath);
            TryDelete(originalPath);
            TryDelete(thumbnailPath);
            throw;
        }
    }

    public BitmapSource? TryLoadThumbnail(string fileName) => TryLoad(fileName);

    public BitmapSource? TryLoadOriginal(string fileName) => TryLoad(fileName);

    public void Delete(SavedImageFiles files)
    {
        TryDelete(ResolveManagedPath(files.OriginalFileName));
        TryDelete(ResolveManagedPath(files.ThumbnailFileName));
    }

    public PendingImageFileDeletion StageDelete(SavedImageFiles files)
    {
        ArgumentNullException.ThrowIfNull(files);
        Directory.CreateDirectory(_imageDirectory);
        var token = Guid.NewGuid().ToString("N");
        var entries = new List<PendingImageFileDeletionEntry>();

        try
        {
            StageIfPresent(files.OriginalFileName, token, entries);
            StageIfPresent(files.ThumbnailFileName, token, entries);
            return new PendingImageFileDeletion(entries);
        }
        catch
        {
            PendingImageFileDeletion.RollbackEntries(entries);
            throw;
        }
    }

    public void ReconcilePendingDeletions(IReadOnlySet<string> referencedFileNames)
    {
        ArgumentNullException.ThrowIfNull(referencedFileNames);
        if (!Directory.Exists(_imageDirectory))
        {
            return;
        }

        foreach (var stagedPath in Directory.EnumerateFiles(_imageDirectory, $"*{DeleteMarker}*.tmp", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var stagedName = Path.GetFileName(stagedPath);
                var markerIndex = stagedName.LastIndexOf(DeleteMarker, StringComparison.Ordinal);
                if (markerIndex <= 0)
                {
                    continue;
                }

                var originalFileName = stagedName[..markerIndex];
                var originalPath = ResolveManagedPath(originalFileName);
                if (referencedFileNames.Contains(originalFileName))
                {
                    if (File.Exists(originalPath))
                    {
                        File.Delete(stagedPath);
                    }
                    else
                    {
                        File.Move(stagedPath, originalPath);
                    }
                }
                else
                {
                    File.Delete(stagedPath);
                }
            }
            catch
            {
                // 单个残留文件恢复或清理失败时保留现场，不能影响应用启动。
            }
        }
    }

    public bool OriginalExists(string fileName) => File.Exists(ResolveManagedPath(fileName));

    private void StageIfPresent(string fileName, string token, ICollection<PendingImageFileDeletionEntry> entries)
    {
        var originalPath = ResolveManagedPath(fileName);
        if (!File.Exists(originalPath))
        {
            return;
        }

        var stagedPath = ResolveManagedPath($"{fileName}{DeleteMarker}{token}.tmp");
        File.Move(originalPath, stagedPath);
        entries.Add(new PendingImageFileDeletionEntry(originalPath, stagedPath));
    }

    private BitmapSource? TryLoad(string fileName)
    {
        try
        {
            var path = ResolveManagedPath(fileName);
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    private string ResolveManagedPath(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (!string.Equals(safeName, fileName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(safeName))
        {
            throw new InvalidDataException("图片文件名无效。");
        }

        var path = Path.GetFullPath(Path.Combine(_imageDirectory, safeName));
        var prefix = _imageDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("图片路径超出应用数据目录。");
        }
        return path;
    }

    private static void ValidateImage(BitmapSource image)
    {
        if (image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            throw new InvalidDataException("图片尺寸无效。");
        }

        var pixels = checked((long)image.PixelWidth * image.PixelHeight);
        if (pixels > MaximumPixelCount)
        {
            throw new InvalidDataException("图片像素过大，已安全跳过。");
        }
    }

    private static BitmapSource CreateThumbnail(BitmapSource image)
    {
        var scale = Math.Min(1d, (double)ThumbnailMaximumEdge / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale >= 1d)
        {
            var copy = new WriteableBitmap(image);
            copy.Freeze();
            return copy;
        }

        var transformed = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清理失败不能覆盖原始异常；后续启动会继续处理未登记的临时文件。
        }
    }
}

public sealed record PendingImageFileDeletionEntry(string OriginalPath, string StagedPath);

public sealed class PendingImageFileDeletion : IDisposable
{
    private readonly IReadOnlyList<PendingImageFileDeletionEntry> _entries;
    private bool _finished;

    internal PendingImageFileDeletion(IReadOnlyList<PendingImageFileDeletionEntry> entries)
    {
        _entries = entries;
    }

    public bool Complete()
    {
        if (_finished)
        {
            throw new InvalidOperationException("图片删除事务已经结束。");
        }

        _finished = true;
        var allDeleted = true;
        foreach (var entry in _entries)
        {
            try
            {
                if (File.Exists(entry.StagedPath))
                {
                    File.Delete(entry.StagedPath);
                }
            }
            catch
            {
                allDeleted = false;
            }
        }
        return allDeleted;
    }

    public void Rollback()
    {
        if (_finished)
        {
            return;
        }

        RollbackEntries(_entries);
        _finished = true;
    }

    internal static void RollbackEntries(IEnumerable<PendingImageFileDeletionEntry> entries)
    {
        List<Exception>? failures = null;
        foreach (var entry in entries.Reverse())
        {
            try
            {
                if (File.Exists(entry.StagedPath) && !File.Exists(entry.OriginalPath))
                {
                    File.Move(entry.StagedPath, entry.OriginalPath);
                }
            }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("图片删除回滚失败。", failures);
        }
    }

    public void Dispose() => Rollback();
}
