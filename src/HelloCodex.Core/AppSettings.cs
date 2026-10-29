using System.Text.Json.Serialization;

namespace HelloCodex.Core;

public sealed class AppSettings
{
    public string CodexPath { get; set; } = "codex";
    public string LimitId { get; set; } = "codex";
    public string Model { get; set; } = "gpt-5.6-luna";
    public string Effort { get; set; } = "low";
    public string Message { get; set; } = "1";
    public int PollSeconds { get; set; } = 120;
    public int SuccessSleepSeconds { get; set; } = 600;
    public int WindowMinutes { get; set; } = 300;
    public int TriggerThresholdSeconds { get; set; } = 4 * 3600 + 59 * 60;
    public string TimeZoneId { get; set; } = "system";
    public TimeOnly DisabledStart { get; set; } = new(16, 0);
    public TimeOnly DisabledEnd { get; set; } = new(21, 0);
    public List<DisabledTimeRange>? DisabledRanges { get; set; }
    public List<EnabledTimeRange>? EnabledRanges { get; set; }
    public bool CustomizeByWeekday { get; set; }
    public List<DayEnabledSchedule>? WeeklyEnabledRanges { get; set; }
    public bool StartMonitoringOnLaunch { get; set; } = true;
    public string UiTheme { get; set; } = "system";

