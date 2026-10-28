namespace PlusQuotaLauncher.Core;

public static class SchedulePolicy
{
    public static bool IsDisabled(DateTimeOffset utcNow, AppSettings settings)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, settings.SelectedTimeZone);
        var current = TimeOnly.FromDateTime(local.DateTime);
        return settings.GetDisabledRanges().Any(range => IsInRange(current, range));
    }

    private static bool IsInRange(TimeOnly current, DisabledTimeRange range) =>
        range.Start < range.End
            ? current >= range.Start && current < range.End
            : current >= range.Start || current < range.End;

    public static bool ShouldAutoSend(DateTimeOffset utcNow, QuotaWindow window, AppSettings settings)
    {
        if (IsDisabled(utcNow, settings))
            return false;
        return window.ResetUtc - utcNow > TimeSpan.FromSeconds(settings.TriggerThresholdSeconds);
    }

    public static DateTimeOffset InSelectedTimeZone(DateTimeOffset utcTime, AppSettings settings) =>
        TimeZoneInfo.ConvertTime(utcTime, settings.SelectedTimeZone);
}
