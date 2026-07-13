using DesktopSpike.Models;

namespace DesktopSpike.Services;

public sealed record ClipboardImageStorageStartupResult(
    ClipboardImageRepository? Repository,
    IReadOnlyList<ClipboardImageRecord> Records,
    bool IsAvailable);

public sealed class ClipboardImageStorageBootstrapper
{
    private readonly DatabaseInitializer _initializer = new();

    public ClipboardImageStorageStartupResult Start(string databasePath, string imageDirectory)
    {
        try
        {
            _initializer.Initialize(databasePath);
            var repository = new ClipboardImageRepository(databasePath, imageDirectory);
            return new ClipboardImageStorageStartupResult(repository, repository.LoadAll(), true);
        }
        catch
        {
            return new ClipboardImageStorageStartupResult(null, Array.Empty<ClipboardImageRecord>(), false);
        }
    }
}
