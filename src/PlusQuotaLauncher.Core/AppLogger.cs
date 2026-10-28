using System.Text;

namespace PlusQuotaLauncher.Core;

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message, Exception? Exception = null);

public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _path;

    public AppLogger(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RotateIfNeeded();
    }

    public event Action<LogEntry>? EntryWritten;

    public void Info(string message) => Write(new LogEntry(DateTimeOffset.UtcNow, "INFO", message));
    public void Warning(string message) => Write(new LogEntry(DateTimeOffset.UtcNow, "WARN", message));
    public void Error(string message, Exception? exception = null) =>
        Write(new LogEntry(DateTimeOffset.UtcNow, "ERROR", message, exception));

    private void Write(LogEntry entry)
    {
        var line = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss} UTC] [{entry.Level}] {entry.Message}";
        if (entry.Exception is not null)
            line += Environment.NewLine + entry.Exception;

        lock (_gate)
            File.AppendAllText(_path, line + Environment.NewLine, new UTF8Encoding(false));

        EntryWritten?.Invoke(entry);
    }

    private void RotateIfNeeded()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length <= 5 * 1024 * 1024)
            return;
        var old = _path + ".old";
        File.Move(_path, old, true);
    }
}
