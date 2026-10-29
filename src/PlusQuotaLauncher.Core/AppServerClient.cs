using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace PlusQuotaLauncher.Core;

public sealed class AppServerClient : IAsyncDisposable
{
    private sealed record RpcReply(JsonElement Result, int? ErrorCode, string? ErrorMessage);

    private readonly AppLogger _logger;
    private readonly string _codexPath;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcReply>> _pending = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private StreamWriter? _writer;
    private Task? _readLoop;
    private volatile bool _connectionHealthy;
    private long _nextId;

    public AppServerClient(string codexPath, AppLogger logger)
    {
        _codexPath = codexPath;
        _logger = logger;
    }

    public bool IsRunning => _connectionHealthy &&
                             _process is { HasExited: false } &&
                             _writer is not null &&
                             _readLoop is { IsCompleted: false };

    public async Task<QuotaSnapshot> ReadQuotaAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var accountResult = await CallAsync(
                    "account/read",
                    new { refreshToken = false },
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                var limitsResult = await CallAsync(
                    "account/rateLimits/read",
                    new { },
                    TimeSpan.FromSeconds(35),
                    cancellationToken);
                return new QuotaSnapshot(
                    QuotaParser.ParseAccount(accountResult),
                    QuotaParser.ParseWindow(limitsResult, settings.LimitId, settings.WindowMinutes),
                    DateTimeOffset.UtcNow);
            }
            catch (AppServerConnectionException ex) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
            {
                _connectionHealthy = false;
                _logger.Warning($"Codex 连接中断，正在重新连接并重试额度读取：{ex.UserMessage}");
            }
        }
    }

    public async Task<string> StartThreadAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var result = await CallAsync(
            "thread/start",
            new
            {
                model = settings.Model,
                cwd = AppPaths.WorkspaceDirectory,
                approvalPolicy = "never",
                sandbox = ProtocolValues.ThreadReadOnlySandbox,
                serviceName = "chatgpt_plus_quota_launcher"
            },
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (!result.TryGetProperty("thread", out var thread) ||
            !thread.TryGetProperty("id", out var id) ||
            string.IsNullOrWhiteSpace(id.GetString()))
            throw new LauncherException("Codex 创建会话后没有返回 thread ID。", "更新 Codex CLI 后重试；如仍失败，请查看日志。 ");
        return id.GetString()!;
    }

    public async Task<SendReceipt> StartTurnAsync(AppSettings settings, string threadId, CancellationToken cancellationToken)
    {
        var result = await CallAsync(
            "turn/start",
            new
            {
                threadId,
                input = new[] { new { type = "text", text = settings.Message } },
                model = settings.Model,
                effort = settings.Effort,
                cwd = AppPaths.WorkspaceDirectory,
                approvalPolicy = "never",
                sandboxPolicy = new { type = ProtocolValues.TurnReadOnlySandboxPolicy }
            },
            TimeSpan.FromSeconds(30),
            cancellationToken,
            outcomeUncertainOnTimeout: true);
        if (!result.TryGetProperty("turn", out var turn))
            throw new LauncherException("Codex 没有返回 turn 数据。", "查看日志确认任务是否已经创建，避免立即重复发送。", outcomeUncertain: true);
        var turnId = turn.TryGetProperty("id", out var id) ? id.GetString() : null;
        var status = turn.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(turnId))
            throw new LauncherException("Codex 没有返回 turn ID。", "查看日志确认任务是否已经创建，避免立即重复发送。", outcomeUncertain: true);
        if (status is "failed" or "interrupted")
            throw new LauncherException($"Codex 未接受任务，状态为 {status}。", "检查模型权限和 Codex 配置后重试。");
        return new SendReceipt(threadId, turnId, status ?? "accepted");
    }

    private async Task<JsonElement> CallAsync(
        string method,
        object parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool outcomeUncertainOnTimeout = false)
    {
        await EnsureStartedAsync(cancellationToken);
        return await CallCoreAsync(method, parameters, timeout, cancellationToken, outcomeUncertainOnTimeout);
    }

    private async Task<JsonElement> CallCoreAsync(
        string method,
        object parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool outcomeUncertainOnTimeout = false)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<RpcReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Duplicate RPC id.");

        try
        {
            await WriteAsync(
                JsonSerializer.Serialize(new { id, method, @params = parameters }),
                cancellationToken,
                outcomeUncertainOnTimeout);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeoutCts.CancelAfter(timeout);
            RpcReply reply;
            try
            {
                reply = await completion.Task.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
            {
                _connectionHealthy = false;
                throw new AppServerConnectionException(
                    $"Codex 请求 {method} 在 {timeout.TotalSeconds:0} 秒内没有响应。",
                    "程序会重建 Codex 连接；如果仍无法读取，请确认 Codex CLI 和网络状态，并查看日志。",
                    ex,
                    outcomeUncertainOnTimeout);
            }
            catch (AppServerConnectionException ex) when (outcomeUncertainOnTimeout && !ex.OutcomeUncertain)
            {
                throw new AppServerConnectionException(
                    ex.UserMessage,
                    "请求结果不确定，请先查看日志，避免重复发送。",
                    ex,
                    outcomeUncertain: true);
            }
            if (reply.ErrorCode.HasValue)
                throw new RpcException(method, reply.ErrorCode.Value, reply.ErrorMessage ?? "未知错误");
            return reply.Result;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (IsRunning)
            return;
        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
                return;
            await StopProcessAsync();
            var resolved = await ResolveCodexPathAsync(_codexPath, cancellationToken);
            var startInfo = BuildStartInfo(resolved);
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                    _logger.Warning("Codex: " + args.Data);
            };
            try
            {
                if (!_process.Start())
                    throw new InvalidOperationException("Process.Start returned false.");
            }
            catch (Exception ex)
            {
                throw new LauncherException(
                    "无法启动 Codex CLI。",
                    "安装官方 Codex CLI，执行 codex login，并检查设置中的 CLI 路径。",
                    ex);
            }
            _process.BeginErrorReadLine();
            _writer = _process.StandardInput;
            _writer.AutoFlush = true;
            _readLoop = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _lifetime.Token), _lifetime.Token);

            try
            {
                await CallCoreAsync(
                    "initialize",
                    new { clientInfo = new { name = "chatgpt_plus_quota_launcher", title = "ChatGPT Plus 额度启动器", version = "2.4.0" } },
                    TimeSpan.FromSeconds(25),
                    cancellationToken);
                await WriteAsync(JsonSerializer.Serialize(new { method = "initialized", @params = new { } }), cancellationToken);
                if (_process.HasExited || _readLoop is not { IsCompleted: false })
                    throw new AppServerConnectionException("Codex App Server 初始化后立即断开。", "程序会重新连接；如持续发生，请查看诊断日志。");
                _connectionHealthy = true;
                _logger.Info($"已连接 Codex App Server：{resolved}");
            }
            catch
            {
                await StopProcessAsync();
                throw;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;
                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException ex)
                {
                    _logger.Warning($"Codex App Server 输出了无效的 JSON，已跳过该行：{ex.Message}");
                    continue;
                }
                using (document)
                {
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        _logger.Warning("Codex App Server 输出的 JSON 不是对象，已跳过该行。");
                        continue;
                    }
                    if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                        continue;
                    if (!_pending.TryRemove(id, out var completion))
                        continue;
                    if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    {
                        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode) ? parsedCode : -1;
                        var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : "未知错误";
                        completion.TrySetResult(new RpcReply(default, code, message));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(new RpcReply(result.Clone(), null, null));
                    }
                    else
                    {
                        completion.TrySetResult(new RpcReply(default, -1, "响应中缺少 result"));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
            _logger.Error("读取 Codex App Server 输出失败。", ex);
        }
        finally
        {
            _connectionHealthy = false;
            var exception = new AppServerConnectionException(
                "Codex App Server 已断开。",
                "应用会在下次操作时重新连接；如果问题持续出现，请更新 Codex CLI。",
                failure);
            foreach (var item in _pending.Values)
                item.TrySetException(exception);
            _pending.Clear();
        }
    }

    private async Task WriteAsync(string line, CancellationToken cancellationToken, bool outcomeUncertainOnWriteFailure = false)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            if (_writer is null)
                throw new AppServerConnectionException("Codex App Server 尚未连接。", "程序会在下次操作时重新连接。");
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _writer.FlushAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            _connectionHealthy = false;
            throw new AppServerConnectionException(
                "无法向 Codex App Server 发送请求。",
                "程序会在下次操作时重新连接；如已发送任务，请先查看日志。",
                ex,
                outcomeUncertainOnWriteFailure);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private static async Task<string> ResolveCodexPathAsync(string configuredPath, CancellationToken cancellationToken)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));
        if (File.Exists(expanded))
            return Path.GetFullPath(expanded);
        if (Path.IsPathRooted(expanded) || expanded.Contains(Path.DirectorySeparatorChar))
            throw new LauncherException($"找不到 Codex CLI：{expanded}", "在设置中选择有效的 codex.exe 或 codex.cmd。");

        var info = new ProcessStartInfo("where.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(expanded);
        using var process = Process.Start(info);
        if (process is null)
            throw new LauncherException("无法搜索 Codex CLI。", "确认 Windows 的 where.exe 可用，或填写 Codex CLI 完整路径。");
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(File.Exists);
        if (process.ExitCode != 0 || first is null)
            throw new LauncherException("未在 PATH 中找到 Codex CLI。", "安装官方 Codex CLI 并执行 codex login，或在设置中填写完整路径。");
        return first;
    }

    private static ProcessStartInfo BuildStartInfo(string resolvedPath)
    {
        if (resolvedPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            resolvedPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /s /c \"\"{resolvedPath}\" app-server --listen stdio://\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        var info = new ProcessStartInfo(resolvedPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        return info;
    }

    private async Task StopProcessAsync()
    {
        var process = _process;
        _connectionHealthy = false;
        _process = null;
        _writer = null;
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(1000))
                    process.Kill(true);
            }
            if (_readLoop is not null)
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        finally
        {
            _readLoop = null;
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await StopProcessAsync();
        _lifetime.Dispose();
        _startGate.Dispose();
        _writeGate.Dispose();
    }
}
