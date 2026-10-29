using PlusQuotaLauncher.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfMessageBox = System.Windows.MessageBox;

namespace PlusQuotaLauncher;

public partial class MainWindow : Window
{
    private sealed record TimeZoneChoice(string Id, string Display);
    private sealed record ThemeChoice(string Id, string Display);
    private sealed class DisabledRangeEditor
    {
        public string Start { get; set; } = "16:00";
        public string End { get; set; } = "21:00";
    }

    private readonly JsonFileStore<AppSettings> _settingsStore;
    private readonly QuotaCoordinator _coordinator;
    private readonly AppLogger _logger;
    private readonly bool _startHidden;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly DrawingIcon _trayDrawingIcon;
    private readonly ObservableCollection<DisabledRangeEditor> _disabledRangeEditors = [];
    private AppSettings _settings;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _allowExit;
    private bool _busy;
    private DateTimeOffset _lastErrorNotification;
    private string _lastErrorText = string.Empty;
    private bool _loadingSettings;
    private bool _recentEventIsError;

    public MainWindow(
        AppSettings settings,
        JsonFileStore<AppSettings> settingsStore,
        QuotaCoordinator coordinator,
        AppLogger logger,
        bool startHidden)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _coordinator = coordinator;
        _logger = logger;
        _startHidden = startHidden;

