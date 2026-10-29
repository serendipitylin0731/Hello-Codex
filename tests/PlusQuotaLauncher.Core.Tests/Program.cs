using System.Text.Json;
using PlusQuotaLauncher.Core;

if (args is ["app-server", ..] && Environment.GetEnvironmentVariable("PLUS_QUOTA_FAKE_MODE") is { } serverMode)
    return await RunFakeCodexAsync(serverMode, Environment.GetEnvironmentVariable("PLUS_QUOTA_FAKE_MARKER")!);

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

settings.DisabledRanges =
[
    new DisabledTimeRange(new TimeOnly(8, 0), new TimeOnly(9, 30)),
    new DisabledTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
    new DisabledTimeRange(new TimeOnly(22, 0), new TimeOnly(6, 0))
];
Check(SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 8, 15, 0, TimeSpan.Zero), settings), "first disabled range in union");
Check(SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero), settings), "second disabled range in union");
Check(SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero), settings), "overnight disabled range in union");
Check(!SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero), settings), "outside disabled range union");
settings.DisabledRanges = [];
Check(!SchedulePolicy.IsDisabled(new DateTimeOffset(2026, 10, 29, 16, 30, 0, TimeSpan.Zero), settings), "empty disabled range union");
settings.DisabledRanges = null;
var legacySettings = JsonSerializer.Deserialize<AppSettings>("""
{
  "TimeZoneId": "UTC",
  "DisabledStart": "07:30:00",
  "DisabledEnd": "08:45:00"
}
""")!;
Check(legacySettings.GetDisabledRanges() is [{ Start: var legacyStart, End: var legacyEnd }] &&
      legacyStart == new TimeOnly(7, 30) && legacyEnd == new TimeOnly(8, 45),
    "legacy disabled range migrates");
var rangeRoundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
{
    DisabledRanges = [new DisabledTimeRange(new TimeOnly(9, 0), new TimeOnly(10, 0))]
}))!;
Check(rangeRoundTrip.GetDisabledRanges() is [{ Start: var roundTripStart }] && roundTripStart == new TimeOnly(9, 0),
    "multiple disabled ranges serialize");
settings.DisabledRanges =
[
    new DisabledTimeRange(new TimeOnly(8, 0), new TimeOnly(9, 30)),
    new DisabledTimeRange(new TimeOnly(12, 0), new TimeOnly(13, 0)),
    new DisabledTimeRange(new TimeOnly(22, 0), new TimeOnly(6, 0))
];
Check(settings.GetEnabledRanges(DayOfWeek.Monday) is [{ StartMinute: 360, EndMinute: 480 },
    { StartMinute: 570, EndMinute: 720 }, { StartMinute: 780, EndMinute: 1320 }],
    "legacy disabled ranges become enabled complement");

settings.EnabledRanges =
[
    new EnabledTimeRange(720, 900),
    new EnabledTimeRange(540, 750),
    new EnabledTimeRange(900, 960)
];
settings.NormalizeSchedules();
Check(settings.EnabledRanges is [{ StartMinute: 540, EndMinute: 960 }],
    "overlapping and adjacent enabled ranges merge on save");
Check(SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero), settings),
    "enabled start inclusive");
Check(!SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero), settings),
    "enabled end exclusive");
settings.EnabledRanges = [new EnabledTimeRange(0, 1440)];
Check(SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 26, 23, 59, 0, TimeSpan.Zero), settings),
    "full day enabled through final minute");
settings.EnabledRanges = [];
Check(!SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), settings),
    "empty enabled day skips automatic checks");
settings.EnabledRanges = [new EnabledTimeRange(540, 960)];
settings.WeeklyEnabledRanges = Enum.GetValues<DayOfWeek>()
    .Select(day => new DayEnabledSchedule(day, []))
    .ToList();
settings.WeeklyEnabledRanges.Single(day => day.Day == DayOfWeek.Monday).Ranges =
    [new EnabledTimeRange(540, 720), new EnabledTimeRange(660, 780)];
settings.WeeklyEnabledRanges.Single(day => day.Day == DayOfWeek.Tuesday).Ranges =
    [new EnabledTimeRange(720, 780)];
settings.CustomizeByWeekday = true;
settings.NormalizeSchedules();
Check(settings.GetEnabledRanges(DayOfWeek.Monday) is [{ StartMinute: 540, EndMinute: 780 }],
    "weekly Monday ranges merge");
Check(SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.Zero), settings) &&
      !SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero), settings) &&
      SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero), settings),
    "Monday and Tuesday use independent enabled ranges");
settings.TimeZoneId = "China Standard Time";
settings.WeeklyEnabledRanges.Single(day => day.Day == DayOfWeek.Monday).Ranges =
    [new EnabledTimeRange(0, 120)];
Check(SchedulePolicy.IsEnabled(new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero), settings),
    "weekly day boundary follows chosen timezone");
var savedWeeklySettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
Check(savedWeeklySettings.CustomizeByWeekday &&
      savedWeeklySettings.GetEnabledRanges(DayOfWeek.Monday) is [{ StartMinute: 0, EndMinute: 120 }] &&
      savedWeeklySettings.GetEnabledRanges(DayOfWeek.Sunday).Count == 0,
    "weekly schedules survive settings round trip");
settings.TimeZoneId = "UTC";
settings.CustomizeByWeekday = false;
settings.EnabledRanges = [new EnabledTimeRange(0, 1440)];

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
Check(new AppSettings().TimeZoneId == "system" &&
      new AppSettings().SelectedTimeZone.Id == TimeZoneInfo.Local.Id,
    "new settings follow system timezone");
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

