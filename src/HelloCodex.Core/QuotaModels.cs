namespace HelloCodex.Core;

public sealed record AccountInfo(string Type, string? Email, string? PlanType)
{
    public string DisplayPlan => string.IsNullOrWhiteSpace(PlanType) ? "未知" : PlanType.ToUpperInvariant();
}

public sealed record QuotaWindow(
    string LimitId,
    string WindowName,
    double? UsedPercent,
    int WindowDurationMinutes,
    long ResetsAt)
{
    public DateTimeOffset ResetUtc => DateTimeOffset.FromUnixTimeSeconds(ResetsAt);
}

public sealed record QuotaSnapshot(AccountInfo Account, QuotaWindow? Window, DateTimeOffset CheckedAtUtc);

public sealed record SendReceipt(string ThreadId, string TurnId, string Status);

public enum CoordinatorState
{
    Idle,
    Checking,
    Disabled,
    Waiting,
    Sending,
    Sent,
    Error
}

public sealed record CoordinatorStatus(
    CoordinatorState State,
    string Summary,
    QuotaSnapshot? Snapshot = null,
    Exception? Error = null);
