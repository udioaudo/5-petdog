namespace DesktopSpike.Models;

public sealed record WindowPlacementSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    string? MonitorDeviceName);
