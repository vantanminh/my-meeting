using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MeetingAssistant.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4D41;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkR = 0x52;

    private HwndSource? _source;
    private IntPtr _handle;

    public bool IsRegistered { get; private set; }
    public event EventHandler? ToggleRecordingRequested;

    public void Attach(Window window)
    {
        if (_source is not null) return;

        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);
        IsRegistered = RegisterHotKey(_handle, HotkeyId, ModControl | ModShift, VkR);
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            ToggleRecordingRequested?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero && IsRegistered)
            UnregisterHotKey(_handle, HotkeyId);
        if (_source is not null) _source.RemoveHook(WndProc);
        _source = null;
        _handle = IntPtr.Zero;
        IsRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
