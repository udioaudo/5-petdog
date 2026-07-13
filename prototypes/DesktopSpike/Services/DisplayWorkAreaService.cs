using DesktopSpike.Models;
using System.Runtime.InteropServices;
using System.Windows;

namespace DesktopSpike.Services;

public sealed class DisplayWorkAreaService
{
    private const int MonitorInfoPrimary = 1;
    private const int EffectiveDpi = 0;

    public IReadOnlyList<DisplayWorkArea> GetDisplays()
    {
        var displays = new List<DisplayWorkArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return true;
            }

            var scale = GetScale(monitor);
            var rect = new Rect(
                info.Work.Left / scale,
                info.Work.Top / scale,
                (info.Work.Right - info.Work.Left) / scale,
                (info.Work.Bottom - info.Work.Top) / scale);
            displays.Add(new DisplayWorkArea(
                info.DeviceName?.TrimEnd('\0') ?? string.Empty,
                rect,
                (info.Flags & MonitorInfoPrimary) != 0));
            return true;
        }, IntPtr.Zero);

        if (displays.Count == 0)
        {
            displays.Add(new DisplayWorkArea("PRIMARY", SystemParameters.WorkArea, true));
        }

        return displays;
    }

    private static double GetScale(IntPtr monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0
                ? dpiX / 96d
                : 1d;
        }
        catch (DllNotFoundException)
        {
            return 1d;
        }
        catch (EntryPointNotFoundException)
        {
            return 1d;
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? DeviceName;
    }
}
