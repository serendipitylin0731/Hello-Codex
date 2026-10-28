using System.Text.Json.Serialization;

namespace PlusQuotaLauncher.Core;

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
    public string TimeZoneId { get; set; } = "UTC";
    public TimeOnly DisabledStart { get; set; } = new(16, 0);
    public TimeOnly DisabledEnd { get; set; } = new(21, 0);
    public List<DisabledTimeRange>? DisabledRanges { get; set; }
    public bool StartMonitoringOnLaunch { get; set; } = true;
    public string UiTheme { get; set; } = "system";

    [JsonIgnore]
    public TimeZoneInfo SelectedTimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

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
        var ranges = GetDisabledRanges();
        for (var index = 0; index < ranges.Count; index++)
        {
            if (ranges[index].Start == ranges[index].End)
            {
                throw new LauncherException(
                    $"第 {index + 1} 个免打扰时间段的开始和结束时间不能相同。",
                    "请修改或移除该时间段。");
            }
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
