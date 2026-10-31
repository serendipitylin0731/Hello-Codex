using HelloCodex.Core;
using System.Windows;

namespace HelloCodex;

internal static class ErrorReporter
{
    public static void Show(Window? owner, string title, Exception exception, AppLogger logger)
    {
        logger.Error(title, exception);
        var userMessage = exception is LauncherException launcher
            ? launcher.UserMessage
            : "程序遇到未预期错误。";
        var suggestion = exception is LauncherException known
            ? known.Suggestion
            : "请重试；如果问题持续出现，请打开日志并在 GitHub Issues 中报告。";
        var message = $"{userMessage}\n\n处理建议：{suggestion}\n\n技术信息：{exception.GetType().Name}: {exception.Message}\n\n日志：{AppPaths.LogFile}";
        if (owner is null || !owner.IsVisible)
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
