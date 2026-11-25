using System;
using System.Windows;

namespace BlockLauncher;

/// <summary>
/// 窗口比屏幕工作区还大时按比例收回来。
/// 4K 开 250%~300% 缩放时逻辑分辨率只有 1280×720，
/// 固定 1200×780 的主窗口会顶出屏幕，看起来就是「位置偏了」。
/// </summary>
public static class WindowFit
{
    /// <summary>在 App 启动时挂一次，之后所有窗口（含运行时新建的）都会自动适配。</summary>
    public static void Attach()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Fit(sender as Window)));
    }

    public static void Fit(Window? window)
    {
        if (window == null) return;

        var area = SystemParameters.WorkArea;
        if (area.Width <= 0 || area.Height <= 0) return;

        // 留 6% 给阴影和任务栏边缘，别贴死
        var maxWidth = area.Width * 0.94;
        var maxHeight = area.Height * 0.94;

        var adjusted = false;

        if (window.Width > maxWidth)
        {
            window.Width = Math.Max(window.MinWidth, maxWidth);
            adjusted = true;
        }

        if (window.Height > maxHeight)
        {
            window.Height = Math.Max(window.MinHeight, maxHeight);
            adjusted = true;
        }

        // 缩小之后左上角不动，右下角会缩回来；居中的窗口要重新摆一次
        if (adjusted && window.WindowStartupLocation == WindowStartupLocation.CenterScreen)
        {
            window.Left = area.Left + (area.Width - window.Width) / 2;
            window.Top = area.Top + (area.Height - window.Height) / 2;
        }
    }
}
