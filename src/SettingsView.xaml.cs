using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BlockLauncher;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ShowPage("launch");
    }

    // ===== 小工具 =====

    private static TextBlock Text(string text, double size = 11.5, string color = "#E8F0FA", bool bold = false)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    private Border Card(string? title, UIElement content)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(title)) stack.Children.Add(Text(title, 12, "#B9C9DC", true));
        stack.Children.Add(content);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
            Child = stack
        };
    }

    private Grid Field(string label, UIElement control, double labelWidth = 110)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = Text(label, 11, "#B9C9DC");
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private Button Btn(string content, double width = 0, Action? onClick = null)
    {
        var b = new Button
        {
            Content = content,
            Style = (Style)FindResource("Btn"),
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 11
        };
        if (width > 0) b.Width = width;
        if (onClick != null) b.Click += (_, _) => onClick();
        return b;
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    // ===== 导航 =====

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) ShowPage(tag);
    }

    private void ShowPage(string page)
    {
        foreach (var b in new[] { NavLaunch, NavUI, NavOther })
        {
            if (b == null) continue;
            var on = (string)b.Tag == page;
            b.Background = on ? new SolidColorBrush(Color.FromArgb(0x33, 0x3B, 0x82, 0xF6)) : Brushes.Transparent;
            b.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6));
        }

        PageHost.Children.Clear();
        switch (page)
        {
            case "ui": ShowUI(); break;
            case "other": ShowOther(); break;
            default: ShowLaunch(); break;
        }
    }

    public void SelectPage(string page) => ShowPage(page);

    // ===== 启动 =====

    private void ShowLaunch()
    {
        var javaRow = new StackPanel();
        var javaPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var pathText = Text(string.IsNullOrEmpty(AppSettings.JavaPath) ? "自动寻找本机 Java" : AppSettings.JavaPath,
            10, "#8FA0B8");
        pathText.Margin = new Thickness(0, 6, 0, 0);
        pathText.TextTrimming = TextTrimming.CharacterEllipsis;

        var browse = Btn("浏览 java.exe…", 130, () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择 java.exe",
                Filter = "java.exe|java.exe|所有文件 (*.*)|*.*"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            {
                AppSettings.Set("javaPath", dialog.FileName);
                ShowPage("launch");
            }
        });
        var auto = Btn("自动", 70, () =>
        {
            AppSettings.Set("javaPath", "");
            ShowPage("launch");
        });
        javaPanel.Children.Add(auto);
        javaPanel.Children.Add(browse);
        javaRow.Children.Add(javaPanel);
        javaRow.Children.Add(pathText);

        var launch = new StackPanel();
        launch.Children.Add(Field("Java", javaRow));
        launch.Children.Add(Text("留自动就让启动器自己找，找不到会按版本要求下载", 9.5, "#6B7C92"));

        var mem = AppSettings.DefaultMemory;
        var memText = Text(mem + " MB", 11, "#E8F0FA");
        memText.VerticalAlignment = VerticalAlignment.Center;
        memText.Margin = new Thickness(12, 0, 0, 0);
        var slider = new Slider
        {
            Minimum = 1024,
            Maximum = 16384,
            Value = mem,
            TickFrequency = 512,
            IsSnapToTickEnabled = true,
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        slider.ValueChanged += (_, e) =>
        {
            memText.Text = (int)e.NewValue + " MB";
            AppSettings.Set("defaultMemory", ((int)e.NewValue).ToString());
        };
        var memPanel = new StackPanel { Orientation = Orientation.Horizontal };
        memPanel.Children.Add(slider);
        memPanel.Children.Add(memText);
        launch.Children.Add(Field("默认内存", memPanel));
        launch.Children.Add(Text("版本设置里单独改过的，以版本设置为准", 9.5, "#6B7C92"));

        PageHost.Children.Add(Card("启动", launch));

        var download = new StackPanel();
        download.Children.Add(Field("下载源", UiKit.ChoiceGroup(
            new[] { "自动（镜像优先）", "官方源" },
            AppSettings.DownloadSource == "official" ? 1 : 0,
            idx => AppSettings.Set("downloadSource", idx == 1 ? "official" : "auto"))));
        download.Children.Add(Field("下载线程", UiKit.ChoiceGroup(
            new[] { "4", "8", "16", "32" },
            AppSettings.DownloadThreads switch { 4 => 0, 16 => 2, 32 => 3, _ => 1 },
            idx => AppSettings.Set("downloadThreads", new[] { 4, 8, 16, 32 }[idx].ToString()))));
        PageHost.Children.Add(Card("下载", download));
    }

    // ===== 个性化 =====

    private void ShowUI()
    {
        var ui = new StackPanel();

        var animRow = new StackPanel { Orientation = Orientation.Horizontal };
        var animToggle = new ToggleSwitch(AppSettings.Animations);
        var animLabel = Text(AppSettings.Animations ? "开" : "关", 10.5, "#B9C9DC");
        animLabel.VerticalAlignment = VerticalAlignment.Center;
        animLabel.Margin = new Thickness(10, 0, 0, 0);
        animToggle.Toggled += on =>
        {
            AppSettings.Set("animations", on ? "1" : "0");
            animLabel.Text = on ? "开" : "关";
        };
        animRow.Children.Add(animToggle);
        animRow.Children.Add(animLabel);

        ui.Children.Add(Field("界面动画", animRow));
        ui.Children.Add(Text("关掉之后切页面和弹窗都不再有过渡", 9.5, "#6B7C92"));
        PageHost.Children.Add(Card("个性化", ui));

        // 标题栏
        var titleMode = AppSettings.Get("titleBarMode", "default");
        var titlePick = titleMode switch { "none" => 0, "text" => 2, _ => 1 };

        var titleBox = new TextBox
        {
            Text = AppSettings.Get("titleBarText", ""),
            FontSize = 11.5,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            Width = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = titlePick == 2
        };
        titleBox.TextChanged += (_, _) =>
        {
            AppSettings.Set("titleBarText", titleBox.Text);
            if (Window.GetWindow(this) is MainWindow mw) mw.ApplyTitleBar();
        };

        var titleStack = new StackPanel();
        titleStack.Children.Add(UiKit.ChoiceGroup(
            new[] { "无", "默认", "文本" },
            titlePick,
            i =>
            {
                titlePick = i;
                AppSettings.Set("titleBarMode", i switch { 0 => "none", 2 => "text", _ => "default" });
                titleBox.IsEnabled = i == 2;
                if (Window.GetWindow(this) is MainWindow mw) mw.ApplyTitleBar();
            }));
        var boxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var boxLabel = Text("标题栏文本", 10.5, "#B9C9DC");
        boxLabel.VerticalAlignment = VerticalAlignment.Center;
        boxLabel.Margin = new Thickness(0, 0, 10, 0);
        boxRow.Children.Add(boxLabel);
        boxRow.Children.Add(titleBox);
        titleStack.Children.Add(boxRow);

        PageHost.Children.Add(Card("标题栏", titleStack));

        // 主题（皮肤）
        var themeStack = new StackPanel();
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var theme in ThemeService.Themes) grid.Children.Add(BuildThemeCard(theme));
        themeStack.Children.Add(grid);
        PageHost.Children.Add(Card("主题", themeStack));
    }

    private Border BuildThemeCard(ThemeService.ThemeItem theme)
    {
        var unlocked = ThemeService.IsUnlocked(theme);
        var active = ThemeService.Current.Id == theme.Id;
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(theme.Hex); }
        catch { color = Colors.SlateGray; }

        var stack = new StackPanel();
        stack.Children.Add(new Border
        {
            Height = 34,
            CornerRadius = new CornerRadius(7),
            Background = theme.Id == "rainbow" && unlocked
                ? ThemeService.RainbowBrush()
                : new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = unlocked ? 1 : 0.35
        });
        stack.Children.Add(Text(theme.Name, 11, unlocked ? "#E8F0FA" : "#6B7C92", true));
        stack.Children.Add(Text(
            active ? "使用中" : unlocked ? "已解锁" : "未解锁",
            9,
            active ? "#9CC8FF" : "#6B7C92"));

        var card = new Border
        {
            Background = new SolidColorBrush(active
                ? Color.FromArgb(0x44, color.R, color.G, color.B)
                : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(active
                ? Color.FromArgb(0xAA, color.R, color.G, color.B)
                : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 8, 8),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = stack
        };

        card.ToolTip = unlocked
            ? (active ? "正在使用这个主题" : "点一下换成这个主题")
            : "还不知道怎么解锁";

        card.MouseLeftButtonUp += (_, _) =>
        {
            if (!unlocked)
            {
                DarkDialog.Info(Window.GetWindow(this),
                    "「" + theme.Name + "」还没解锁",
                    ThemeService.Hint(theme));
                return;
            }

            if (active)
            {
                DarkDialog.Toast(Window.GetWindow(this), "已经在用这个主题了");
                return;
            }

            ThemeService.Use(theme);
            if (Window.GetWindow(this) is MainWindow main) main.ApplyTheme();
            DarkDialog.Toast(Window.GetWindow(this), "已换成「" + theme.Name + "」");
            ShowPage("ui");
        };

        return card;
    }

    // ===== 其他 =====

    private void ShowOther()
    {
        var other = new StackPanel();
        var pathText = Text(VersionService.McDir, 10, "#8FA0B8");
        pathText.Margin = new Thickness(0, 6, 0, 0);
        pathText.TextTrimming = TextTrimming.CharacterEllipsis;
        other.Children.Add(Field("游戏文件夹", Btn("打开", 70, () => OpenFolder(VersionService.McDir))));
        other.Children.Add(pathText);

        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth");
        other.Children.Add(Field("配置文件夹", Btn("打开", 70, () => OpenFolder(configDir))));
        PageHost.Children.Add(Card("路径", other));

        // 微软登录
        var loginStack = new StackPanel();
        var msBox = new TextBox
        {
            Text = AppSettings.Get("msClientId", ""),
            FontSize = 10.5,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        msBox.TextChanged += (_, _) => AppSettings.Set("msClientId", msBox.Text.Trim());
        var msRow = new StackPanel { Orientation = Orientation.Horizontal };
        var msLabel = Text("客户端 ID", 10.5, "#B9C9DC");
        msLabel.VerticalAlignment = VerticalAlignment.Center;
        msLabel.Margin = new Thickness(0, 0, 10, 0);
        msRow.Children.Add(msLabel);
        msRow.Children.Add(msBox);
        loginStack.Children.Add(msRow);
        loginStack.Children.Add(Text("Azure 应用注册里的「应用程序(客户端) ID」，正版登录要用", 9.5, "#6B7C92"));
        PageHost.Children.Add(Card("微软登录", loginStack));

        var about = new StackPanel();
        about.Children.Add(Text("Hearth " + AppSettings.Version, 12, "#E8F0FA", true));
        about.Children.Add(Text("炉火", 10, "#8FA0B8"));
        about.Children.Add(new Border { Height = 8 });
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        links.Children.Add(Btn("打开日志文件夹", 130, () => OpenFolder(Path.GetTempPath())));
        about.Children.Add(links);
        PageHost.Children.Add(Card("关于", about));
    }
}
