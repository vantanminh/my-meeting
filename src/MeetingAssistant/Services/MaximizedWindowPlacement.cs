using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MeetingAssistant.Services;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

public readonly record struct MaximizedPlacement(int X, int Y, int Width, int Height);

public readonly record struct WorkAreaOverflow(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>
/// A borderless window is maximized to the full monitor,
/// which places the bottom of the UI under the Windows taskbar. These bounds keep
/// the maximized window inside the monitor work area on whichever display it is on.
/// </summary>
public static class MaximizedWindowPlacement
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static MaximizedPlacement ForWorkArea(ScreenRect monitor, ScreenRect work)
    {
        return new MaximizedPlacement(
            Math.Abs(work.Left - monitor.Left),
            Math.Abs(work.Top - monitor.Top),
            Math.Max(0, work.Right - work.Left),
            Math.Max(0, work.Bottom - work.Top));
    }

    public static WorkAreaOverflow OverflowOutside(ScreenRect window, ScreenRect work)
    {
        return new WorkAreaOverflow(
            Math.Max(0, work.Left - window.Left),
            Math.Max(0, work.Top - window.Top),
            Math.Max(0, window.Right - work.Right),
            Math.Max(0, window.Bottom - work.Bottom));
    }

    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo && TryReadPlacement(hwnd, out var placement))
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MaxPosition = new NativePoint { X = placement.X, Y = placement.Y };
            info.MaxSize = new NativePoint { X = placement.Width, Y = placement.Height };
            Marshal.StructureToPtr(info, lParam, false);
            // Leave handled false so WPF can still apply MinWidth/MinHeight, then
            // mark the message handled itself and keep this work-area size.
        }

        return IntPtr.Zero;
    }

    internal static bool TryReadPlacement(IntPtr hwnd, out MaximizedPlacement placement)
    {
        placement = default;
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        placement = ForWorkArea(
            new ScreenRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
        return placement.Width > 0 && placement.Height > 0;
    }

    internal static bool TryMeasureOverflow(IntPtr hwnd, out WorkAreaOverflow overflow)
    {
        overflow = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window))
            return false;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        overflow = OverflowOutside(
            new ScreenRect(window.Left, window.Top, window.Right, window.Bottom),
            new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom));
        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
}
