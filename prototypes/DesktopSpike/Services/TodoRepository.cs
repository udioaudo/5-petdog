using System.Globalization;
using System.IO;
using DesktopSpike.Models;
using Microsoft.Data.Sqlite;

namespace DesktopSpike.Services;

public sealed class TodoRepository
{
    private readonly string _databasePath;

    public TodoRepository(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public IReadOnlyList<TodoRecord> LoadAll()
    {
        var records = new List<TodoRecord>();
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, text, is_completed, created_at_utc, updated_at_utc, completed_at_utc
            FROM todos
            ORDER BY is_completed ASC,
                     CASE WHEN is_completed = 1 THEN completed_at_utc END DESC,
                     created_at_utc DESC;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var isCompleted = reader.GetInt64(2) == 1;
            var updatedAt = ParseTimestamp(reader.GetString(4));
            records.Add(new TodoRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                isCompleted,
                ParseTimestamp(reader.GetString(3)),
                updatedAt,
                reader.IsDBNull(5) ? (isCompleted ? updatedAt : null) : ParseTimestamp(reader.GetString(5))));
        }
        return records;
    }

    public void Add(TodoRecord record)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO todos (id, text, is_completed, created_at_utc, updated_at_utc, completed_at_utc)
            VALUES ($id, $text, $isCompleted, $createdAt, $updatedAt, $completedAt);
            """;
        AddCommonParameters(command, record);
        command.ExecuteNonQuery();
    }

    public void UpdateText(Guid id, string text, DateTimeOffset updatedAt)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE todos SET text = $text, updated_at_utc = $updatedAt WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));
        EnsureSingleRow(command.ExecuteNonQuery());
    }

    public void UpdateCompletion(Guid id, bool isCompleted, DateTimeOffset updatedAt, DateTimeOffset? completedAt)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE todos
            SET is_completed = $isCompleted,
                updated_at_utc = $updatedAt,
                completed_at_utc = $completedAt
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$isCompleted", isCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(updatedAt));
        command.Parameters.AddWithValue("$completedAt", completedAt is null ? DBNull.Value : FormatTimestamp(completedAt.Value));
        EnsureSingleRow(command.ExecuteNonQuery());
    }

    public void Delete(Guid id)
    {
        using var connection = DatabaseInitializer.OpenConnection(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM todos WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        EnsureSingleRow(command.ExecuteNonQuery());
    }

    private static void AddCommonParameters(SqliteCommand command, TodoRecord record)
    {
        command.Parameters.AddWithValue("$id", record.Id.ToString("D"));
        command.Parameters.AddWithValue("$text", record.Text);
        command.Parameters.AddWithValue("$isCompleted", record.IsCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", FormatTimestamp(record.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", FormatTimestamp(record.UpdatedAt));
        command.Parameters.AddWithValue("$completedAt", record.CompletedAt is null ? DBNull.Value : FormatTimestamp(record.CompletedAt.Value));
    }

    private static void EnsureSingleRow(int affectedRows)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException("待办记录不存在或写入结果异常。");
        }
    }

    private static string FormatTimestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
