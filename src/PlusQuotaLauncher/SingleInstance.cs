namespace PlusQuotaLauncher;

internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\ChatGPTPlusQuotaLauncher.V2";
    private const string ShowEventName = @"Local\ChatGPTPlusQuotaLauncher.V2.Show";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showEvent;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listener;

    private SingleInstance(Mutex mutex, bool isPrimary, EventWaitHandle? showEvent)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
        _showEvent = showEvent;
    }

    public bool IsPrimary { get; }

    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(false, MutexName, out var created);
        EventWaitHandle? showEvent = null;
        if (created)
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return new SingleInstance(mutex, created, showEvent);
    }

    public static void SignalPrimary()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void StartListening(Action callback)
    {
        if (!IsPrimary || _showEvent is null)
            return;
        _listener = Task.Run(() =>
        {
            var handles = new WaitHandle[] { _showEvent, _cancellation.Token.WaitHandle };
            while (!_cancellation.IsCancellationRequested)
            {
                var selected = WaitHandle.WaitAny(handles);
                if (selected == 0)
                    callback();
                else
                    break;
            }
        });
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _showEvent?.Dispose();
        _mutex.Dispose();
        _cancellation.Dispose();
    }
}
