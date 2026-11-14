using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace KeySim;

/// <summary>
/// 端到端验证用的按键注入器：keysim &lt;进程名&gt; [w:毫秒 | k:键名 | t:窗口标题] ...
/// 只用于本地测试，不参与正式构建。
/// </summary>
internal static class Program
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: keysim <processName> [w:ms | k:KEY | fg] ...");
            return 2;
        }

        var name = args[0].Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        try
        {
            // 不做 DPI 感知的话，UIA 的矩形和 SetCursorPos 会差一个缩放比例。
            SetProcessDpiAwarenessContext(PerMonitorAwareV2);
        }
        catch
        {
            // 老系统没有这个 API，130% 缩放下点击可能偏，能接受。
        }

        var process = Process.GetProcessesByName(name).FirstOrDefault();
        if (process is null)
        {
            Console.Error.WriteLine($"process not found: {name}");
            return 1;
        }

        var hwnd = FindMainWindow((uint)process.Id);
        if (hwnd == IntPtr.Zero)
        {
            Console.Error.WriteLine("no visible window");
            return 1;
        }

        ShowWindow(hwnd, 9); // SW_RESTORE
        BringWindowToTop(hwnd);

        // 前台窗口切换有系统限制，先模拟一次 Alt 按键解锁（常用做法）。
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        if (GetForegroundWindow() != hwnd)
        {
            SetForegroundWindow(hwnd);
            Thread.Sleep(200);
        }

        Thread.Sleep(150);

        if (GetForegroundWindow() != hwnd)
        {
            Console.Error.WriteLine("warning: target window is not foreground");
        }

        foreach (var arg in args.Skip(1))
        {
            var parts = arg.Split(':', 2);
            var kind = parts[0];
            var value = parts.Length > 1 ? parts[1] : string.Empty;

            switch (kind)
            {
                case "w":
                    Thread.Sleep(int.Parse(value));
                    break;

                case "k":
                    Tap(ParseVk(value));
                    break;

                case "hold":
                    var holdVk = ParseVk(value);
                    keybd_event(holdVk, 0, 0, UIntPtr.Zero);
                    break;

                case "release":
                    keybd_event(ParseVk(value), 0, 2, UIntPtr.Zero);
                    break;

                case "click":
                    if (!ClickByName(value))
                    {
                        return 1;
                    }

                    break;

                case "toggle":
                    if (!ToggleByName(value))
                    {
                        return 1;
                    }

                    break;

                case "shot":
                    var shotArgs = value.Split('|', 2);
                    if (shotArgs.Length != 2 || !ShotWindow((uint)process.Id, shotArgs[0], shotArgs[1], false))
                    {
                        return 1;
                    }

                    break;

                case "shotx":
                    var shotxArgs = value.Split('|', 2);
                    if (shotxArgs.Length != 2 || !ShotWindow((uint)process.Id, shotxArgs[0], shotxArgs[1], true))
                    {
                        return 1;
                    }

                    break;

                case "screen":
                    if (!ShotScreen(value))
                    {
                        return 1;
                    }

                    break;

                case "windows":
                    ListWindows((uint)process.Id);
                    break;

                case "rect":
                    PrintRect((uint)process.Id, value);
                    break;

                case "clickp":
                    var point = value.Split(',');
                    SetCursorPos(int.Parse(point[0]), int.Parse(point[1]));
                    Thread.Sleep(60);
                    mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(50);
                    mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
                    Thread.Sleep(300);
                    Console.WriteLine($"clicked point {value}");
                    break;

                default:
                    Console.Error.WriteLine($"unknown step: {arg}");
                    return 2;
            }
        }

        Console.WriteLine("ok");
        return 0;
    }

    private static void Tap(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        Thread.Sleep(18);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
        Thread.Sleep(12);
    }

    /// <summary>按 UIA 名称找到控件，再用真实鼠标点它（对话框是模态的，Invoke 会卡住调用方）。</summary>
    private static bool ClickByName(string name)
    {
        var element = FindByName(name, ControlType.Button);
        if (element is null)
        {
            Console.Error.WriteLine($"button not found: {name}");
            return false;
        }

        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0)
        {
            Console.Error.WriteLine($"element has no bounds: {name}");
            return false;
        }

        var x = (int)(rect.Left + rect.Width / 2);
        var y = (int)(rect.Top + rect.Height / 2);
        SetCursorPos(x, y);
        Thread.Sleep(80);
        mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(350);
        Console.WriteLine($"clicked {name} at {x},{y}");
        return true;
    }

    /// <summary>按名称 + 控件类型查找，避免命中文案相同的 TextBlock。</summary>
    private static AutomationElement? FindByName(string name, ControlType type)
    {
        var condition = new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, name),
            new PropertyCondition(AutomationElement.ControlTypeProperty, type));
        return AutomationElement.RootElement.FindFirst(TreeScope.Descendants, condition);
    }

    /// <summary>直接走 TogglePattern 切换开关，比模拟鼠标稳。</summary>
    private static bool ToggleByName(string name)
    {
        var element = FindByName(name, ControlType.CheckBox);
        if (element is null)
        {
            Console.Error.WriteLine($"checkbox not found: {name}");
            return false;
        }

        if (!element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
        {
            Console.Error.WriteLine($"not togglable: {name}");
            return false;
        }

        ((TogglePattern)pattern).Toggle();
        Thread.Sleep(220);
        Console.WriteLine($"toggled {name} -> {element.GetCurrentPropertyValue(TogglePattern.ToggleStateProperty)}");
        return true;
    }

    /// <summary>按窗口标题过滤后截图（设置对话框不是最大窗口，不能靠面积挑）。</summary>
    private static bool ShotWindow(uint pid, string titleFilter, string path, bool fromScreen)
    {
        var target = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var buffer = new StringBuilder(256);
            GetWindowText(hwnd, buffer, buffer.Capacity);
            if (buffer.ToString().Contains(titleFilter, StringComparison.OrdinalIgnoreCase))
            {
                target = hwnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero)
        {
            Console.Error.WriteLine($"window not found: {titleFilter}");
            return false;
        }

        if (!GetWindowRect(target, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            if (fromScreen)
            {
                // 屏幕拷贝：读到的是合成后的真实画面，不会拿到窗口的旧缓存。
                graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
            }
            else
            {
                // PrintWindow 前先强制重绘一次，否则 WPF 窗口可能把上一帧交回来
                // （读数明明已经归零，截出来还是旧数字）。
                RedrawWindow(target, IntPtr.Zero, IntPtr.Zero, 0x0401 | 0x0100 | 0x0080);
                var hdc = graphics.GetHdc();
                var ok = PrintWindow(target, hdc, 2);
                graphics.ReleaseHdc(hdc);
                if (!ok)
                {
                    Console.Error.WriteLine("PrintWindow failed");
                    return false;
                }
            }
        }

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(full, ImageFormat.Png);
        Console.WriteLine($"saved {full} ({width}x{height})");
        return true;
    }

    /// <summary>整屏截图：PrintWindow 只拿得到窗口缓存，看不清有没有模态框挡在上面。</summary>
    private static bool ShotScreen(string path)
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        }

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        bitmap.Save(full, ImageFormat.Png);
        Console.WriteLine($"saved {full} ({bounds.Width}x{bounds.Height})");
        return true;
    }

    /// <summary>列出目标进程的所有窗口，排查是不是弹了模态对话框。</summary>
    private static void ListWindows(uint pid)
    {
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid)
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            Console.WriteLine($"hwnd={hwnd} visible={IsWindowVisible(hwnd)} enabled={IsWindowEnabled(hwnd)} title={title}");
            return true;
        }, IntPtr.Zero);
    }

    /// <summary>打印窗口外框（物理像素），用来算鼠标落点。</summary>
    private static void PrintRect(uint pid, string titleFilter)
    {
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd))
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            if (!title.ToString().Contains(titleFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (GetWindowRect(hwnd, out var rect))
            {
                Console.WriteLine($"rect L={rect.Left} T={rect.Top} R={rect.Right} B={rect.Bottom} " +
                                  $"w={rect.Right - rect.Left} h={rect.Bottom - rect.Top}");
            }

            return false;
        }, IntPtr.Zero);
    }

    private static byte ParseVk(string value)    {
        var upper = value.Trim().ToUpperInvariant();
        return upper switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "F11" => 0x7A,
            _ when upper.Length == 1 && char.IsLetterOrDigit(upper[0]) => ParseSingle(upper[0]),
            _ when upper.StartsWith("0X") => Convert.ToByte(upper[2..], 16),
            _ => throw new ArgumentException($"unknown key: {value}"),
        };
    }

    private static byte ParseSingle(char c) => char.IsDigit(c)
        ? (byte)c
        : (byte)c;

    private static IntPtr FindMainWindow(uint pid)
    {
        var best = IntPtr.Zero;
        long bestArea = 0;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid || !IsWindowVisible(hwnd))
            {
                return true;
            }

            // 取面积最大的可见窗口，避开 WPF 的隐藏辅助窗口。
            GetWindowRectSafe(hwnd, out var area);
            if (area <= bestArea)
            {
                return true;
            }

            bestArea = area;
            best = hwnd;
            return true;
        }, IntPtr.Zero);

        return best;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    private static void GetWindowRectSafe(IntPtr hwnd, out long area)
    {
        area = 0;
        if (GetWindowRect(hwnd, out var rect))
        {
            area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
        }
    }
}
