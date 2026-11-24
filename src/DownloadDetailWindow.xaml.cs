using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlockLauncher;

public partial class DownloadDetailWindow : Window
{
    private readonly MainWindow _main;
    private bool _closed;
    private long _lastBytes;
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly System.Collections.Generic.List<(string Stage, TextBlock Icon, TextBlock Name, TextBlock State)> _rows = new();

    public DownloadDetailWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;

        Loaded += (_, _) =>
        {
            WindowAnim.PlayIn(RootPanel);
            BuildSteps();
            Refresh();
            TickLoop();
        };

        _main.DownloadChanged += Refresh;

        Closed += (_, _) =>
        {
            _closed = true;
            _main.DownloadChanged -= Refresh;
        };
    }

    private void BuildSteps()
    {
        StepList.Children.Clear();
        _rows.Clear();

        foreach (var (stage, label, _, _, _) in _main.CurrentDownload.Steps)
        {
            var grid = new Grid { Margin = new Thickness(0, 9, 0, 9) };

            var icon = new TextBlock
            {
                Text = "…",
                Width = 26,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var name = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(26, 0, 0, 0)
            };

            var state = new TextBlock
            {
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8))
            };

            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(state);
            StepList.Children.Add(grid);

            _rows.Add((stage, icon, name, state));
        }
    }

    private void Refresh()
    {
        var st = _main.CurrentDownload;
        if (st == null) return;

        TitleText.Text = st.Title;
        PctText.Text = Math.Round(st.Percent, 1).ToString("0.0") + " %";
        StateText.Text = st.Message;

        // 单文件任务没有阶段清单，把右栏收起来
        if (StepList.Parent is FrameworkElement holder)
            holder.Visibility = st.Detailed ? Visibility.Visible : Visibility.Collapsed;

        var activeIndex = -1;
        for (var i = 0; i < _rows.Count && i < st.Steps.Count; i++)
        {
            var row = _rows[i];
            var step = st.Steps[i];

            if (step.DoneFlag)
            {
                row.Icon.Text = "✓";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                row.State.Text = "";
            }
            else if (step.Total > 0)
            {
                activeIndex = i;
                row.Icon.Text = "●";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC8, 0xFF));
                row.State.Text = step.Done + " / " + step.Total;
                row.State.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF));
            }
            else
            {
                row.Icon.Text = "…";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92));
                row.State.Text = "";
            }
        }

        if (st.Finished)
        {
            PctText.Foreground = new SolidColorBrush(st.Ok
                ? Color.FromRgb(0x4A, 0xDE, 0x80)
                : Color.FromRgb(0xF8, 0x71, 0x71));

            CancelBtn.Visibility = Visibility.Collapsed;
            HintText.Text = st.Ok ? "装好了，可以去「版本选择」启动" : "出错了，重新来一次";
        }
        else if (st.Running)
        {
            PctText.Foreground = Brushes.White;
            CancelBtn.Visibility = Visibility.Visible;
            HintText.Text = "下载期间可以关掉这个窗口，右下角的小球会继续转";
        }

        UpdateTime();
    }

    /// <summary>每秒刷一次计时（模态窗口里 DispatcherTimer 不可靠，用 async 循环）</summary>
    private async void TickLoop()
    {
        while (!_closed)
        {
            UpdateTime();
            UpdateSpeed();
            try { await Task.Delay(500); }
            catch { break; }
        }
    }

    /// <summary>按字节增量算实时速度</summary>
    private void UpdateSpeed()
    {
        var now = DateTime.UtcNow;
        var bytes = _main.CurrentDownload.Bytes;
        var seconds = (now - _lastSample).TotalSeconds;
        if (seconds < 0.4) return;

        var speed = (bytes - _lastBytes) / seconds;
        _lastBytes = bytes;
        _lastSample = now;

        if (_main.CurrentDownload.Finished || !_main.CurrentDownload.Running)
        {
            SpeedText.Text = "—";
            return;
        }

        SpeedText.Text = speed >= 1024 * 1024
            ? (speed / 1024 / 1024).ToString("0.0") + " MB/s"
            : (speed / 1024).ToString("0") + " KB/s";
    }

    private void UpdateTime()
    {
        var span = DateTime.UtcNow - _main.CurrentDownload.Started;
        if (span.TotalSeconds < 0 || span.TotalDays > 30) span = TimeSpan.Zero;

        var total = (int)span.TotalSeconds;
        TimeText.Text = total >= 3600
            ? (total / 3600) + ":" + (total % 3600 / 60).ToString("00") + ":" + (total % 60).ToString("00")
            : (total / 60) + ":" + (total % 60).ToString("00");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _main.CancelCurrentDownload?.Invoke();
        CancelBtn.IsEnabled = false;
        HintText.Text = "正在取消…";
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
