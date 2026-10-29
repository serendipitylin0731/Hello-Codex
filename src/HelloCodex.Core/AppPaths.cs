namespace HelloCodex.Core;

public static class AppPaths
{
    private static readonly string LocalData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static string DataDirectory { get; } = Path.Combine(
        LocalData, "Hello! Codex");

    public static string LegacyDataDirectory => Path.Combine(LocalData, "ChatGPTPlusQuotaLauncher");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string StateFile => Path.Combine(DataDirectory, "state.json");
    public static string LogFile => Path.Combine(DataDirectory, "launcher.log");
    public static string WorkspaceDirectory => Path.Combine(DataDirectory, "codex_workspace");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(WorkspaceDirectory);
        // Keep user settings and the send-deduplication state when upgrading.
        foreach (var name in new[] { "settings.json", "state.json" })
        {
            var source = Path.Combine(LegacyDataDirectory, name);
            var destination = Path.Combine(DataDirectory, name);
            if (!File.Exists(destination) && File.Exists(source))
                File.Copy(source, destination);
        }
    }
}
