using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BlockLauncher;

/// <summary>启动画面：全屏盖住，加载完成后十字裂开</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();

        // 铺满屏幕
        WindowState = WindowState.Maximized;
        Loaded += (_, _) => PlayIn();
        LoadAssets();
    }

    private void LoadAssets()
    {
        try
        {
            IconImage.Source = new BitmapImage(new Uri("pack://application:,,,/splash-icon.png", UriKind.Absolute));
        }
        catch { }

        try
        {
            BgImage.Source = new BitmapImage(new Uri("pack://application:,,,/assets/hero.jpg", UriKind.Absolute));
        }
        catch { }
    }

    private void PlayIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
    }

    public void SetProgress(double pct, string? text = null)
    {
        var anim = new DoubleAnimation(BarFill.Width, Math.Max(0, Math.Min(170, 170 * pct / 100)),
            TimeSpan.FromMilliseconds(320))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BarFill.BeginAnimation(FrameworkElement.WidthProperty, anim);

        if (!string.IsNullOrEmpty(text)) StatusText.Text = text;
    }

    /// <summary>
    /// 十字裂开：把当前画面截成图，切成四块，朝四个方向飞出去。
    /// 主窗口已经在下面了，飞走的时候正好露出来。
    /// </summary>
    public async Task CrackOpenAsync(int durationMs = 620)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 10 || h < 10)
        {
            Close();
            return;
        }

        // 把现在的画面渲染成一张图
        RenderTargetBitmap shot;
        try
        {
            shot = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            shot.Render(this);
            shot.Freeze();
        }
        catch
        {
            await FadeOutAsync();
            return;
        }

        // 藏起原件，换成四块碎片
        ContentPanel.Visibility = Visibility.Collapsed;
        Backdrop.Visibility = Visibility.Collapsed;
        ShardLayer.Visibility = Visibility.Visible;

        var halfW = w / 2;
        var halfH = h / 2;

        var quads = new (double X, double Y, double OutX, double OutY)[]
        {
            (0, 0, -w, -h),          // 左上 → 左上
            (halfW, 0, w, -h),       // 右上 → 右上
            (0, halfH, -w, h),       // 左下 → 左下
            (halfW, halfH, w, h),    // 右下 → 右下
        };

        var animations = new System.Collections.Generic.List<AnimationTimeline>();

        foreach (var q in quads)
        {
            var img = new Image
            {
                Source = shot,
                Stretch = Stretch.None,
                Width = w,
                Height = h
            };

            // 只留自己那一块
            img.Clip = new RectangleGeometry(new Rect(q.X, q.Y, halfW, halfH));

            // 放在原点，但因为 Clip 只显示象限，所以位置天然对齐
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);

            var translate = new TranslateTransform(0, 0);
            img.RenderTransform = translate;
            ShardLayer.Children.Add(img);

            var ax = new DoubleAnimation(q.OutX, TimeSpan.FromMilliseconds(durationMs))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var ay = new DoubleAnimation(q.OutY, TimeSpan.FromMilliseconds(durationMs))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

            translate.BeginAnimation(TranslateTransform.XProperty, ax);
            translate.BeginAnimation(TranslateTransform.YProperty, ay);

            animations.Add(ax);
        }

        // 同时整体淡一点，收尾更干净
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(durationMs))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        BeginAnimation(OpacityProperty, fade);

        await Task.Delay(durationMs);
        Close();
    }

    private async Task FadeOutAsync()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        BeginAnimation(OpacityProperty, fade);
        await Task.Delay(280);
        Close();
    }
}
