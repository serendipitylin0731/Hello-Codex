namespace PlusQuotaLauncher.Core;

public class LauncherException : Exception
{
    public LauncherException(string userMessage, string suggestion, Exception? inner = null, bool outcomeUncertain = false)
        : base(userMessage, inner)
    {
        UserMessage = userMessage;
        Suggestion = suggestion;
        OutcomeUncertain = outcomeUncertain;
    }

    public string UserMessage { get; }
    public string Suggestion { get; }
    public bool OutcomeUncertain { get; }
}

public sealed class RpcException : LauncherException
{
    public RpcException(string method, int code, string message)
        : base(
            $"Codex 拒绝了请求 {method}（错误 {code}）：{message}",
            "检查 Codex 登录状态、模型权限和配置后重试。")
    {
        Method = method;
        Code = code;
    }

    public string Method { get; }
    public int Code { get; }
}
