using System.IO;

namespace DesktopSpike.Services;

public sealed class AppDataPaths
{
    public AppDataPaths(string? localAppDataRoot = null)
    {
        var root = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataDirectory = Path.Combine(root, "HMinus", "DesktopSpike", "data");
        DatabasePath = Path.Combine(DataDirectory, "hminus.db");
        ImageDirectory = Path.Combine(DataDirectory, "clipboard-images");
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string ImageDirectory { get; }
}
