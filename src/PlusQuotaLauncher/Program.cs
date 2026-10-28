using PlusQuotaLauncher.Core;
using System.Windows;

namespace PlusQuotaLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = SingleInstance.Acquire();
        if (!singleInstance.IsPrimary)
        {
            SingleInstance.SignalPrimary();
            return;
        }

        AppPaths.EnsureCreated();
        var logger = new AppLogger(AppPaths.LogFile);
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? mainWindow = null;

        application.DispatcherUnhandledException += (_, eventArgs) =>
        {
            ErrorReporter.Show(mainWindow, "界面错误", eventArgs.Exception, logger);
            eventArgs.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                logger.Error("未处理的程序错误。", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            logger.Error("后台任务错误。", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        QuotaCoordinator? coordinator = null;
        try
        {
            var settingsStore = new JsonFileStore<AppSettings>(AppPaths.SettingsFile);
            AppSettings settings;
            try
            {
                settings = settingsStore.Load();
                settings.Validate();
            }
            catch (Exception ex)
            {
                logger.Error("加载设置失败，已使用默认设置。", ex);
                settings = new AppSettings();
                System.Windows.MessageBox.Show(
                    $"设置文件无法使用，程序已恢复默认设置。\n\n{ex.Message}\n\n原文件仍保留在：{AppPaths.SettingsFile}",
                    "设置恢复",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            coordinator = new QuotaCoordinator(
                settings,
                new JsonFileStore<AppState>(AppPaths.StateFile),
                logger);
            var startHidden = args.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
            mainWindow = new MainWindow(settings, settingsStore, coordinator, logger, startHidden);
            application.MainWindow = mainWindow;
            singleInstance.StartListening(() => mainWindow.Dispatcher.BeginInvoke(mainWindow.RestoreFromTray));
            mainWindow.Show();
            application.Run();
            mainWindow.FinishAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ErrorReporter.Show(mainWindow, "启动失败", ex, logger);
        }
        finally
        {
            if (coordinator is not null)
                coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