    [JsonIgnore]
    public TimeZoneInfo SelectedTimeZone => TimeZoneId == "system"
        ? TimeZoneInfo.Local
        : TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CodexPath))
            throw new LauncherException("Codex CLI 路径不能为空。", "填写 codex，或选择 codex.exe / codex.cmd 的完整路径。");
        if (string.IsNullOrWhiteSpace(LimitId))
            throw new LauncherException("额度桶名称不能为空。", "通常填写 codex；可通过“立即检查”确认账户返回的额度桶。");
        if (string.IsNullOrWhiteSpace(Model))
            throw new LauncherException("模型名称不能为空。", "填写你的 ChatGPT Plus 账户在 Codex 中可用的模型名称。");
        if (string.IsNullOrWhiteSpace(Effort))
            throw new LauncherException("思考强度不能为空。", "可填写 low、medium、high 等账户与模型支持的值。");
        if (string.IsNullOrWhiteSpace(Message))
            throw new LauncherException("发送内容不能为空。", "请输入手动或自动触发时要发送给 Codex 的内容。");
        if (PollSeconds < 30 || PollSeconds > 86400)
            throw new LauncherException("检查间隔必须在 30 秒到 24 小时之间。", "建议保持默认的 120 秒。");
        if (SuccessSleepSeconds < 1 || SuccessSleepSeconds > 86400)
            throw new LauncherException("发送后的等待时间必须在 1 秒到 24 小时之间。", "建议保持默认的 600 秒。");
        if (WindowMinutes < 1 || TriggerThresholdSeconds < 0)
            throw new LauncherException("额度窗口或触发阈值无效。", "恢复默认设置后重试。");
        if (EnabledRanges is null && !CustomizeByWeekday)
        {
            var legacyRanges = GetDisabledRanges();
            for (var index = 0; index < legacyRanges.Count; index++)
            {
                if (legacyRanges[index].Start == legacyRanges[index].End)
                {
                    throw new LauncherException(
                        $"第 {index + 1} 个旧版免打扰时间段无效。",
                        "请修改或移除该时间段，然后保存新的启用时间。");
                }
            }
        }
        if (EnabledRanges is not null)
            ValidateRanges(EnabledRanges, "每天相同");
        if (CustomizeByWeekday)
        {
            if (WeeklyEnabledRanges is null || WeeklyEnabledRanges.Count != 7 ||
                WeeklyEnabledRanges.Select(day => day.Day).Distinct().Count() != 7 ||
                WeeklyEnabledRanges.Any(day => !Enum.IsDefined(day.Day)))
                throw new LauncherException("每日定制需要包含周一到周日的设置。", "请重新打开每日定制并保存。");
            foreach (var day in WeeklyEnabledRanges)
                ValidateRanges(day.Ranges, day.Day.ToString());
        }
        if (UiTheme is not ("system" or "light" or "dark"))
            throw new LauncherException("界面主题设置无效。", "请选择跟随系统、浅色或深色。");
        try
        {
            _ = SelectedTimeZone;
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new LauncherException("所选时区在当前 Windows 中不存在。", "重新选择时区并保存。", ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new LauncherException("所选时区数据损坏。", "在 Windows 中修复时区设置，或选择 UTC。", ex);
        }
    }

    public IReadOnlyList<DisabledTimeRange> GetDisabledRanges() =>
        DisabledRanges ?? [new DisabledTimeRange(DisabledStart, DisabledEnd)];

    public IReadOnlyList<EnabledTimeRange> GetEnabledRanges(DayOfWeek day) => CustomizeByWeekday
        ? WeeklyEnabledRanges?.FirstOrDefault(schedule => schedule.Day == day)?.Ranges ?? []
        : EnabledRanges ?? ConvertLegacyDisabledRanges();

    public void NormalizeSchedules()
    {
        Validate();
        if (EnabledRanges is not null)
            EnabledRanges = MergeRanges(EnabledRanges);
        if (WeeklyEnabledRanges is not null)
        {
            foreach (var day in WeeklyEnabledRanges)
                day.Ranges = MergeRanges(day.Ranges);
        }
    }

    public static List<EnabledTimeRange> MergeRanges(IEnumerable<EnabledTimeRange> ranges)
    {
        var ordered = ranges.OrderBy(range => range.StartMinute).ThenBy(range => range.EndMinute);
        var merged = new List<EnabledTimeRange>();
        foreach (var range in ordered)
        {
            if (merged.Count > 0 && range.StartMinute <= merged[^1].EndMinute)
                merged[^1].EndMinute = Math.Max(merged[^1].EndMinute, range.EndMinute);
            else
                merged.Add(new EnabledTimeRange(range.StartMinute, range.EndMinute));
        }
        return merged;
    }

    private List<EnabledTimeRange> ConvertLegacyDisabledRanges()
    {
        var blocked = new List<EnabledTimeRange>();
        foreach (var range in GetDisabledRanges())
        {
            var start = range.Start.Hour * 60 + range.Start.Minute;
            var end = range.End.Hour * 60 + range.End.Minute;
            if (start < end)
                blocked.Add(new EnabledTimeRange(start, end));
            else
            {
                blocked.Add(new EnabledTimeRange(start, 1440));
                if (end > 0)
                    blocked.Add(new EnabledTimeRange(0, end));
            }
        }
        var enabled = new List<EnabledTimeRange>();
        var cursor = 0;
        foreach (var range in MergeRanges(blocked))
        {
            if (cursor < range.StartMinute)
                enabled.Add(new EnabledTimeRange(cursor, range.StartMinute));
            cursor = Math.Max(cursor, range.EndMinute);
        }
        if (cursor < 1440)
            enabled.Add(new EnabledTimeRange(cursor, 1440));
        return enabled;
    }

    private static void ValidateRanges(IEnumerable<EnabledTimeRange>? ranges, string label)
    {
        if (ranges is null)
            throw new LauncherException($"{label}的启用时间设置缺失。", "请重新设置并保存。");
        var index = 0;
        foreach (var range in ranges)
        {
            index++;
            if (range is null || range.StartMinute < 0 || range.EndMinute > 1440 ||
                range.StartMinute >= range.EndMinute)
            {
                throw new LauncherException(
                    $"{label}的第 {index} 个启用区间无效。",
                    "请将开始和结束时间设置在 00:00–24:00 之间，且结束晚于开始。");
            }
        }
    }
}

public sealed class EnabledTimeRange
{
    public EnabledTimeRange()
    {
    }

    public EnabledTimeRange(int startMinute, int endMinute)
    {
        StartMinute = startMinute;
        EndMinute = endMinute;
    }

    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

public sealed class DayEnabledSchedule
{
    public DayEnabledSchedule()
    {
    }

    public DayEnabledSchedule(DayOfWeek day, List<EnabledTimeRange> ranges)
    {
        Day = day;
        Ranges = ranges;
    }

    public DayOfWeek Day { get; set; }
    public List<EnabledTimeRange> Ranges { get; set; } = [];
}

public sealed class DisabledTimeRange
{
    public DisabledTimeRange()
    {
    }

    public DisabledTimeRange(TimeOnly start, TimeOnly end)
    {
        Start = start;
        End = end;
    }

    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

public sealed class AppState
{
    public long LastSuccessResetAt { get; set; }
    public long LastSuccessUnix { get; set; }
    public string LastThreadId { get; set; } = string.Empty;
    public long PendingResetAt { get; set; }
}
