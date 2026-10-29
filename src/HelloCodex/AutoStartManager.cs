using Microsoft.Win32;

namespace HelloCodex;

internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HelloCodex";
    private const string LegacyValueName = "ChatGPTPlusQuotaLauncher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key is null)
            return false;
        var current = key.GetValue(ValueName) as string;
        var legacy = key.GetValue(LegacyValueName) as string;
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            // Replace the old login target with this installed executable.
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(current) &&
                executable is not null &&
                executable.EndsWith("HelloCodex.exe", StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
            if (!string.IsNullOrWhiteSpace(current) ||
                executable?.EndsWith("HelloCodex.exe", StringComparison.OrdinalIgnoreCase) == true)
                key.DeleteValue(LegacyValueName, false);
            current = key.GetValue(ValueName) as string;
        }
        return !string.IsNullOrWhiteSpace(current);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ??
                        Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ??
                             System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
                             throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
        key.DeleteValue(LegacyValueName, false);
    }
}