if (OperatingSystem.IsWindows())
{
    var fixtureDirectory = Path.Combine(Path.GetTempPath(), "plus-quota-transport-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(fixtureDirectory);
    try
    {
        var logPath = Path.Combine(fixtureDirectory, "launcher.log");
        File.WriteAllText(logPath, "old session log");
        var logger = new AppLogger(logPath);
        Check(new FileInfo(logPath).Length == 0, "startup clears previous log");
        var china = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        logger.SetTimeZone(china);
        var before = DateTimeOffset.UtcNow;
        logger.Info("timezone probe");
        var after = DateTimeOffset.UtcNow;
        var firstLine = File.ReadLines(logPath).Single();
        var timestamp = firstLine[1..firstLine.IndexOf(']')];
        var localTime = DateTimeOffset.ParseExact(timestamp[..26], "yyyy-MM-dd HH:mm:ss zzz", null);
        Check(timestamp.EndsWith("China Standard Time") && localTime.Offset == TimeSpan.FromHours(8) &&
              localTime >= before.AddSeconds(-1) && localTime <= after.AddSeconds(1),
            "log timestamp follows selected timezone");
        logger.SetTimeZone(TimeZoneInfo.Utc);
        logger.Info("utc probe");
        Check(File.ReadLines(logPath).Last().Contains("+00:00 UTC]"), "log timezone changes without rewriting history");

        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        try
        {
            var pathDirectory = Path.Combine(fixtureDirectory, "path");
            Directory.CreateDirectory(pathDirectory);
            var pathCodex = Path.Combine(pathDirectory, "codex.exe");
            File.WriteAllText(pathCodex, "test executable marker");
            var localAppData = Path.Combine(fixtureDirectory, "local");
            var installedCodex = Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(installedCodex)!);
            File.WriteAllText(installedCodex, "test executable marker");
            Environment.SetEnvironmentVariable("LOCALAPPDATA", localAppData);

            Environment.SetEnvironmentVariable("PATH", pathDirectory);
            Check(AppServerClient.ResolveCodexPath("codex") == pathCodex,
                "Codex CLI resolves from PATH first");
            Environment.SetEnvironmentVariable("PATH", fixtureDirectory);
            Check(AppServerClient.ResolveCodexPath("codex") == installedCodex,
                "Codex CLI resolves from user installation with stale PATH");
            try
            {
                AppServerClient.ResolveCodexPath(Path.Combine(fixtureDirectory, "missing-codex.exe"));
                Check(false, "explicit missing Codex path is rejected");
            }
            catch (LauncherException)
            {
                Check(true, "explicit missing Codex path is rejected");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("LOCALAPPDATA", oldLocalAppData);
        }

        var testExecutable = Environment.ProcessPath!;
        foreach (var mode in new[] { "malformed", "reader-ended" })
        {
            var marker = Path.Combine(fixtureDirectory, mode + ".marker");
            Environment.SetEnvironmentVariable("PLUS_QUOTA_FAKE_MODE", mode);
            Environment.SetEnvironmentVariable("PLUS_QUOTA_FAKE_MARKER", marker);
            await using var client = new AppServerClient(testExecutable, logger);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var snapshot = await client.ReadQuotaAsync(settings, timeout.Token);
            Check(snapshot.Account.Type == "chatgpt" && snapshot.Window?.WindowDurationMinutes == 300,
                $"quota read recovers from {mode}");
            if (mode == "reader-ended")
                Check(File.Exists(marker), "broken reader triggered reconnect");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Check(false, "transport and timezone regression checks");
    }
    finally
    {
        Environment.SetEnvironmentVariable("PLUS_QUOTA_FAKE_MODE", null);
        Environment.SetEnvironmentVariable("PLUS_QUOTA_FAKE_MARKER", null);
        Directory.Delete(fixtureDirectory, recursive: true);
    }
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

static async Task<int> RunFakeCodexAsync(string mode, string marker)
{
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var request = JsonDocument.Parse(line);
        if (!request.RootElement.TryGetProperty("id", out var id))
            continue;
        var method = request.RootElement.GetProperty("method").GetString();
        if (method == "account/read" && mode == "reader-ended" && !File.Exists(marker))
        {
            File.WriteAllText(marker, "first connection closed stdout");
            await Console.Out.WriteLineAsync("{not valid json");
            await Console.Out.FlushAsync();
            FakeConsoleWindows.CloseStandardOutput();
            while (await Console.In.ReadLineAsync() is not null)
            {
            }
            return 0;
        }
        if (method == "account/read" && mode == "malformed")
        {
            await Console.Out.WriteLineAsync("{not valid json");
            await Console.Out.FlushAsync();
        }
        object result = method switch
        {
            "initialize" => new { },
            "account/read" => new { account = new { type = "chatgpt", planType = "plus" } },
            "account/rateLimits/read" => new
            {
                rateLimitsByLimitId = new
                {
                    codex = new
                    {
                        limitId = "codex",
                        primary = new { usedPercent = 5, windowDurationMins = 300, resetsAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeSeconds() }
                    }
                }
            },
            _ => throw new InvalidOperationException($"Unexpected fake Codex request: {method}")
        };
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id = id.GetInt64(), result }));
        await Console.Out.FlushAsync();
    }
    return 0;
}

internal static class FakeConsoleWindows
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static void CloseStandardOutput() => CloseHandle(GetStdHandle(-11));
}
