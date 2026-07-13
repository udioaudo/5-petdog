using System.IO;
using DesktopSpike.Models;
using Microsoft.Data.Sqlite;

namespace DesktopSpike.Services;

public sealed class ClipboardTextRepository
{
    private readonly string _databasePath;

    public ClipboardTextRepository(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public IReadOnlyList<ClipboardTextRecord> LoadAll()
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, fingerprint, created_at_utc, last_copied_at_utc, is_pinned
            FROM clipboard_texts
            ORDER BY is_pinned DESC, last_copied_at_utc DESC;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<ClipboardTextRecord>();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }
        return records;
    }

    public ClipboardTextRecord Capture(string text, string fingerprint, DateTimeOffset capturedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var transaction = connection.BeginTransaction();
        var existing = FindByFingerprint(connection, transaction, fingerprint);
        ClipboardTextRecord record;
        if (existing is null)
        {
            record = new ClipboardTextRecord(Guid.NewGuid(), text, fingerprint, capturedAt, capturedAt, false);
            Insert(connection, transaction, record);
        }
        else
        {
            var lastCopiedAt = capturedAt > existing.LastCopiedAt ? capturedAt : existing.LastCopiedAt;
            record = existing with { LastCopiedAt = lastCopiedAt };
            UpdateLastCopiedAt(connection, transaction, record.Id, lastCopiedAt);
        }
        transaction.Commit();
        return record;
    }

    public ClipboardTextRecord Touch(Guid id, DateTimeOffset copiedAt)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var transaction = connection.BeginTransaction();
        var existing = FindById(connection, transaction, id)
            ?? throw new InvalidOperationException("文字剪贴板记录不存在。");
        var lastCopiedAt = copiedAt > existing.LastCopiedAt ? copiedAt : existing.LastCopiedAt;
        UpdateLastCopiedAt(connection, transaction, id, lastCopiedAt);
        transaction.Commit();
        return existing with { LastCopiedAt = lastCopiedAt };
    }

    public ClipboardTextRecord SetPinned(Guid id, bool isPinned)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var transaction = connection.BeginTransaction();
        var existing = FindById(connection, transaction, id)
            ?? throw new InvalidOperationException("文字剪贴板记录不存在。");
        UpdatePinned(connection, transaction, id, isPinned);
        transaction.Commit();
        return existing with { IsPinned = isPinned };
    }

    public void Delete(Guid id)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_texts WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("文字剪贴板记录删除失败。");
        }
    }

    public ClipboardTextEditResult Edit(Guid id, string text, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var transaction = connection.BeginTransaction();
        var source = FindById(connection, transaction, id)
            ?? throw new InvalidOperationException("文字剪贴板记录不存在。");
        var duplicate = FindByFingerprint(connection, transaction, fingerprint);
        if (duplicate is not null && duplicate.Id != source.Id)
        {
            var lastCopiedAt = source.LastCopiedAt > duplicate.LastCopiedAt ? source.LastCopiedAt : duplicate.LastCopiedAt;
            var isPinned = source.IsPinned || duplicate.IsPinned;
            UpdateLastCopiedAt(connection, transaction, duplicate.Id, lastCopiedAt);
            if (duplicate.IsPinned != isPinned)
            {
                UpdatePinned(connection, transaction, duplicate.Id, isPinned);
            }
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM clipboard_texts WHERE id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", source.Id.ToString("D"));
            deleteCommand.ExecuteNonQuery();
            transaction.Commit();
            return new ClipboardTextEditResult(duplicate with { LastCopiedAt = lastCopiedAt, IsPinned = isPinned }, source.Id, true);
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE clipboard_texts
            SET text = $text, fingerprint = $fingerprint
            WHERE id = $id;
            """;
        updateCommand.Parameters.AddWithValue("$text", text);
        updateCommand.Parameters.AddWithValue("$fingerprint", fingerprint);
        updateCommand.Parameters.AddWithValue("$id", id.ToString("D"));
        if (updateCommand.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("文字剪贴板记录更新失败。");
        }
        transaction.Commit();
        return new ClipboardTextEditResult(source with { Text = text, Fingerprint = fingerprint }, null, false);
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, ClipboardTextRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO clipboard_texts
                (id, text, fingerprint, created_at_utc, last_copied_at_utc, is_pinned)
            VALUES
                ($id, $text, $fingerprint, $created, $lastCopied, $isPinned);
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$text", record.Text);
        command.Parameters.AddWithValue("$fingerprint", record.Fingerprint);
        command.Parameters.AddWithValue("$created", record.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$lastCopied", record.LastCopiedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$isPinned", record.IsPinned ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private static void UpdateLastCopiedAt(SqliteConnection connection, SqliteTransaction transaction, Guid id, DateTimeOffset value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE clipboard_texts SET last_copied_at_utc = $value WHERE id = $id;";
        command.Parameters.AddWithValue("$value", value.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("文字剪贴板记录更新时间失败。");
        }
    }

    private static void UpdatePinned(SqliteConnection connection, SqliteTransaction transaction, Guid id, bool isPinned)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE clipboard_texts SET is_pinned = $isPinned WHERE id = $id;";
        command.Parameters.AddWithValue("$isPinned", isPinned ? 1 : 0);
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException("文字剪贴板置顶状态更新失败。");
        }
    }

    private static ClipboardTextRecord? FindByFingerprint(SqliteConnection connection, SqliteTransaction transaction, string fingerprint)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, text, fingerprint, created_at_utc, last_copied_at_utc, is_pinned
            FROM clipboard_texts WHERE fingerprint = $fingerprint LIMIT 1;
            """;
        command.Parameters.AddWithValue("$fingerprint", fingerprint);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private static ClipboardTextRecord? FindById(SqliteConnection connection, SqliteTransaction transaction, Guid id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, text, fingerprint, created_at_utc, last_copied_at_utc, is_pinned
            FROM clipboard_texts WHERE id = $id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadRecord(reader) : null;
    }

    private static ClipboardTextRecord ReadRecord(SqliteDataReader reader) => new(
        Guid.Parse(reader.GetString(0)),
        reader.GetString(1),
        reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3)),
        DateTimeOffset.Parse(reader.GetString(4)),
        reader.GetInt32(5) == 1);
}

