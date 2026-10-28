using System.Diagnostics;
using System.Reflection;
using PlusQuotaLauncher.Core;

namespace PlusQuotaLauncher;

internal sealed class MainForm : Form
{
    private sealed record TimeZoneChoice(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    private readonly JsonFileStore<AppSettings> _settingsStore;
    private readonly QuotaCoordinator _coordinator;
    private readonly AppLogger _logger;
    private readonly bool _startHidden;
    private readonly NotifyIcon _trayIcon;
    private AppSettings _settings;
    private CancellationTokenSource? _monitorCancellation;
    private Task? _monitorTask;
    private bool _allowExit;
    private bool _busy;
    private DateTimeOffset _lastErrorNotification;
    private string _lastErrorText = string.Empty;

    private readonly Label _stateLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _accountLabel = new();
    private readonly Label _quotaLabel = new();
    private readonly Label _resetLabel = new();
    private readonly Label _recentEventLabel = new();
    private readonly Button _monitorButton = new();
    private readonly Button _checkButton = new();
    private readonly Button _manualSendButton = new();
    private readonly Button _saveButton = new();
    private readonly ComboBox _timeZoneCombo = new();
    private readonly DateTimePicker _disabledStartPicker = new();
    private readonly DateTimePicker _disabledEndPicker = new();
    private readonly TextBox _modelBox = new();
    private readonly ComboBox _effortCombo = new();
    private readonly TextBox _messageBox = new();
    private readonly TextBox _codexPathBox = new();
    private readonly TextBox _limitIdBox = new();
    private readonly NumericUpDown _pollSecondsBox = new();
    private readonly CheckBox _monitorOnLaunchBox = new();
    private readonly CheckBox _autoStartBox = new();
    private readonly Button _advancedButton = new();
    private readonly Panel _advancedPanel = new();
    private readonly TableLayoutPanel _settingsLayout = new();
    private readonly Panel _settingsScrollPanel = new();

    public MainForm(
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

        Text = "ChatGPT Plus 额度启动器";
        Width = 1040;
        Height = 720;
        MinimumSize = new Size(960, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 247, 251);
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        BuildUi();
        LoadSettingsIntoUi();

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("显示主窗口", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("立即检查额度", null, async (_, _) => await CheckNowAsync());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "ChatGPT Plus 额度启动器",
            Visible = true,
            ContextMenuStrip = trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _logger.EntryWritten += OnLogEntry;
        FormClosing += OnFormClosing;
        FormClosed += OnFormClosed;
        Shown += OnShown;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(8, 34, 68), Padding = new Padding(24, 14, 24, 14) };
        root.Controls.Add(header, 0, 0);
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(headerLayout);
        var logo = new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 12, 4)
        };
        headerLayout.Controls.Add(logo, 0, 0);

