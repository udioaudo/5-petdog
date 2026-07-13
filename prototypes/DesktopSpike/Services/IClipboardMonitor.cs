using DesktopSpike.Models;
using System.Windows;

namespace DesktopSpike.Services;

public interface IClipboardMonitor : IDisposable
{
    event EventHandler<ClipboardSnapshot>? SnapshotCaptured;
    event EventHandler<string>? StatusChanged;

    ClipboardSnapshot? LastSnapshot { get; }

    void Start(Window owner);
    void Stop();
    Task<bool> CopyBackAsync(ClipboardSnapshot snapshot);
}
