using System.Text.Json;
using PlusQuotaLauncher.Core;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
        Console.WriteLine($"PASS {name}");
    else
    {
        Console.Error.WriteLine($"FAIL {name}");
        failures.Add(name);
    }
}

var settings = new AppSettings
{
    TimeZoneId = "UTC",
    DisabledStart = new TimeOnly(16, 0),
    DisabledEnd = new TimeOnly(21, 0)
};
Check(!SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 15, 59, 59, TimeSpan.Zero), settings), "before disabled range");
Check(SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 16, 0, 0, TimeSpan.Zero), settings), "disabled start inclusive");
Check(SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 20, 59, 59, TimeSpan.Zero), settings), "inside disabled range");
Check(!SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 21, 0, 0, TimeSpan.Zero), settings), "disabled end exclusive");

var now = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
var exact = new QuotaWindow("codex", "primary", 0, 300, now.AddSeconds(settings.TriggerThresholdSeconds).ToUnixTimeSeconds());
var over = exact with { ResetsAt = exact.ResetsAt + 1 };
Check(!SchedulePolicy.ShouldAutoSend(now, exact, settings), "threshold is strict");
Check(SchedulePolicy.ShouldAutoSend(now, over, settings), "threshold plus one second triggers");

using var document = JsonDocument.Parse("""
{
  "rateLimitsByLimitId": {
    "codex": {
      "limitId": "codex",
      "primary": { "usedPercent": 12.5, "windowDurationMins": 300, "resetsAt": 1793200000 },
      "secondary": { "usedPercent": 2, "windowDurationMins": 10080, "resetsAt": 1793800000 }
    }
  }
}
""");
var parsed = QuotaParser.ParseWindow(document.RootElement, "codex", 300);
Check(parsed is { ResetsAt: 1793200000, WindowDurationMinutes: 300 }, "parse selected five-hour window");
Check(QuotaParser.ParseWindow(document.RootElement, "other", 300) is null, "do not use unrelated bucket");

Check(ProtocolValues.ThreadReadOnlySandbox == "read-only", "thread sandbox wire value");
Check(ProtocolValues.TurnReadOnlySandboxPolicy == "readOnly", "turn sandbox policy wire value");

settings.Validate();
Check(true, "default settings validate");
Check(settings.UiTheme == "system", "default theme follows system");
var invalidTheme = new AppSettings { UiTheme = "neon" };
try
{
    invalidTheme.Validate();
    Check(false, "invalid theme rejected");
}
catch (LauncherException)
{
    Check(true, "invalid theme rejected");
}

if (Environment.GetEnvironmentVariable("PLUS_QUOTA_LIVE") == "1")
{
    AppPaths.EnsureCreated();
    var logger = new AppLogger(Path.Combine(AppPaths.DataDirectory, "integration-probe.log"));
    await using var client = new AppServerClient("codex", logger);
    var live = await client.ReadQuotaAsync(settings, CancellationToken.None);
    Check(live.Account.Type == "chatgpt", "live Codex uses ChatGPT authentication");
    Check(live.Window is { WindowDurationMinutes: 300 }, "live Codex exposes five-hour window");
    Console.WriteLine($"LIVE plan={live.Account.DisplayPlan} reset={live.Window?.ResetUtc:O}");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} regression test(s) failed.");
    return 1;
}

Console.WriteLine("All core regression tests passed.");
return 0;
