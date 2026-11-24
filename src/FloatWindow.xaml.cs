using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockLauncher;

/// <summary>右下角的下载悬浮窗</summary>
public partial class FloatWindow : Window
{
    private double _widthBase = 336;

    public FloatWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            PlaceBottomRight();
            WindowAnim.PlayIn(RootPanel);
        };
    }

    /// <summary>贴到屏幕右下角</summary>
    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 18;
        Top = area.Bottom - Height - 18;
    }

    public void SetTitle(string text) => TitleText.Text = text;

    public void SetStage(string text) => StageText.Text = text;

    /// <summary>0–100</summary>
    public void SetProgress(double pct, string? info = null)
    {
        var w = Math.Max(0, Math.Min(_widthBase, _widthBase * pct / 100.0));
        Fill.BeginAnimation(WidthProperty,
            new DoubleAnimation(Fill.Width, w, TimeSpan.FromMilliseconds(240))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        PctText.Text = Math.Round(pct, 1).ToString("0.0") + " %";
        if (info != null) InfoText.Text = info;
    }

    /// <summary>结束时变成结果样式</summary>
    public void Finish(bool ok, string message)
    {
        StageText.Text = message;
        StageText.Foreground = new SolidColorBrush(ok
            ? Color.FromRgb(0x4A, 0xDE, 0x80)
            : Color.FromRgb(0xF8, 0x71, 0x71));
        Fill.Fill = new SolidColorBrush(ok
            ? Color.FromRgb(0x4A, 0xDE, 0x80)
            : Color.FromRgb(0xF8, 0x71, 0x71));
    }

    /// <summary>用户点了关闭：外面应该取消下载</summary>
    public event Action? CloseRequested;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
        Close();
    }
}
