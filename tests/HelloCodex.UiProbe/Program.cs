using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using HelloCodex;
using HelloCodex.Core;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HelloCodexUiProbe-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        // An empty schedule starts the monitor without making a Codex request.
        var settings = new AppSettings { StartMonitoringOnLaunch = true, EnabledRanges = [] };
        var settingsStore = new JsonFileStore<AppSettings>(Path.Combine(directory, "settings.json"));
        var logger = new AppLogger(Path.Combine(directory, "probe.log"));
        var coordinator = new QuotaCoordinator(settings,
            new JsonFileStore<AppState>(Path.Combine(directory, "state.json")), logger);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new MainWindow(settings, settingsStore, coordinator, logger)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false
        };
        app.MainWindow = window;
        var exitCode = 1;
        window.Start(true);
        app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Task.Delay(1000);
                var tray = (System.Windows.Forms.NotifyIcon)typeof(MainWindow)
                    .GetField("_trayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                if (window.IsVisible || window.ShowInTaskbar || new WindowInteropHelper(window).Handle != IntPtr.Zero || !tray.Visible)
                    throw new InvalidOperationException("后台启动时窗口已创建或可见，或托盘图标不可用。");
                Console.WriteLine("PASS background start has no window and tray icon is present");
                if (!File.ReadAllText(Path.Combine(directory, "probe.log")).Contains("额度监控已启动"))
                    throw new InvalidOperationException("后台启动没有启动额度监控。");
                Console.WriteLine("PASS monitoring starts while the window is hidden");

                window.RestoreFromTray();
                await Task.Delay(500);
                if (!window.IsVisible || !window.ShowInTaskbar || new WindowInteropHelper(window).Handle == IntPtr.Zero)
                    throw new InvalidOperationException("无法从托盘恢复主窗口。");
                Console.WriteLine("PASS restore from tray displays the main window");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                typeof(MainWindow).GetMethod("ExitApplication", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            }
        });
        app.Run();
        window.FinishAsync().GetAwaiter().GetResult();
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Directory.Delete(directory, true);
        return exitCode;
    }
}
