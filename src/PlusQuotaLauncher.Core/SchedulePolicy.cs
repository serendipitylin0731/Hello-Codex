namespace PlusQuotaLauncher.Core;

public static class SchedulePolicy
{
    public static bool IsDisabled(DateTimeOffset utcNow, AppSettings settings)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, settings.SelectedTimeZone);
        var current = TimeOnly.FromDateTime(local.DateTime);
        var start = settings.DisabledStart;
        var end = settings.DisabledEnd;
        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    public static bool ShouldAutoSend(DateTimeOffset utcNow, QuotaWindow window, AppSettings settings)
    {
        if (IsDisabled(utcNow, settings))
            return false;
        return window.ResetUtc - utcNow > TimeSpan.FromSeconds(settings.TriggerThresholdSeconds);
    }

    public static DateTimeOffset InSelectedTimeZone(DateTimeOffset utcTime, AppSettings settings) =>
        TimeZoneInfo.ConvertTime(utcTime, settings.SelectedTimeZone);
}
