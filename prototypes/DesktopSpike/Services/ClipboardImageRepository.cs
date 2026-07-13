using DesktopSpike.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;

namespace DesktopSpike.Services;

public sealed record ClipboardImageDeleteResult(bool FileCleanupCompleted);

public sealed class ClipboardImageRepository
{
    private readonly string _databasePath;
    private readonly ClipboardImageFileStore _fileStore;

    public ClipboardImageRepository(string databasePath, string imageDirectory)
    {
        _databasePath = Path.GetFullPath(databasePath);
        _fileStore = new ClipboardImageFileStore(imageDirectory);
        _fileStore.ReconcilePendingDeletions(LoadReferencedFileNames());
    }

    public IReadOnlyList<ClipboardImageRecord> LoadAll()
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, fingerprint, original_file_name, thumbnail_file_name,
                   pixel_width, pixel_height, created_at_utc, last_copied_at_utc, is_pinned
            FROM clipboard_images
            ORDER BY is_pinned DESC, last_copied_at_utc DESC;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<ClipboardImageRecord>();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }
        return records;
    }

    public ClipboardImageRecord Capture(BitmapSource image, string fingerprint, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        using (var connection = DatabaseInitializer.OpenConnection(_databasePath))
        using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT id FROM clipboard_images WHERE fingerprint = $fingerprint;";
            lookup.Parameters.AddWithValue("$fingerprint", fingerprint);
            var existingId = lookup.ExecuteScalar() as string;
            if (Guid.TryParse(existingId, out var id))
            {
                return UpdateLastCopiedAt(id, capturedAt);
            }
        }

        var recordId = Guid.NewGuid();
        var files = _fileStore.Save(recordId, image);
        try
        {
            using var connection = DatabaseInitializer.OpenConnection(_databasePath);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO clipboard_images
                    (id, fingerprint, original_file_name, thumbnail_file_name, pixel_width, pixel_height,
                     created_at_utc, last_copied_at_utc, is_pinned)
                VALUES
                    ($id, $fingerprint, $original, $thumbnail, $width, $height, $created, $lastCopied, 0);
                """;
            command.Parameters.AddWithValue("$id", recordId.ToString("D"));
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.Parameters.AddWithValue("$original", files.OriginalFileName);
            command.Parameters.AddWithValue("$thumbnail", files.ThumbnailFileName);
            command.Parameters.AddWithValue("$width", image.PixelWidth);
            command.Parameters.AddWithValue("$height", image.PixelHeight);
            command.Parameters.AddWithValue("$created", Format(capturedAt));
            command.Parameters.AddWithValue("$lastCopied", Format(capturedAt));
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch
        {
            _fileStore.Delete(files);
            throw;
        }

        return Load(recordId);
    }

    public ClipboardImageRecord UpdateLastCopiedAt(Guid id, DateTimeOffset copiedAt)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_images SET last_copied_at_utc = $copiedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$copiedAt", Format(copiedAt));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("未找到需要更新的图片记录。");
        }
        return Load(id);
    }

    public ClipboardImageRecord SetPinned(Guid id, bool isPinned)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_images SET is_pinned = $isPinned WHERE id = $id;";
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("未找到需要更新的图片记录。");
        }
        return Load(id);
    }

    public ClipboardImageDeleteResult Delete(Guid id)
    {
        var record = Load(id);
        var files = new SavedImageFiles(record.OriginalFileName, record.ThumbnailFileName);
        using var pendingFiles = _fileStore.StageDelete(files);

        using (var connection = DatabaseInitializer.OpenConnection(_databasePath))
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM clipboard_images WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            if (command.ExecuteNonQuery() != 1)
            {
                throw new InvalidOperationException("未找到需要删除的图片记录。");
            }
            transaction.Commit();
        }

        return new ClipboardImageDeleteResult(pendingFiles.Complete());
    }

    public BitmapSource? TryLoadOriginal(ClipboardImageRecord record) => _fileStore.TryLoadOriginal(record.OriginalFileName);

    public BitmapSource? TryLoadOriginal(string originalFileName) => _fileStore.TryLoadOriginal(originalFileName);

    private IReadOnlySet<string> LoadReferencedFileNames()
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT original_file_name, thumbnail_file_name FROM clipboard_images;";
        using var reader = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
            names.Add(reader.GetString(1));
        }
        return names;
    }

    private ClipboardImageRecord Load(Guid id)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, fingerprint, original_file_name, thumbnail_file_name,
                   pixel_width, pixel_height, created_at_utc, last_copied_at_utc, is_pinned
            FROM clipboard_images WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("未找到图片记录。");
        }
        return ReadRecord(reader);
    }

    private ClipboardImageRecord ReadRecord(SqliteDataReader reader)
    {
        var originalFileName = reader.GetString(2);
        var thumbnailFileName = reader.GetString(3);
        var thumbnail = _fileStore.TryLoadThumbnail(thumbnailFileName);
        var originalExists = _fileStore.OriginalExists(originalFileName);
        return new ClipboardImageRecord(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            originalFileName,
            thumbnailFileName,
            reader.GetInt32(4),
            reader.GetInt32(5),
            Parse(reader.GetString(6)),
            Parse(reader.GetString(7)),
            reader.GetInt64(8) == 1,
            thumbnail,
            thumbnail is null || !originalExists);
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
