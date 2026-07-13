using DesktopSpike.Models;

namespace DesktopSpike.Services;

public sealed record ClipboardTextStorageStartupResult(
    ClipboardTextRepository? Repository,
    IReadOnlyList<ClipboardTextRecord> Records,
    bool IsAvailable);

public sealed class ClipboardTextStorageBootstrapper
{
    private readonly DatabaseInitializer _initializer = new();

    public ClipboardTextStorageStartupResult Start(string databasePath)
    {
        try
        {
            _initializer.Initialize(databasePath);
            var repository = new ClipboardTextRepository(databasePath);
            return new ClipboardTextStorageStartupResult(repository, repository.LoadAll(), true);
        }
        catch
        {
            return new ClipboardTextStorageStartupResult(null, Array.Empty<ClipboardTextRecord>(), false);
        }
    }
}
