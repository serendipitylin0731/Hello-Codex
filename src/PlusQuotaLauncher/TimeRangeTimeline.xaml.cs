using PlusQuotaLauncher.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PlusQuotaLauncher;

public partial class TimeRangeTimeline : System.Windows.Controls.UserControl
{
    private sealed class IntervalEditor : INotifyPropertyChanged
    {
        private string _startText = "09:00";
        private string _endText = "18:00";

        public string StartText
        {
            get => _startText;
            set { _startText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StartText))); }
        }

        public string EndText
        {
            get => _endText;
            set { _endText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EndText))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record SegmentVisual(Border Bar, Thumb? StartHandle, Thumb? EndHandle);

    private readonly ObservableCollection<IntervalEditor> _intervals = [];
    private readonly Dictionary<IntervalEditor, SegmentVisual> _visuals = [];
    private bool _isReadOnly;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            EditHint.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RangeRows.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            AddRangeButton.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            RebuildTimeline();
        }
    }

    public TimeRangeTimeline()
    {
        InitializeComponent();
        RangeRows.ItemsSource = _intervals;
        _intervals.CollectionChanged += (_, _) => RebuildTimeline();
    }

    public void SetRanges(IEnumerable<EnabledTimeRange> ranges)
    {
        foreach (var old in _intervals)
            old.PropertyChanged -= OnIntervalChanged;
        _intervals.Clear();
        foreach (var range in ranges)
        {
            AddEditor(new IntervalEditor
            {
                StartText = FormatMinute(range.StartMinute),
                EndText = FormatMinute(range.EndMinute)
            });
        }
        RebuildTimeline();
    }

    public List<EnabledTimeRange> GetRanges()
    {
        var ranges = new List<EnabledTimeRange>();
        for (var index = 0; index < _intervals.Count; index++)
        {
            var editor = _intervals[index];
            if (!TryParseMinute(editor.StartText, false, out var start) ||
                !TryParseMinute(editor.EndText, true, out var end) || start >= end)
            {
                throw new LauncherException(
                    $"第 {index + 1} 个启用区间的时间无效。",
                    "请输入 HH:mm；开始时间早于结束时间，结束可用 24:00。跨午夜请拆成两天的区间。");
            }
            ranges.Add(new EnabledTimeRange(start, end));
        }
        return ranges;
    }

    private static string FormatMinute(int minute) => $"{minute / 60:00}:{minute % 60:00}";

    private void AddEditor(IntervalEditor editor)
    {
        editor.PropertyChanged += OnIntervalChanged;
        _intervals.Add(editor);
    }

    private void OnIntervalChanged(object? sender, PropertyChangedEventArgs e) => UpdateTimeline();

    private static bool TryParseMinute(string text, bool allowEndOfDay, out int minute)
    {
        minute = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 || parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            hours < 0 || hours > 24 || minutes < 0 || minutes > 59 ||
            hours == 24 && (minutes != 0 || !allowEndOfDay))
            return false;
        minute = hours * 60 + minutes;
        return true;
    }

    private void RebuildTimeline()
    {
        TrackCanvas.Children.Clear();
        _visuals.Clear();
        foreach (var editor in _intervals)
        {
            var bar = new Border { Height = 18, CornerRadius = new CornerRadius(8), Opacity = 0.72 };
            bar.SetResourceReference(BackgroundProperty, "BlueBrush");
            var startHandle = IsReadOnly ? null : CreateHandle(editor, true);
            var endHandle = IsReadOnly ? null : CreateHandle(editor, false);
            _visuals.Add(editor, new SegmentVisual(bar, startHandle, endHandle));
            TrackCanvas.Children.Add(bar);
            if (startHandle is not null && endHandle is not null)
            {
                TrackCanvas.Children.Add(startHandle);
                TrackCanvas.Children.Add(endHandle);
            }
        }
        UpdateTimeline();
    }

    private Thumb CreateHandle(IntervalEditor editor, bool isStart)
    {
        var handle = new Thumb
        {
            Style = (Style)Resources["TimelineHandleStyle"],
            ToolTip = isStart ? "拖动开始时间（每格 15 分钟）" : "拖动结束时间（每格 15 分钟）"
        };
        var origin = 0;
        var accumulated = 0.0;
        handle.DragStarted += (_, _) =>
        {
            accumulated = 0;
            TryParseMinute(isStart ? editor.StartText : editor.EndText, !isStart, out origin);
        };
        handle.DragDelta += (_, args) =>
        {
            if (!TryParseMinute(editor.StartText, false, out var start) ||
                !TryParseMinute(editor.EndText, true, out var end) || TrackCanvas.ActualWidth <= 0)
                return;
            accumulated += args.HorizontalChange;
            var delta = (int)Math.Round(accumulated / TrackCanvas.ActualWidth * 1440 / 15) * 15;
            var target = isStart
                ? Math.Clamp(origin + delta, 0, Math.Max(0, end - 1))
                : Math.Clamp(origin + delta, Math.Min(1440, start + 1), 1440);
            if (isStart)
                editor.StartText = FormatMinute(target);
            else
                editor.EndText = FormatMinute(target);
            UpdateTimeline();
        };
        return handle;
    }

    private void UpdateTimeline()
    {
        var width = TrackCanvas.ActualWidth;
        if (width <= 0)
            return;
        foreach (var (editor, visual) in _visuals)
        {
            if (!TryParseMinute(editor.StartText, false, out var start) ||
                !TryParseMinute(editor.EndText, true, out var end) || start >= end)
            {
                visual.Bar.Visibility = Visibility.Collapsed;
                if (visual.StartHandle is not null && visual.EndHandle is not null)
                {
                    visual.StartHandle.Visibility = Visibility.Collapsed;
                    visual.EndHandle.Visibility = Visibility.Collapsed;
                }
                continue;
            }
            visual.Bar.Visibility = Visibility.Visible;
            if (visual.StartHandle is not null && visual.EndHandle is not null)
            {
                visual.StartHandle.Visibility = Visibility.Visible;
                visual.EndHandle.Visibility = Visibility.Visible;
            }
            var left = width * start / 1440;
            var right = width * end / 1440;
            visual.Bar.Width = Math.Max(2, right - left);
            Canvas.SetLeft(visual.Bar, left);
            Canvas.SetTop(visual.Bar, 8);
            if (visual.StartHandle is not null && visual.EndHandle is not null)
            {
                Canvas.SetLeft(visual.StartHandle, left - 7);
                Canvas.SetTop(visual.StartHandle, 3);
                Canvas.SetLeft(visual.EndHandle, right - 7);
                Canvas.SetTop(visual.EndHandle, 3);
            }
        }
    }

    private void TrackCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTimeline();

    private void TrackCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsReadOnly || e.OriginalSource != TrackCanvas || TrackCanvas.ActualWidth <= 0)
            return;
        var start = Math.Clamp((int)Math.Round(e.GetPosition(TrackCanvas).X / TrackCanvas.ActualWidth * 96) * 15, 0, 1380);
        AddEditor(new IntervalEditor { StartText = FormatMinute(start), EndText = FormatMinute(start + 60) });
    }

    private void RangeTextChanged(object sender, TextChangedEventArgs e) => UpdateTimeline();

    private void AddRange_Click(object sender, RoutedEventArgs e) => AddEditor(new IntervalEditor());

    private void RemoveRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: IntervalEditor editor })
        {
            editor.PropertyChanged -= OnIntervalChanged;
            _intervals.Remove(editor);
        }
    }
}
