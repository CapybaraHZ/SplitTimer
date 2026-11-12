using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SplitTimer.Services;

internal static class ThemeHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>让系统标题栏跟着走深色，避免白条破坏整体视觉。</summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        if (window is null)
        {
            return;
        }

        if (window.IsLoaded)
        {
            Apply(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply(window);
        }
    }

    private static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }

            // 圆角窗口（Win11）；Win10 上调用会失败，忽略即可。
            var corner = 2;
            DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        }
        catch
        {
            // 老系统没有 dwmapi 的这些属性，保持默认标题栏。
        }
    }
}
