using System.IO;
using Microsoft.Data.Sqlite;

namespace DesktopSpike.Services;

public sealed class DatabaseInitializer
{
    static DatabaseInitializer()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public const int CurrentSchemaVersion = 4;

    public void Initialize(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath))
            ?? throw new InvalidOperationException("无法确定数据库目录。");
        Directory.CreateDirectory(directory);

        using var connection = OpenConnection(databasePath);
        var version = ReadSchemaVersion(connection);
        if (version > CurrentSchemaVersion)
        {
            throw new InvalidDataException("数据库版本高于当前程序支持的版本。");
        }

        if (version == CurrentSchemaVersion)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        try
        {
            if (version == 0)
            {
                using var createCommand = connection.CreateCommand();
                createCommand.Transaction = transaction;
                createCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS todos (
                        id TEXT PRIMARY KEY NOT NULL,
                        text TEXT NOT NULL,
                        is_completed INTEGER NOT NULL CHECK (is_completed IN (0, 1)),
                        created_at_utc TEXT NOT NULL,
                        updated_at_utc TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_todos_display_order
                        ON todos (is_completed ASC, created_at_utc DESC);
                    PRAGMA user_version = 1;
                    """;
                createCommand.ExecuteNonQuery();
                version = 1;
            }

            if (version == 1)
            {
                using var migrationCommand = connection.CreateCommand();
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = """
                    ALTER TABLE todos ADD COLUMN completed_at_utc TEXT NULL;
                    UPDATE todos
                    SET completed_at_utc = updated_at_utc
                    WHERE is_completed = 1 AND completed_at_utc IS NULL;
                    CREATE INDEX IF NOT EXISTS idx_todos_completed_order
                        ON todos (is_completed ASC, completed_at_utc DESC, created_at_utc DESC);
                    PRAGMA user_version = 2;
                    """;
                migrationCommand.ExecuteNonQuery();
                version = 2;
            }

            if (version == 2)
            {
                using var clipboardCommand = connection.CreateCommand();
                clipboardCommand.Transaction = transaction;
                clipboardCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS clipboard_texts (
                        id TEXT PRIMARY KEY NOT NULL,
                        text TEXT NOT NULL,
                        fingerprint TEXT NOT NULL UNIQUE,
                        created_at_utc TEXT NOT NULL,
                        last_copied_at_utc TEXT NOT NULL,
                        is_pinned INTEGER NOT NULL DEFAULT 0 CHECK (is_pinned IN (0, 1))
                    );
                    CREATE INDEX IF NOT EXISTS idx_clipboard_texts_display_order
                        ON clipboard_texts (is_pinned DESC, last_copied_at_utc DESC);
                    PRAGMA user_version = 3;
                    """;
                clipboardCommand.ExecuteNonQuery();
                version = 3;
            }

            if (version == 3)
            {
                using var imageCommand = connection.CreateCommand();
                imageCommand.Transaction = transaction;
                imageCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS clipboard_images (
                        id TEXT PRIMARY KEY NOT NULL,
                        fingerprint TEXT NOT NULL UNIQUE,
                        original_file_name TEXT NOT NULL,
                        thumbnail_file_name TEXT NOT NULL,
                        pixel_width INTEGER NOT NULL CHECK (pixel_width > 0),
                        pixel_height INTEGER NOT NULL CHECK (pixel_height > 0),
                        created_at_utc TEXT NOT NULL,
                        last_copied_at_utc TEXT NOT NULL,
                        is_pinned INTEGER NOT NULL DEFAULT 0 CHECK (is_pinned IN (0, 1))
                    );
                    CREATE INDEX IF NOT EXISTS idx_clipboard_images_display_order
                        ON clipboard_images (is_pinned DESC, last_copied_at_utc DESC);
                    PRAGMA user_version = 4;
                    """;
                imageCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public static SqliteConnection OpenConnection(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        return connection;
    }

    public static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