        InitializeComponent();
        DisabledRangesItems.ItemsSource = _disabledRangeEditors;
        LoadSettingsIntoUi();

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示主窗口", null, (_, _) => SafeUi(RestoreFromTray));
        trayMenu.Items.Add("立即检查额度", null, (_, _) => SafeUi(() => _ = CheckNowAsync()));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => SafeUi(ExitApplication));
        _trayDrawingIcon = DrawingIcon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ??
                           System.Drawing.SystemIcons.Application;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayDrawingIcon,
            Text = "ChatGPT Plus 额度启动器",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => SafeUi(RestoreFromTray);

        _logger.EntryWritten += OnLogEntry;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        StateChanged += (_, _) => MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void LoadSettingsIntoUi()
    {
        _loadingSettings = true;
        var zones = TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => new TimeZoneChoice(zone.Id, $"{zone.DisplayName}  [{zone.Id}]"))
            .ToList();
        TimeZoneCombo.ItemsSource = zones;
        TimeZoneCombo.DisplayMemberPath = nameof(TimeZoneChoice.Display);
        TimeZoneCombo.SelectedValuePath = nameof(TimeZoneChoice.Id);
        TimeZoneCombo.SelectedValue = zones.Any(zone => zone.Id == _settings.TimeZoneId)
            ? _settings.TimeZoneId
            : "UTC";

        _disabledRangeEditors.Clear();
        foreach (var range in _settings.GetDisabledRanges())
        {
            _disabledRangeEditors.Add(new DisabledRangeEditor
            {
                Start = range.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                End = range.End.ToString("HH:mm", CultureInfo.InvariantCulture)
            });
        }

        ModelBox.Text = _settings.Model;
        EffortCombo.ItemsSource = new[] { "none", "low", "medium", "high", "xhigh", "max" };
        EffortCombo.Text = _settings.Effort;
        MessageBox.Text = _settings.Message;
        ThemeCombo.ItemsSource = new[]
        {
            new ThemeChoice("system", "跟随系统"),
            new ThemeChoice("light", "浅色"),
            new ThemeChoice("dark", "深色")
        };
        ThemeCombo.DisplayMemberPath = nameof(ThemeChoice.Display);
        ThemeCombo.SelectedValuePath = nameof(ThemeChoice.Id);
        ThemeCombo.SelectedValue = _settings.UiTheme;
        CodexPathBox.Text = _settings.CodexPath;
        LimitIdBox.Text = _settings.LimitId;
        PollSecondsBox.Text = _settings.PollSeconds.ToString(CultureInfo.InvariantCulture);
        MonitorOnLaunchBox.IsChecked = _settings.StartMonitoringOnLaunch;
        AutoStartBox.IsChecked = AutoStartManager.IsEnabled();
        _loadingSettings = false;
        ApplyTheme(_settings.UiTheme);
    }

    private AppSettings ReadSettingsFromUi()
    {
        var zoneId = TimeZoneCombo.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(zoneId))
            throw new LauncherException("请选择时区。", "从时区列表中选择一个项目。");
        var disabledRanges = new List<DisabledTimeRange>();
        for (var index = 0; index < _disabledRangeEditors.Count; index++)
        {
            var editor = _disabledRangeEditors[index];
            if (!TryParseTime(editor.Start, out var start) || !TryParseTime(editor.End, out var end))
            {
                throw new LauncherException(
                    $"第 {index + 1} 个免打扰时间段格式无效。",
                    "请输入 HH:mm，例如 22:00 或 06:30。");
            }
            disabledRanges.Add(new DisabledTimeRange(start, end));
        }
        if (!int.TryParse(PollSecondsBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pollSeconds))
            throw new LauncherException("检查间隔必须是整数。", "请输入 30 到 86400 之间的秒数。");

        var updated = new AppSettings
        {
            CodexPath = CodexPathBox.Text.Trim(),
            LimitId = LimitIdBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            Effort = EffortCombo.Text.Trim(),
            Message = MessageBox.Text,
            PollSeconds = pollSeconds,
            SuccessSleepSeconds = _settings.SuccessSleepSeconds,
            WindowMinutes = _settings.WindowMinutes,
            TriggerThresholdSeconds = _settings.TriggerThresholdSeconds,
            TimeZoneId = zoneId,
            DisabledStart = disabledRanges.FirstOrDefault()?.Start ?? _settings.DisabledStart,
            DisabledEnd = disabledRanges.FirstOrDefault()?.End ?? _settings.DisabledEnd,
            DisabledRanges = disabledRanges,
            StartMonitoringOnLaunch = MonitorOnLaunchBox.IsChecked == true,
            UiTheme = ThemeCombo.SelectedValue as string ?? "system"
        };
        updated.Validate();
        return updated;
    }

    private static bool TryParseTime(string value, out TimeOnly result) =>
        TimeOnly.TryParseExact(
            value.Trim(),
            new[] { "H:mm", "HH:mm" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);

    private async Task<bool> SaveSettingsAsync(bool showSuccess)
    {
        try
        {
            SetBusy(true);
            var updated = ReadSettingsFromUi();
            await _coordinator.UpdateSettingsAsync(updated);
            _settingsStore.Save(updated);
            AutoStartManager.SetEnabled(AutoStartBox.IsChecked == true);
            _settings = updated;
            _logger.SetTimeZone(updated.SelectedTimeZone);
            _logger.Info("设置已保存。");
            if (showSuccess)
            {
                WpfMessageBox.Show(
                    this,
                    "设置已保存并立即生效。",
                    "保存成功",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return true;
        }
        catch (Exception ex)
        {
            ErrorReporter.Show(this, "保存设置失败", ex, _logger);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ToggleMonitoringAsync()
    {
        if (_monitorCancellation is not null)
        {
            StopMonitoring();
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Idle, "监控已暂停。"));
            return;
        }
        if (!await SaveSettingsAsync(false))
            return;
        StartMonitoring();
    }

    private void StartMonitoring()
    {
        if (_monitorCancellation is not null)
            return;
        _monitorCancellation = new CancellationTokenSource();
        var token = _monitorCancellation.Token;
        MonitorButtonText.Text = "暂停监控";
        _trayIcon.Text = "Plus 额度启动器 · 监控中";
        _logger.Info("额度监控已启动。");
        _monitorTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                CoordinatorStatus status;
                try
                {
                    status = await _coordinator.CheckAsync(true, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                SafeUi(() =>
                {
                    ApplyStatus(status);
                    if (status.State == CoordinatorState.Error && status.Error is not null)
                        NotifyBackgroundError(status.Error);
                    if (status.State == CoordinatorState.Sent)
                        ShowBalloon("自动任务已启动", status.Summary, Forms.ToolTipIcon.Info);
                });
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollSeconds), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopMonitoring()
    {
        var cancellation = _monitorCancellation;
        _monitorCancellation = null;
        cancellation?.Cancel();
        if (cancellation is not null)
            _ = (_monitorTask ?? Task.CompletedTask).ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        MonitorButtonText.Text = "启动监控";
        _trayIcon.Text = "ChatGPT Plus 额度启动器";
        _logger.Info("额度监控已暂停。");
    }

    private async Task CheckNowAsync()
    {
        if (_busy)
            return;
        try
        {
            if (!await SaveSettingsAsync(false))
                return;
            SetBusy(true);
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Checking, "正在读取账户和额度…"));
            var status = await _coordinator.CheckAsync(false, CancellationToken.None);
            ApplyStatus(status);
            if (status.State == CoordinatorState.Error && status.Error is not null)
                ErrorReporter.Show(this, "额度检查失败", status.Error, _logger);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Error, QuotaCoordinator.FriendlyMessage(ex), Error: ex));
            ErrorReporter.Show(this, "额度检查失败", ex, _logger);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ManualSendAsync()
    {
        if (_busy)
            return;
        try
        {
            if (!await SaveSettingsAsync(false))
                return;
            var confirmation = WpfMessageBox.Show(
                this,
                $"将立即创建新的 Codex 会话并发送：\n\n{_settings.Message}\n\n本操作会忽略免打扰时段和额度阈值，并消耗 ChatGPT Plus/Codex 使用额度。是否继续？",
                "确认手动发送",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes)
                return;
            SetBusy(true);
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Sending, "正在手动提交任务…"));
            var receipt = await _coordinator.SendManualAsync(CancellationToken.None);
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Sent, $"任务已接受 · {receipt.ThreadId[..8]}"));
            ShowBalloon("手动任务已启动", $"Thread: {receipt.ThreadId}", Forms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Error, QuotaCoordinator.FriendlyMessage(ex), Error: ex));
            ErrorReporter.Show(this, "手动发送失败", ex, _logger);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyStatus(CoordinatorStatus status)
    {
        var visual = status.State switch
        {
            CoordinatorState.Checking => ("检查中", "#E8F2FF", "#1559B7"),
            CoordinatorState.Disabled => ("免打扰", "#F1ECFF", "#6941C6"),
            CoordinatorState.Waiting => ("监控中", "#E8F8F0", "#087A50"),
            CoordinatorState.Sending => ("发送中", "#FFF2D7", "#9A5B00"),
            CoordinatorState.Sent => ("已发送", "#E2F7EA", "#087A3E"),
            CoordinatorState.Error => ("发生错误", "#FDEBEC", "#B4232A"),
            _ => ("未启动", "#EEF1F5", "#526073")
        };
        StateBadgeText.Text = visual.Item1;
        StateBadgeBorder.Background = BrushFrom(visual.Item2);
        StateBadgeText.Foreground = BrushFrom(visual.Item3);
        HeaderStatusText.Text = visual.Item1;
        HeaderStatusDot.Fill = BrushFrom(visual.Item3);
        SummaryText.Text = status.Summary.TrimEnd('。');

        if (status.Snapshot is null)
            return;
        var account = status.Snapshot.Account;
        AccountText.Text = account.Type == "chatgpt"
            ? $"ChatGPT {account.DisplayPlan}"
            : $"{account.Type} · 非 ChatGPT 订阅登录";
        AccountDot.Fill = BrushFrom(account.Type == "chatgpt" ? "#12A66A" : "#B4232A");
        if (status.Snapshot.Window is { } window)
        {
            if (window.UsedPercent.HasValue)
            {
                QuotaText.Text = $"已使用 {window.UsedPercent:0.##}%";
                QuotaProgress.Value = Math.Clamp(window.UsedPercent.Value, 0, 100);
                QuotaProgress.Visibility = Visibility.Visible;
            }
            else
            {
                QuotaText.Text = "额度可用";
                QuotaProgress.Visibility = Visibility.Collapsed;
            }
            var localReset = SchedulePolicy.InSelectedTimeZone(window.ResetUtc, _settings);
            ResetText.Text = $"下次重置  {localReset:MM-dd HH:mm}";
        }
        else
        {
            QuotaText.Text = "未找到额度";
            QuotaProgress.Visibility = Visibility.Collapsed;
            ResetText.Text = "请展开高级设置检查额度桶";
        }
    }

    private static SolidColorBrush BrushFrom(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color)!);

    private void ApplyTheme(string theme)
    {
        var dark = theme == "dark" || theme == "system" && IsSystemDarkTheme();
        var palette = dark
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#0C131F",
                ["CardBackgroundBrush"] = "#151F2E",
                ["CardBorderBrush"] = "#263449",
                ["FieldBackgroundBrush"] = "#101927",
                ["FieldBorderBrush"] = "#34435A",
                ["SoftBackgroundBrush"] = "#111A27",
                ["AdvancedBackgroundBrush"] = "#101927",
                ["QuotaBackgroundBrush"] = "#102B47",
                ["QuotaHeadingBrush"] = "#71BAFF",
                ["QuotaMutedBrush"] = "#A8C4DE",
                ["InkBrush"] = "#F2F6FC",
                ["MutedBrush"] = "#9DAEC3",
                ["LineBrush"] = "#2A394E",
                ["SecondaryBackgroundBrush"] = "#17375C",
                ["SecondaryForegroundBrush"] = "#8FC6FF",
                ["WarningBackgroundBrush"] = "#4A3519",
                ["WarningForegroundBrush"] = "#FFD083",
                ["SwitchOffBrush"] = "#46556B",
                ["ProgressTrackBrush"] = "#244566"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#F3F6FA",
                ["CardBackgroundBrush"] = "#FFFFFF",
                ["CardBorderBrush"] = "#E8ECF2",
                ["FieldBackgroundBrush"] = "#FFFFFF",
                ["FieldBorderBrush"] = "#CDD5E1",
                ["SoftBackgroundBrush"] = "#F7F9FC",
                ["AdvancedBackgroundBrush"] = "#F8FAFD",
                ["QuotaBackgroundBrush"] = "#EAF3FF",
                ["QuotaHeadingBrush"] = "#0A4F96",
                ["QuotaMutedBrush"] = "#52677F",
                ["InkBrush"] = "#172033",
                ["MutedBrush"] = "#667085",
                ["LineBrush"] = "#E4E9F1",
                ["SecondaryBackgroundBrush"] = "#EAF2FE",
                ["SecondaryForegroundBrush"] = "#1559B7",
                ["WarningBackgroundBrush"] = "#FFF2D7",
                ["WarningForegroundBrush"] = "#9A5B00",
                ["SwitchOffBrush"] = "#C8D0DC",
                ["ProgressTrackBrush"] = "#D4E5F8"
            };
        foreach (var (key, value) in palette)
            Resources[key] = BrushFrom(value);
        if (!_recentEventIsError)
            RecentEventText.Foreground = (System.Windows.Media.Brush)Resources["MutedBrush"];
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CheckButton.IsEnabled = !busy;
        ManualSendButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void OnLogEntry(LogEntry entry)
    {
        SafeUi(() =>
        {
            _recentEventIsError = entry.Level == "ERROR";
            RecentEventText.Text = $"{_logger.InSelectedTimeZone(entry.Timestamp):HH:mm}  {FriendlyEvent(entry)}";
            RecentEventText.Foreground = _recentEventIsError
                ? BrushFrom("#B4232A")
                : (System.Windows.Media.Brush)Resources["MutedBrush"];
        });
    }

    private static string FriendlyEvent(LogEntry entry)
    {
        if (entry.Level == "ERROR")
            return "操作遇到问题，请打开诊断日志";
        if (entry.Message.StartsWith("额度窗口 ", StringComparison.Ordinal))
            return "额度信息已更新";
        if (entry.Message.StartsWith("开始自动额度检查", StringComparison.Ordinal) ||
            entry.Message.StartsWith("开始手动额度检查", StringComparison.Ordinal))
            return "正在检查额度";
        if (entry.Message.StartsWith("已连接 Codex App Server", StringComparison.Ordinal))
            return "已连接 Codex";
        return entry.Message.Length <= 42 ? entry.Message : entry.Message[..42] + "…";
    }

    private void NotifyBackgroundError(Exception exception)
    {
        var text = QuotaCoordinator.FriendlyMessage(exception);
        if (text == _lastErrorText && DateTimeOffset.UtcNow - _lastErrorNotification < TimeSpan.FromMinutes(10))
            return;
        _lastErrorText = text;
        _lastErrorNotification = DateTimeOffset.UtcNow;
        ShowBalloon("额度监控遇到问题", text + "；打开主窗口查看详情。", Forms.ToolTipIcon.Error);
    }

    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void BrowseCodex()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Codex CLI",
            Filter = "Codex CLI|codex.exe;codex.cmd;codex.bat|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            CodexPathBox.Text = dialog.FileName;
    }

    private void OpenLog()
    {
        try
        {
            if (!File.Exists(AppPaths.LogFile))
                File.WriteAllText(AppPaths.LogFile, string.Empty);
            Process.Start(new ProcessStartInfo(AppPaths.LogFile) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorReporter.Show(this, "无法打开日志", ex, _logger);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_startHidden)
            HideToTray(false);
        if (_settings.StartMonitoringOnLaunch)
            StartMonitoring();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowExit)
            return;
        var choice = WpfMessageBox.Show(
            this,
            "关闭窗口后要继续在后台监控吗？\n\n选择“是”隐藏到系统托盘；选择“否”完全退出；选择“取消”返回程序。",
            "关闭程序",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (choice == MessageBoxResult.Yes)
        {
            eventArgs.Cancel = true;
            HideToTray(true);
        }
        else if (choice == MessageBoxResult.No)
        {
            _allowExit = true;
            StopMonitoring();
            _trayIcon.Visible = false;
        }
        else
        {
            eventArgs.Cancel = true;
        }
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _logger.EntryWritten -= OnLogEntry;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayDrawingIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public async Task FinishAsync()
    {
        StopMonitoring();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void HideToTray(bool notify)
    {
        Hide();
        ShowInTaskbar = false;
        if (notify)
            ShowBalloon("程序仍在后台运行", "双击托盘图标可以重新打开界面。", Forms.ToolTipIcon.Info);
    }

    public void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _allowExit = true;
        StopMonitoring();
        _trayIcon.Visible = false;
        Close();
    }

    private void SafeUi(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    private async void MonitorButton_Click(object sender, RoutedEventArgs e) => await ToggleMonitoringAsync();

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await CheckNowAsync();

    private async void ManualSendButton_Click(object sender, RoutedEventArgs e) => await ManualSendAsync();

    private async void SaveButton_Click(object sender, RoutedEventArgs e) => await SaveSettingsAsync(true);

    private void AdvancedToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var expanded = AdvancedPanel.Visibility != Visibility.Visible;
        AdvancedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AdvancedChevron.Text = expanded ? "⌃" : "⌄";
    }

    private void BrowseCodexButton_Click(object sender, RoutedEventArgs e) => BrowseCodex();

    private void AddDisabledRangeButton_Click(object sender, RoutedEventArgs e)
    {
        _disabledRangeEditors.Add(new DisabledRangeEditor { Start = "22:00", End = "06:00" });
    }

    private void RemoveDisabledRangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DisabledRangeEditor editor })
            _disabledRangeEditors.Remove(editor);
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) => OpenLog();

    private void ThemeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;
        ApplyTheme(ThemeCombo.SelectedValue as string ?? "system");
    }

    private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        SafeUi(() =>
        {
            if ((ThemeCombo.SelectedValue as string ?? _settings.UiTheme) == "system")
                ApplyTheme("system");
        });
    }

    private void TitleBarDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
