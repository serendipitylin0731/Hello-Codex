namespace PlusQuotaLauncher.Core;

public sealed class QuotaCoordinator : IAsyncDisposable
{
    private readonly AppLogger _logger;
    private readonly JsonFileStore<AppState> _stateStore;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private AppServerClient? _client;
    private AppSettings _settings;
    private AppState _state;

    public QuotaCoordinator(AppSettings settings, JsonFileStore<AppState> stateStore, AppLogger logger)
    {
        _settings = settings;
        _stateStore = stateStore;
        _logger = logger;
        _state = stateStore.Load();
    }

    public AppSettings Settings => _settings;

    public async Task UpdateSettingsAsync(AppSettings settings)
    {
        settings.Validate();
        await _operationGate.WaitAsync();
        try
        {
            var pathChanged = !string.Equals(_settings.CodexPath, settings.CodexPath, StringComparison.OrdinalIgnoreCase);
            _settings = settings;
            if (pathChanged && _client is not null)
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<CoordinatorStatus> CheckAsync(bool automatic, CancellationToken cancellationToken)
    {
        if (automatic && SchedulePolicy.IsDisabled(DateTimeOffset.UtcNow, _settings))
            return new CoordinatorStatus(CoordinatorState.Disabled, "当前处于禁用时段，未查询额度。");

        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            _logger.Info(automatic ? "开始自动额度检查。" : "开始手动额度检查。");
            var client = GetClient();
            var snapshot = await client.ReadQuotaAsync(_settings, cancellationToken);
            if (snapshot.Account.Type != "chatgpt")
                _logger.Warning($"当前 Codex 登录类型为 {snapshot.Account.Type}，不是 ChatGPT 订阅登录。");
            if (snapshot.Window is null)
            {
                return new CoordinatorStatus(
                    CoordinatorState.Waiting,
                    $"没有找到 {_settings.WindowMinutes} 分钟额度窗口（桶：{_settings.LimitId}）。",
                    snapshot);
            }

            var window = snapshot.Window;
            var remaining = window.ResetUtc - DateTimeOffset.UtcNow;
            var localReset = SchedulePolicy.InSelectedTimeZone(window.ResetUtc, _settings);
            _logger.Info($"额度窗口 reset={localReset:O} [{_settings.TimeZoneId}] remaining={remaining} used={window.UsedPercent}");
            if (!automatic || !SchedulePolicy.ShouldAutoSend(DateTimeOffset.UtcNow, window, _settings))
                return new CoordinatorStatus(CoordinatorState.Waiting, "额度已更新，当前无需自动发送。", snapshot);
            if (_state.LastSuccessResetAt == window.ResetsAt)
                return new CoordinatorStatus(CoordinatorState.Waiting, "本额度窗口已经发送过，已跳过。", snapshot);
            if (_state.PendingResetAt == window.ResetsAt)
                return new CoordinatorStatus(CoordinatorState.Waiting, "本额度窗口存在结果不确定的发送记录，已安全跳过。", snapshot);

            var receipt = await SendForWindowAsync(client, window, cancellationToken);
            return new CoordinatorStatus(
                CoordinatorState.Sent,
                $"已提交任务：thread {receipt.ThreadId}",
                snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("额度检查失败。", ex);
            return new CoordinatorStatus(CoordinatorState.Error, FriendlyMessage(ex), Error: ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<SendReceipt> SendManualAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var client = GetClient();
            var threadId = await client.StartThreadAsync(_settings, cancellationToken);
            var receipt = await client.StartTurnAsync(_settings, threadId, cancellationToken);
            _logger.Info($"手动发送已接受：thread={receipt.ThreadId} turn={receipt.TurnId}");
            return receipt;
        }
        catch (Exception ex)
        {
            _logger.Error("手动发送失败。", ex);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<SendReceipt> SendForWindowAsync(AppServerClient client, QuotaWindow window, CancellationToken cancellationToken)
    {
        var threadId = await client.StartThreadAsync(_settings, cancellationToken);
        if (!SchedulePolicy.ShouldAutoSend(DateTimeOffset.UtcNow, window, _settings))
            throw new LauncherException("条件在发送前发生变化，已取消本次自动发送。", "程序会在下一个检查周期重新评估。");

        _state.PendingResetAt = window.ResetsAt;
        _stateStore.Save(_state);
        try
        {
            var receipt = await client.StartTurnAsync(_settings, threadId, cancellationToken);
            _state.LastSuccessResetAt = window.ResetsAt;
            _state.LastSuccessUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _state.LastThreadId = receipt.ThreadId;
            _state.PendingResetAt = 0;
            _stateStore.Save(_state);
            _logger.Info($"自动发送已接受：thread={receipt.ThreadId} turn={receipt.TurnId}");
            return receipt;
        }
        catch (RpcException)
        {
            _state.PendingResetAt = 0;
            _stateStore.Save(_state);
            throw;
        }
        catch (LauncherException ex) when (!ex.OutcomeUncertain)
        {
            _state.PendingResetAt = 0;
            _stateStore.Save(_state);
            throw;
        }
    }

    private AppServerClient GetClient()
    {
        if (_client is null)
            _client = new AppServerClient(_settings.CodexPath, _logger);
        return _client;
    }

    public static string FriendlyMessage(Exception exception) => exception switch
    {
        LauncherException launcher => launcher.UserMessage,
        UnauthorizedAccessException => "权限不足，无法访问所需文件。",
        IOException => "文件或进程通信失败。",
        _ => "发生未预期错误，请查看日志。"
    };

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
            await _client.DisposeAsync();
        _operationGate.Dispose();
    }
}
