using System.IO;
using System.Windows;
using System.Windows.Threading;
using SplitTimer.Services;

namespace SplitTimer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 界面线程异常不弹出系统对话框，写日志后继续（比赛现场不能被弹窗打断）。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(e.Exception);
        e.Handled = true;
    }

    private static void TryLog(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Directory);
            var path = Path.Combine(SettingsStore.Directory, "error.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不出来就算了，绝不因为记日志让程序崩掉。
        }
    }
}
