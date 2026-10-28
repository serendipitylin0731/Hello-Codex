using PlusQuotaLauncher.Core;

namespace PlusQuotaLauncher;

internal static class ErrorReporter
{
    public static void Show(IWin32Window? owner, string title, Exception exception, AppLogger logger)
    {
        logger.Error(title, exception);
        var userMessage = exception is LauncherException launcher
            ? launcher.UserMessage
            : "程序遇到未预期错误。";
        var suggestion = exception is LauncherException known
            ? known.Suggestion
            : "请重试；如果问题持续出现，请打开日志并在 GitHub Issues 中报告。";
        var message = $"{userMessage}\n\n处理建议：{suggestion}\n\n技术信息：{exception.GetType().Name}: {exception.Message}\n\n日志：{AppPaths.LogFile}";
        MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
