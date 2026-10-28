using PlusQuotaLauncher.Core;

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
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        MainForm? mainForm = null;
        Application.ThreadException += (_, eventArgs) =>
            ErrorReporter.Show(mainForm, "界面错误", eventArgs.Exception, logger);
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
                MessageBox.Show(
                    $"设置文件无法使用，程序已恢复默认设置。\n\n{ex.Message}\n\n原文件仍保留在：{AppPaths.SettingsFile}",
                    "设置恢复",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            var coordinator = new QuotaCoordinator(
                settings,
                new JsonFileStore<AppState>(AppPaths.StateFile),
                logger);
            var startHidden = args.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));
            mainForm = new MainForm(settings, settingsStore, coordinator, logger, startHidden);
            singleInstance.StartListening(() =>
            {
                if (mainForm.IsHandleCreated)
                    mainForm.BeginInvoke(mainForm.RestoreFromTray);
            });
            Application.Run(mainForm);
            mainForm.FinishAsync().GetAwaiter().GetResult();
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ErrorReporter.Show(mainForm, "启动失败", ex, logger);
        }
    }
}
