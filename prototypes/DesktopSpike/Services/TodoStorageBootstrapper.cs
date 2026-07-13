using DesktopSpike.Models;

namespace DesktopSpike.Services;

public sealed record TodoStorageStartupResult(
    TodoRepository? Repository,
    IReadOnlyList<TodoRecord> Records,
    bool IsAvailable);

public sealed class TodoStorageBootstrapper
{
    private readonly DatabaseInitializer _initializer = new();

    public TodoStorageStartupResult Start(string databasePath)
    {
        try
        {
            _initializer.Initialize(databasePath);
            var repository = new TodoRepository(databasePath);
            return new TodoStorageStartupResult(repository, repository.LoadAll(), true);
        }
        catch
        {
            return new TodoStorageStartupResult(null, Array.Empty<TodoRecord>(), false);
        }
    }
}
