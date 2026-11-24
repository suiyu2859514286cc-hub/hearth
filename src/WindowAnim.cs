using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockLauncher;

/// <summary>弹窗出现动画：淡入 + 从 0.97 轻微放大</summary>
public static class WindowAnim
{
    public static void PlayIn(FrameworkElement? root)
    {
        if (root == null) return;
        if (!AppSettings.Animations) return;

        root.Opacity = 0;
        var scale = new ScaleTransform(0.97, 0.97);
        root.RenderTransform = scale;
        root.RenderTransformOrigin = new Point(0.5, 0.5);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var grow = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(400))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        root.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }
}
