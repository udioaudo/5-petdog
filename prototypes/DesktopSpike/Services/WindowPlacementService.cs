using DesktopSpike.Models;
using System.Windows;

namespace DesktopSpike.Services;

public sealed class WindowPlacementService
{
    public const double DefaultWidth = 330;
    public const double MinimumWidth = 280;
    public const double MinimumHeight = 320;
    public const double CollapsedWidth = 56;
    public const double CollapsedHeight = 96;
    public const double DefaultMargin = 12;

    public Rect CreateDefault(Rect workArea)
    {
        var width = Math.Min(DefaultWidth, workArea.Width);
        var height = Math.Min(workArea.Height, Math.Max(MinimumHeight, workArea.Height / 3));
        return new Rect(
            workArea.Right - width - Math.Min(DefaultMargin, Math.Max(0, workArea.Width - width)),
            workArea.Bottom - height - Math.Min(DefaultMargin, Math.Max(0, workArea.Height - height)),
            width,
            height);
    }

    public Rect Restore(WindowPlacementSettings? settings, IReadOnlyList<DisplayWorkArea> displays)
    {
        var display = FindRestoreDisplay(settings, displays);
        if (settings is null || !IsFinite(settings.Left) || !IsFinite(settings.Top) ||
            !IsFinite(settings.Width) || !IsFinite(settings.Height) || settings.Width <= 0 || settings.Height <= 0)
        {
            return CreateDefault(display.WorkArea);
        }

        return ClampExpanded(new Rect(settings.Left, settings.Top, settings.Width, settings.Height), display.WorkArea);
    }

    public Rect ClampExpanded(Rect bounds, Rect workArea)
    {
        var minWidth = Math.Min(MinimumWidth, workArea.Width);
        var minHeight = Math.Min(MinimumHeight, workArea.Height);
        var width = Math.Clamp(bounds.Width, minWidth, workArea.Width);
        var height = Math.Clamp(bounds.Height, minHeight, workArea.Height);
        var left = Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }

    public Rect ClampCollapsed(Point location, Rect workArea)
    {
        var width = Math.Min(CollapsedWidth, workArea.Width);
        var height = Math.Min(CollapsedHeight, workArea.Height);
        return new Rect(
            Math.Clamp(location.X, workArea.Left, workArea.Right - width),
            Math.Clamp(location.Y, workArea.Top, workArea.Bottom - height),
            width,
            height);
    }

    public DisplayWorkArea FindDisplayForBounds(Rect bounds, IReadOnlyList<DisplayWorkArea> displays)
    {
        if (displays.Count == 0)
        {
            return new DisplayWorkArea("PRIMARY", SystemParameters.WorkArea, true);
        }

        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var containing = displays.FirstOrDefault(display => display.WorkArea.Contains(center));
        if (containing is not null)
        {
            return containing;
        }

        return displays.MinBy(display => DistanceSquared(center, display.WorkArea))!;
    }

    private DisplayWorkArea FindRestoreDisplay(WindowPlacementSettings? settings, IReadOnlyList<DisplayWorkArea> displays)
    {
        if (displays.Count == 0)
        {
            return new DisplayWorkArea("PRIMARY", SystemParameters.WorkArea, true);
        }

        if (!string.IsNullOrWhiteSpace(settings?.MonitorDeviceName))
        {
            var named = displays.FirstOrDefault(display =>
                string.Equals(display.DeviceName, settings.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (named is not null)
            {
                return named;
            }
        }

        return displays.FirstOrDefault(display => display.IsPrimary) ?? displays[0];
    }

    private static double DistanceSquared(Point point, Rect rect)
    {
        var x = Math.Clamp(point.X, rect.Left, rect.Right);
        var y = Math.Clamp(point.Y, rect.Top, rect.Bottom);
        var dx = point.X - x;
        var dy = point.Y - y;
        return dx * dx + dy * dy;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
