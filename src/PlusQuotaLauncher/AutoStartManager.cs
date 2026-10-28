using Microsoft.Win32;

namespace PlusQuotaLauncher;

internal static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ChatGPTPlusQuotaLauncher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ??
                        Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
