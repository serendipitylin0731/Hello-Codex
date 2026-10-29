using System.Text;

namespace PlusQuotaLauncher.Core;

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message, Exception? Exception = null);

public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _path;
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    public AppLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(_path, string.Empty, new UTF8Encoding(false));
    }

    public event Action<LogEntry>? EntryWritten;

    public void SetTimeZone(TimeZoneInfo timeZone)
    {
        lock (_gate)
            _timeZone = timeZone;
    }

    public DateTimeOffset InSelectedTimeZone(DateTimeOffset timestamp)
    {
        lock (_gate)
            return TimeZoneInfo.ConvertTime(timestamp, _timeZone);
    }

    public void Info(string message) => Write(new LogEntry(DateTimeOffset.UtcNow, "INFO", message));
    public void Warning(string message) => Write(new LogEntry(DateTimeOffset.UtcNow, "WARN", message));
    public void Error(string message, Exception? exception = null) =>
        Write(new LogEntry(DateTimeOffset.UtcNow, "ERROR", message, exception));

    private void Write(LogEntry entry)
    {
        lock (_gate)
        {
            var local = TimeZoneInfo.ConvertTime(entry.Timestamp, _timeZone);
            var line = $"[{local:yyyy-MM-dd HH:mm:ss zzz} {_timeZone.Id}] [{entry.Level}] {entry.Message}";
            if (entry.Exception is not null)
                line += Environment.NewLine + entry.Exception;
            File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));
        }

        EntryWritten?.Invoke(entry);
    }

}
