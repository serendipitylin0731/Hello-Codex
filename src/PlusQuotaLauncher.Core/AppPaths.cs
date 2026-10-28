namespace PlusQuotaLauncher.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChatGPTPlusQuotaLauncher");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string LogFile => Path.Combine(DataDirectory, "launcher.log");
    public static string WorkspaceDirectory => Path.Combine(DataDirectory, "codex_workspace");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(WorkspaceDirectory);
    }
}
