using System.Diagnostics;
using System.Text.Json;
using HelloCodex.Core;

internal static class Program
{
    private static string FileAt(string name) => Path.Combine(AppContext.BaseDirectory, name);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args is ["--simulate-installer", var codexPath])
            {
                File.WriteAllText(FileAt("probe-cli.txt"), codexPath);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                start.ArgumentList.Add(PostInstallRestart.Argument);
                start.ArgumentList.Add(Environment.ProcessId.ToString());
                start.Environment["PLUS_QUOTA_INSTALLER_PROBE"] = "must-not-reach-final-process";
                using var bootstrap = Process.Start(start)!;
                File.WriteAllText(FileAt("probe-bootstrap.json"), JsonSerializer.Serialize(new
                {
                    InstallerId = Environment.ProcessId,
                    BootstrapId = bootstrap.Id
                }));
                Thread.Sleep(1200); // The bootstrap must wait until this fake installer exits.
                return 0;
            }
            if (PostInstallRestart.IsRequested(args))
            {
                PostInstallRestart.Run(args, Environment.ProcessPath!);
                return 0;
            }
            if (args is [PostInstallRestart.CompletedArgument])
            {
                File.WriteAllText(FileAt("probe-running.txt"), Environment.ProcessId.ToString());
                File.AppendAllText(FileAt("probe-completions.txt"), Environment.ProcessId + Environment.NewLine);
                var logger = new AppLogger(FileAt("probe-quota.log"));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
                var client = new AppServerClient(File.ReadAllText(FileAt("probe-cli.txt")), logger);
                try
                {
                    var quota = client.ReadQuotaAsync(new AppSettings(), timeout.Token).GetAwaiter().GetResult();
                    File.WriteAllText(FileAt("probe-result.json"), JsonSerializer.Serialize(new
                    {
                        ProcessId = Environment.ProcessId,
                        InstallerEnvironmentInherited = Environment.GetEnvironmentVariable("PLUS_QUOTA_INSTALLER_PROBE") is not null,
                        RestartRequested = PostInstallRestart.IsRequested(args),
                        AccountType = quota.Account.Type,
                        WindowMinutes = quota.Window?.WindowDurationMinutes
                    }));
                }
                finally
                {
                    client.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                Thread.Sleep(5000); // Leave time for the test runner to inspect the parent PID.
                return 0;
            }
            throw new ArgumentException("Use --simulate-installer <codex.exe path> in an isolated publish directory.");
        }
        catch (Exception ex)
        {
            File.WriteAllText(FileAt("probe-error-" + Environment.ProcessId + ".txt"), ex.ToString());
            return 1;
        }
    }
}
