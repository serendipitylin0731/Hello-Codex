using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HelloCodex.Core;

[SupportedOSPlatform("windows")]
public static class PostInstallRestart
{
    public const string Argument = "--post-install";
    public const string CompletedArgument = "--post-install-complete";

    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static void Run(string[] args, string executablePath)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(Argument, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var installerId) ||
            installerId <= 0 || installerId == Environment.ProcessId)
            throw new LauncherException("安装后启动参数无效。", "请从开始菜单打开程序。");

        WaitForInstallerExit(installerId);
        // This process never acquires the application mutex or opens the session log.
        // The new process has no restart request, so this handoff happens only once.
        LaunchFromExplorer(executablePath, CompletedArgument);
    }

    internal static void WaitForInstallerExit(int installerId)
    {
        Process installer;
        try
        {
            installer = Process.GetProcessById(installerId);
        }
        catch (ArgumentException)
        {
            return; // Setup already finished before this process started.
        }
        using (installer)
        {
            if (!installer.WaitForExit(30_000))
                throw new LauncherException("安装程序尚未退出，暂时无法完成自动重启。", "关闭安装向导后从开始菜单打开程序。");
        }
    }

    internal static void LaunchFromExplorer(string executablePath, string arguments, int showWindow = 1)
    {
        var objects = new List<object>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            objects.Add(shell);
            dynamic windows = shell.Windows();
            objects.Add(windows);
            object location = 0;
            object root = 0;
            int desktopHandle;
            // SWC_DESKTOP=8, SWFO_NEEDDISPATCH=1. Use the existing desktop's
            // dispatch object: a newly created Shell.Application alone runs in our context.
            dynamic desktop = windows.FindWindowSW(ref location, ref root, 8, out desktopHandle, 1);
            objects.Add(desktop);
            dynamic document = desktop.Document;
            objects.Add(document);
            dynamic explorer = document.Application;
            objects.Add(explorer);
            explorer.ShellExecute(Path.GetFullPath(executablePath), arguments,
                Path.GetDirectoryName(Path.GetFullPath(executablePath)), "open", showWindow);
        }
        finally
        {
            foreach (var value in objects.AsEnumerable().Reverse().Distinct(ReferenceEqualityComparer.Instance))
            {
                if (value is not null && Marshal.IsComObject(value))
                    Marshal.ReleaseComObject(value);
            }
        }
    }
}
