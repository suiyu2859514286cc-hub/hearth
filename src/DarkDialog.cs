using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BlockLauncher;

/// <summary>跟启动器同风格的深色提示框和确认框，替代白色的系统 MessageBox</summary>
public static class DarkDialog
{
    private static readonly Brush PanelBrush = new SolidColorBrush(Color.FromArgb(0xF5, 0x10, 0x17, 0x20));
    private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA));
    private static readonly Brush SubTextBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xBC, 0xCF));

    /// <summary>轻提示：底部浮一条，两秒后自动消失</summary>
    public static void Toast(Window? owner, string message)
    {
        var border = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22, 13, 22, 13),
            Child = new TextBlock
            {
                Text = message,
                Foreground = TextBrush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            }
        };

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            Content = border,
            Opacity = 0
        };

        win.Show();

        if (owner != null && owner.IsVisible)
        {
            win.Left = owner.Left + (owner.ActualWidth - win.ActualWidth) / 2;
            win.Top = owner.Top + owner.ActualHeight - win.ActualHeight - 48;
        }
        else
        {
            win.Left = (SystemParameters.WorkArea.Width - win.ActualWidth) / 2;
            win.Top = SystemParameters.WorkArea.Bottom - win.ActualHeight - 100;
        }

        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
        win.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.9) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220));
            fadeOut.Completed += (_, _) => win.Close();
            win.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        };
        timer.Start();
    }

    /// <summary>提示卡片：深色，一个「知道了」按钮</summary>
    public static void Info(Window? owner, string title, string message)
    {
        var win = BuildCardWindow(owner, title, message, "知道了", out var close);
        close.Click += (_, _) => win.Close();
        win.ShowDialog();
    }

    /// <summary>从列表里选一个，返回下标；取消返回 -1</summary>
    public static int Pick(Window? owner, string title, string[] options)
    {
        var picked = -1;

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 420,
            MaxHeight = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner != null && owner.IsVisible ? owner : null
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        for (var i = 0; i < options.Length; i++)
        {
            var index = i;
            var b = new Button
            {
                Content = options[i],
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                Foreground = TextBrush,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            b.Click += (_, _) => { picked = index; win.Close(); };
            stack.Children.Add(b);
        }

        var cancel = new Button
        {
            Content = "取消",
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = TextBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancel.Click += (_, _) => win.Close();
        stack.Children.Add(cancel);

        win.Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(22, 18, 22, 18),
            Child = new ScrollViewer { Content = stack, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };

        win.Loaded += (_, _) => WindowAnim.PlayIn((FrameworkElement)win.Content);
        win.ShowDialog();
        return picked;
    }

    /// <summary>确认框：深色，返回 true 表示确定</summary>
    private static Window BuildCardWindow(Window? owner, string title, string message,
        string buttonText, out Button button)
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner != null && owner.IsVisible ? owner : null
        };

        button = new Button
        {
            Content = buttonText,
            Width = 96,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        content.Children.Add(button);

        win.Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 20, 24, 20),
            Child = content
        };

        win.Loaded += (_, _) => WindowAnim.PlayIn((FrameworkElement)win.Content);

        return win;
    }

    public static bool Confirm(Window? owner, string title, string message,
        string okText = "确定", string cancelText = "取消", bool danger = false)
    {
        var result = false;

        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner != null && owner.IsVisible ? owner : null
        };

        var okButton = new Button
        {
            Content = okText,
            Width = 96,
            Margin = new Thickness(0, 0, 8, 0),
            Background = danger
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0xDC, 0x26, 0x26))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        okButton.Click += (_, _) => { result = true; win.Close(); };

        var cancelButton = new Button
        {
            Content = cancelText,
            Width = 96,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = TextBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelButton.Click += (_, _) => { result = false; win.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = SubTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        });
        content.Children.Add(buttons);

        win.Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 20, 24, 20),
            Child = content
        };
        win.Loaded += (_, _) => WindowAnim.PlayIn((FrameworkElement)win.Content);

        win.ShowDialog();
        return result;
    }

    /// <summary>自定义内容的对话框（比如带进度的更新提示）</summary>
    public static void ShowCustom(Window? owner, string title, UIElement body,
        (string Text, bool Primary) cancel,
        (string Text, bool Primary) ok,
        Action<bool> callback)
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner != null && owner.IsVisible ? owner : null
        };

        var okButton = new Button
        {
            Content = ok.Text,
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        okButton.Click += (_, _) => { win.Close(); callback(true); };

        var cancelButton = new Button
        {
            Content = cancel.Text,
            Width = 96,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = TextBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 8, 0, 8),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelButton.Click += (_, _) => { win.Close(); callback(false); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(okButton);
        buttons.Children.Add(cancelButton);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(body);
        content.Children.Add(buttons);

        win.Content = new Border
        {
            Background = PanelBrush,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(24, 20, 24, 20),
            Child = content
        };
        win.Loaded += (_, _) => WindowAnim.PlayIn((FrameworkElement)win.Content);

        win.ShowDialog();
    }
}
