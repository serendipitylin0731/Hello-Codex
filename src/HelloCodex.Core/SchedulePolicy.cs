namespace HelloCodex.Core;

public static class SchedulePolicy
{
    public static bool IsEnabled(DateTimeOffset utcNow, AppSettings settings)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, settings.SelectedTimeZone);
        var minute = local.Hour * 60 + local.Minute;
        return settings.GetEnabledRanges(local.DayOfWeek)
            .Any(range => minute >= range.StartMinute && minute < range.EndMinute);
    }

    public static bool IsDisabled(DateTimeOffset utcNow, AppSettings settings) => !IsEnabled(utcNow, settings);

    public static bool ShouldAutoSend(DateTimeOffset utcNow, QuotaWindow window, AppSettings settings)
    {
        if (!IsEnabled(utcNow, settings))
            return false;
        return window.ResetUtc - utcNow > TimeSpan.FromSeconds(settings.TriggerThresholdSeconds);
    }

    public static DateTimeOffset InSelectedTimeZone(DateTimeOffset utcTime, AppSettings settings) =>
        TimeZoneInfo.ConvertTime(utcTime, settings.SelectedTimeZone);
}