        var headerText = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(0, 15, 0, 0)
        };
        headerLayout.Controls.Add(headerText, 1, 0);
        var heading = new Label
        {
            Text = "ChatGPT Plus 额度启动器",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            AutoSize = true,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = Padding.Empty
        };
        headerText.Controls.Add(heading);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(20),
            BackColor = BackColor
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.Controls.Add(content, 0, 1);

        content.Controls.Add(BuildStatusCard(), 0, 0);
        content.Controls.Add(BuildSettingsCard(), 1, 0);
    }

    private Control BuildStatusCard()
    {
        var card = CreateCard(new Padding(24, 22, 24, 22), new Padding(0, 0, 10, 0));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        for (var i = 0; i < 6; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        var titleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        titleRow.Controls.Add(new Label
        {
            Text = "额度状态",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 5, 14, 0)
        });
        _stateLabel.Text = "未启动";
        _stateLabel.AutoSize = true;
        _stateLabel.Padding = new Padding(10, 5, 10, 5);
        _stateLabel.BackColor = Color.FromArgb(229, 233, 240);
        _stateLabel.ForeColor = Color.FromArgb(65, 74, 92);
        titleRow.Controls.Add(_stateLabel);
        layout.Controls.Add(titleRow);

        ConfigureInfoLabel(_summaryLabel, "等待首次额度检查。", true);
        layout.Controls.Add(_summaryLabel);

        var quotaPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            BackColor = Color.FromArgb(244, 249, 253),
            Padding = new Padding(18, 15, 18, 15),
            Margin = new Padding(0, 16, 0, 8)
        };
        var quotaLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        ConfigureInfoLabel(_quotaLabel, "五小时额度尚未读取", true);
        _quotaLabel.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
        _quotaLabel.ForeColor = Color.FromArgb(8, 78, 126);
        _quotaLabel.Margin = Padding.Empty;
        ConfigureInfoLabel(_resetLabel, "重置时间将在检查后显示");
        _resetLabel.Margin = new Padding(0, 7, 0, 0);
        quotaLayout.Controls.Add(_quotaLabel);
        quotaLayout.Controls.Add(_resetLabel);
        quotaPanel.Controls.Add(quotaLayout);
        layout.Controls.Add(quotaPanel);

        ConfigureInfoLabel(_accountLabel, "尚未读取账户信息");
        _accountLabel.ForeColor = Color.FromArgb(83, 94, 112);
        layout.Controls.Add(_accountLabel);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 18, 0, 16) };
        ConfigureButton(_monitorButton, "启动监控", Color.FromArgb(8, 112, 184), Color.White);
        ConfigureButton(_checkButton, "立即检查", Color.FromArgb(226, 241, 250), Color.FromArgb(8, 78, 126));
        ConfigureButton(_manualSendButton, "手动发送", Color.FromArgb(255, 235, 196), Color.FromArgb(129, 75, 0));
        _monitorButton.Click += async (_, _) => await ToggleMonitoringAsync();
        _checkButton.Click += async (_, _) => await CheckNowAsync();
        _manualSendButton.Click += async (_, _) => await ManualSendAsync();
        buttons.Controls.AddRange(new Control[] { _monitorButton, _checkButton, _manualSendButton });
        layout.Controls.Add(buttons);

        var eventPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            BackColor = Color.FromArgb(249, 250, 252),
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 8, 0, 0)
        };
        eventPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        eventPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _recentEventLabel.Text = "暂无事件";
        _recentEventLabel.AutoEllipsis = true;
        _recentEventLabel.Dock = DockStyle.Fill;
        _recentEventLabel.TextAlign = ContentAlignment.MiddleLeft;
        _recentEventLabel.ForeColor = Color.FromArgb(83, 94, 112);
        var openLogButton = new Button { Text = "诊断日志", AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty };
        openLogButton.FlatAppearance.BorderSize = 0;
        openLogButton.ForeColor = Color.FromArgb(8, 78, 126);
        openLogButton.Click += (_, _) => OpenLog();
        eventPanel.Controls.Add(_recentEventLabel, 0, 0);
        eventPanel.Controls.Add(openLogButton, 1, 0);
        layout.Controls.Add(eventPanel, 0, 6);
        return card;
    }

    private Control BuildSettingsCard()
    {
        var card = CreateCard(new Padding(24, 22, 24, 22), new Padding(10, 0, 0, 0));
        _settingsScrollPanel.Dock = DockStyle.Fill;
        _settingsScrollPanel.AutoScroll = false;
        _settingsScrollPanel.Margin = Padding.Empty;
        card.Controls.Add(_settingsScrollPanel);
        var layout = _settingsLayout;
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.ColumnCount = 1;
        layout.RowCount = 0;
        layout.AutoScroll = false;
        layout.Padding = new Padding(0);
        layout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _settingsScrollPanel.Controls.Add(layout);

        var titleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = "使用设置",
            Font = new Font(Font.FontFamily, 12F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };
        ConfigureButton(_saveButton, "保存", Color.FromArgb(8, 112, 184), Color.White);
        _saveButton.Margin = Padding.Empty;
        _saveButton.Click += async (_, _) => await SaveSettingsAsync(true);
        titleRow.Controls.Add(title, 0, 0);
        titleRow.Controls.Add(_saveButton, 1, 0);
        AddFullRow(layout, titleRow);
        AddFullRow(layout, new Label
        {
            Text = "设置免打扰时间，以及任务发送内容。",
            AutoSize = true,
            ForeColor = Color.FromArgb(83, 94, 112),
            Margin = new Padding(0, 5, 0, 14)
        });

        var essentials = CreateSettingsGrid();
        AddFullRow(layout, essentials);

        _timeZoneCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _timeZoneCombo.Dock = DockStyle.Fill;
        AddField(essentials, "时区", _timeZoneCombo);

        ConfigureTimePicker(_disabledStartPicker);
        ConfigureTimePicker(_disabledEndPicker);
        var disabledRange = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        disabledRange.Controls.Add(_disabledStartPicker);
        disabledRange.Controls.Add(new Label { Text = "至", AutoSize = true, Margin = new Padding(9, 6, 9, 0), ForeColor = Color.FromArgb(83, 94, 112) });
        disabledRange.Controls.Add(_disabledEndPicker);
        AddField(essentials, "免打扰", disabledRange, "此时间段内暂停自动检查，可跨午夜。");

        _messageBox.Dock = DockStyle.Fill;
        AddField(essentials, "发送内容", _messageBox);

        _monitorOnLaunchBox.Text = "打开程序后自动监控";
        _monitorOnLaunchBox.AutoSize = true;
        AddWideField(essentials, _monitorOnLaunchBox);
        _autoStartBox.Text = "随 Windows 登录自动启动";
        _autoStartBox.AutoSize = true;
        AddWideField(essentials, _autoStartBox);

        _advancedButton.Text = "高级设置  ›";
        _advancedButton.Dock = DockStyle.Top;
        _advancedButton.Height = 38;
        _advancedButton.TextAlign = ContentAlignment.MiddleLeft;
        _advancedButton.FlatStyle = FlatStyle.Flat;
        _advancedButton.FlatAppearance.BorderSize = 0;
        _advancedButton.BackColor = Color.FromArgb(244, 247, 251);
        _advancedButton.ForeColor = Color.FromArgb(50, 68, 90);
        _advancedButton.Cursor = Cursors.Hand;
        _advancedButton.Margin = new Padding(0, 14, 0, 0);
        _advancedButton.Click += (_, _) => ToggleAdvancedSettings();
        AddFullRow(layout, _advancedButton);

        _advancedPanel.Dock = DockStyle.Top;
        _advancedPanel.AutoSize = true;
        _advancedPanel.Visible = false;
        _advancedPanel.Padding = new Padding(0, 12, 0, 0);
        var advanced = CreateSettingsGrid();
        _advancedPanel.Controls.Add(advanced);
        AddFullRow(layout, _advancedPanel);

        _modelBox.Dock = DockStyle.Fill;
        AddField(advanced, "模型", _modelBox);
        _effortCombo.Dock = DockStyle.Fill;
        _effortCombo.DropDownStyle = ComboBoxStyle.DropDown;
        _effortCombo.Items.AddRange(new object[] { "none", "low", "medium", "high", "xhigh", "max" });
        AddField(advanced, "思考强度", _effortCombo);
        _limitIdBox.Dock = DockStyle.Fill;
        AddField(advanced, "额度桶", _limitIdBox);

        var cliPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        cliPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cliPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _codexPathBox.Dock = DockStyle.Fill;
        var browse = new Button { Text = "浏览…", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        browse.Click += (_, _) => BrowseCodex();
        cliPanel.Controls.Add(_codexPathBox, 0, 0);
        cliPanel.Controls.Add(browse, 1, 0);
        AddField(advanced, "Codex CLI", cliPanel);

        _pollSecondsBox.Minimum = 30;
        _pollSecondsBox.Maximum = 86400;
        _pollSecondsBox.Increment = 30;
        _pollSecondsBox.Dock = DockStyle.Left;
        _pollSecondsBox.Width = 110;
        AddField(advanced, "检查间隔", _pollSecondsBox, "单位：秒，最短 30 秒。");

        return card;
    }

    private static TableLayoutPanel CreateSettingsGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddFullRow(TableLayoutPanel layout, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, row);
    }

    private void ToggleAdvancedSettings()
    {
        _advancedPanel.Visible = !_advancedPanel.Visible;
        _settingsScrollPanel.AutoScroll = _advancedPanel.Visible;
        _advancedButton.Text = _advancedPanel.Visible ? "高级设置  ⌄" : "高级设置  ›";
        _settingsLayout.PerformLayout();
    }

    private static Panel CreateCard(Padding padding, Padding margin) => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
        Padding = padding,
        Margin = margin
    };

    private static void ConfigureInfoLabel(Label label, string text, bool bold = false)
    {
        label.Text = text;
        label.AutoSize = true;
        label.MaximumSize = new Size(430, 0);
        label.Margin = new Padding(0, 7, 0, 2);
        label.ForeColor = Color.FromArgb(50, 58, 72);
        if (bold)
            label.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
    }

    private static void ConfigureButton(Button button, string text, Color backColor, Color foreColor)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(8, 4, 8, 4);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = backColor;
        button.ForeColor = foreColor;
        button.Cursor = Cursors.Hand;
        button.Margin = new Padding(0, 0, 8, 0);
    }

    private static void ConfigureTimePicker(DateTimePicker picker)
    {
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "HH:mm";
        picker.ShowUpDown = true;
        picker.Width = 110;
        picker.Dock = DockStyle.Left;
    }

    private static void AddField(TableLayoutPanel layout, string labelText, Control control, string? help = null)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = labelText, AutoSize = true, Margin = new Padding(0, 8, 8, 4) };
        var holder = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = string.IsNullOrWhiteSpace(help) ? 1 : 2,
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 7)
        };
        holder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        control.Margin = new Padding(0);
        holder.Controls.Add(control, 0, 0);
        if (!string.IsNullOrWhiteSpace(help))
            holder.Controls.Add(new Label { Text = help, AutoSize = true, ForeColor = Color.FromArgb(105, 115, 130), MaximumSize = new Size(470, 0), Margin = new Padding(0, 4, 0, 0) }, 0, 1);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(holder, 1, row);
    }

    private static void AddWideField(TableLayoutPanel layout, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0, 5, 0, 5);
        layout.Controls.Add(control, 1, row);
    }

    private void LoadSettingsIntoUi()
    {
        var zones = TimeZoneInfo.GetSystemTimeZones()
            .Select(zone => new TimeZoneChoice(zone.Id, $"{zone.DisplayName}  [{zone.Id}]"))
            .ToList();
        _timeZoneCombo.DataSource = zones;
        _timeZoneCombo.SelectedItem = zones.FirstOrDefault(zone => zone.Id == _settings.TimeZoneId) ??
                                          zones.First(zone => zone.Id == "UTC");
        _disabledStartPicker.Value = DateTime.Today.Add(_settings.DisabledStart.ToTimeSpan());
        _disabledEndPicker.Value = DateTime.Today.Add(_settings.DisabledEnd.ToTimeSpan());
        _modelBox.Text = _settings.Model;
        _effortCombo.Text = _settings.Effort;
        _messageBox.Text = _settings.Message;
        _codexPathBox.Text = _settings.CodexPath;
        _limitIdBox.Text = _settings.LimitId;
        _pollSecondsBox.Value = Math.Clamp(_settings.PollSeconds, (int)_pollSecondsBox.Minimum, (int)_pollSecondsBox.Maximum);
        _monitorOnLaunchBox.Checked = _settings.StartMonitoringOnLaunch;
        _autoStartBox.Checked = AutoStartManager.IsEnabled();
    }

    private AppSettings ReadSettingsFromUi()
    {
        var zone = _timeZoneCombo.SelectedItem as TimeZoneChoice ?? throw new LauncherException("请选择时区。", "从时区列表中选择一个项目。");
        var updated = new AppSettings
        {
            CodexPath = _codexPathBox.Text.Trim(),
            LimitId = _limitIdBox.Text.Trim(),
            Model = _modelBox.Text.Trim(),
            Effort = _effortCombo.Text.Trim(),
            Message = _messageBox.Text,
            PollSeconds = (int)_pollSecondsBox.Value,
            SuccessSleepSeconds = _settings.SuccessSleepSeconds,
            WindowMinutes = _settings.WindowMinutes,
            TriggerThresholdSeconds = _settings.TriggerThresholdSeconds,
            TimeZoneId = zone.Id,
            DisabledStart = TimeOnly.FromDateTime(_disabledStartPicker.Value),
            DisabledEnd = TimeOnly.FromDateTime(_disabledEndPicker.Value),
            StartMonitoringOnLaunch = _monitorOnLaunchBox.Checked
        };
        updated.Validate();
        return updated;
    }

    private async Task<bool> SaveSettingsAsync(bool showSuccess)
    {
        try
        {
            SetBusy(true);
            var updated = ReadSettingsFromUi();
            await _coordinator.UpdateSettingsAsync(updated);
            _settingsStore.Save(updated);
            AutoStartManager.SetEnabled(_autoStartBox.Checked);
            _settings = updated;
            _logger.Info("设置已保存。");
            if (showSuccess)
                MessageBox.Show(this, "设置已保存并立即生效。", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        try
        {
            if (!await SaveSettingsAsync(false))
                return;
            StartMonitoring();
        }
        catch
        {
        }
    }

    private void StartMonitoring()
    {
        if (_monitorCancellation is not null)
            return;
        _monitorCancellation = new CancellationTokenSource();
        var token = _monitorCancellation.Token;
        _monitorButton.Text = "暂停监控";
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
                        ShowBalloon("自动任务已启动", status.Summary, ToolTipIcon.Info);
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
        _monitorButton.Text = "启动监控";
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
            var confirmation = MessageBox.Show(
                this,
                $"将立即创建新的 Codex 会话并发送：\n\n{_settings.Message}\n\n本操作会忽略禁用时段和额度阈值，并消耗 ChatGPT Plus/Codex 使用额度。是否继续？",
                "确认手动发送",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
                return;
            SetBusy(true);
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Sending, "正在手动提交任务…"));
            var receipt = await _coordinator.SendManualAsync(CancellationToken.None);
            ApplyStatus(new CoordinatorStatus(CoordinatorState.Sent, $"手动任务已接受：thread {receipt.ThreadId}"));
            ShowBalloon("手动任务已启动", $"Thread: {receipt.ThreadId}", ToolTipIcon.Info);
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
        var (text, background, foreground) = status.State switch
        {
            CoordinatorState.Checking => ("检查中", Color.FromArgb(222, 238, 250), Color.FromArgb(5, 80, 132)),
            CoordinatorState.Disabled => ("禁用时段", Color.FromArgb(238, 232, 250), Color.FromArgb(91, 54, 148)),
            CoordinatorState.Waiting => ("监控中", Color.FromArgb(220, 244, 234), Color.FromArgb(16, 108, 70)),
            CoordinatorState.Sending => ("发送中", Color.FromArgb(255, 238, 205), Color.FromArgb(140, 80, 0)),
            CoordinatorState.Sent => ("已发送", Color.FromArgb(214, 244, 225), Color.FromArgb(0, 104, 54)),
            CoordinatorState.Error => ("发生错误", Color.FromArgb(255, 224, 224), Color.FromArgb(164, 25, 25)),
            _ => ("未启动", Color.FromArgb(229, 233, 240), Color.FromArgb(65, 74, 92))
        };
        _stateLabel.Text = text;
        _stateLabel.BackColor = background;
        _stateLabel.ForeColor = foreground;
        _summaryLabel.Text = status.Summary;

        if (status.Snapshot is not null)
        {
            var account = status.Snapshot.Account;
            _accountLabel.Text = account.Type == "chatgpt"
                ? $"账户：ChatGPT {account.DisplayPlan}"
                : $"账户类型：{account.Type}（非 ChatGPT 订阅）";
            if (status.Snapshot.Window is { } window)
            {
                _quotaLabel.Text = window.UsedPercent.HasValue
                    ? $"已使用 {window.UsedPercent:0.##}%"
                    : "额度状态可用";
                var localReset = SchedulePolicy.InSelectedTimeZone(window.ResetUtc, _settings);
                _resetLabel.Text = $"下次重置  {localReset:MM-dd HH:mm} · {_settings.TimeZoneId}";
            }
            else
            {
                _quotaLabel.Text = "未找到五小时额度";
                _resetLabel.Text = "请在高级设置中检查额度桶";
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _checkButton.Enabled = !busy;
        _manualSendButton.Enabled = !busy;
        _saveButton.Enabled = !busy;
    }

    private void OnLogEntry(LogEntry entry)
    {
        SafeUi(() =>
        {
            _recentEventLabel.Text = $"{entry.Timestamp:HH:mm}  {entry.Message}";
            _recentEventLabel.ForeColor = entry.Level == "ERROR"
                ? Color.FromArgb(164, 25, 25)
                : Color.FromArgb(83, 94, 112);
        });
    }

    private void NotifyBackgroundError(Exception exception)
    {
        var text = QuotaCoordinator.FriendlyMessage(exception);
        if (text == _lastErrorText && DateTimeOffset.UtcNow - _lastErrorNotification < TimeSpan.FromMinutes(10))
            return;
        _lastErrorText = text;
        _lastErrorNotification = DateTimeOffset.UtcNow;
        ShowBalloon("额度监控遇到问题", text + "；打开主窗口查看详情。", ToolTipIcon.Error);
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon)
    {
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = text;
        _trayIcon.BalloonTipIcon = icon;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void BrowseCodex()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择 Codex CLI",
            Filter = "Codex CLI|codex.exe;codex.cmd;codex.bat|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _codexPathBox.Text = dialog.FileName;
    }

    private static Image LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlusQuotaLauncher.logo.png")
                           ?? throw new InvalidOperationException("Embedded logo is missing.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
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

    private void OnShown(object? sender, EventArgs eventArgs)
    {
        if (_startHidden)
            BeginInvoke(() => HideToTray(false));
        if (_settings.StartMonitoringOnLaunch)
            StartMonitoring();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_allowExit || eventArgs.CloseReason != CloseReason.UserClosing)
            return;
        var choice = MessageBox.Show(
            this,
            "关闭窗口后要继续在后台监控吗？\n\n选择“是”隐藏到系统托盘；选择“否”完全退出；选择“取消”返回程序。",
            "关闭程序",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);
        if (choice == DialogResult.Yes)
        {
            eventArgs.Cancel = true;
            HideToTray(true);
        }
        else if (choice == DialogResult.No)
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

    private void OnFormClosed(object? sender, FormClosedEventArgs eventArgs)
    {
        _logger.EntryWritten -= OnLogEntry;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    public async Task FinishAsync()
    {
        StopMonitoring();
        if (_monitorTask is not null)
        {
            try { await _monitorTask; }
            catch (OperationCanceledException) { }
        }
    }

    private void HideToTray(bool notify)
    {
        Hide();
        ShowInTaskbar = false;
        if (notify)
            ShowBalloon("程序仍在后台运行", "双击托盘图标可以重新打开界面。", ToolTipIcon.Info);
    }

    public void RestoreFromTray()
    {
        SafeUi(() =>
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        });
    }

    private void ExitApplication()
    {
        SafeUi(() =>
        {
            _allowExit = true;
            StopMonitoring();
            _trayIcon.Visible = false;
            Close();
        });
    }

    private void SafeUi(Action action)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }
}
