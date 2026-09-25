using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MeetingAssistant.Services;

public static class NativeWindowChrome
{
    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var preference = 2;
        DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));

        var immersiveDark = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref immersiveDark, sizeof(int));
        DwmSetWindowAttribute(handle, 19, ref immersiveDark, sizeof(int));

        var backdrop = 2;
        DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
