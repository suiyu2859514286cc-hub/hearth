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
        foreach (var b in new[] { NavLaunch, NavUI, NavNet, NavOther })
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
            case "net": ShowNet(); break;
            case "other": ShowOther(); break;
            default: ShowLaunch(); break;
        }
    }

    public void SelectPage(string page) => ShowPage(page);

    // ===== 联机（樱花内网穿透）=====

    private PortForwarder? _forwarder;
    private Action? _netRefresh;

    private static TextBox InputBox(string text)
        => new()
        {
            Text = text,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 11
        };

    private void ShowNet()
    {
        var owner = Window.GetWindow(this);

        var tokenBox = InputBox(AppSettings.Get("natfrpToken"));
        var tunnelBox = InputBox(AppSettings.Get("natfrpTunnel"));
        var addrBox = InputBox(AppSettings.Get("natfrpAddress"));
        var localBox = InputBox(AppSettings.Get("natfrpLocalPort", "25565"));
        localBox.Width = 90;

        var status = Text("未启动", 11, "#8FA8C4");
        var logText = Text("", 9, "#6B7C92");
        logText.MaxHeight = 110;

        void Refresh()
        {
            var running = NatFrpService.IsRunning;
            status.Text = running ? "frpc 正在运行" : "未启动";
            status.Foreground = new SolidColorBrush(running
                ? Color.FromRgb(0x4A, 0xDE, 0x80)
                : Color.FromRgb(0x8F, 0xA8, 0xC4));

            var log = NatFrpService.Log;
            if (log.Length > 1400) log = "…" + log[^1400..];
            logText.Text = log.Trim();
        }

        if (_netRefresh != null) NatFrpService.Changed -= _netRefresh;
        _netRefresh = Refresh;
        NatFrpService.Changed += _netRefresh;

        void Save()
        {
            AppSettings.Set("natfrpToken", tokenBox.Text.Trim());
            AppSettings.Set("natfrpTunnel", tunnelBox.Text.Trim());
            AppSettings.Set("natfrpAddress", addrBox.Text.Trim());
            AppSettings.Set("natfrpLocalPort", localBox.Text.Trim());
        }

        async void StartNet()
        {
            Save();
            var token = tokenBox.Text.Trim();
            var tunnel = tunnelBox.Text.Trim();
            if (token == "" || tunnel == "")
            {
                DarkDialog.Toast(owner, "先填访问密钥和隧道 ID");
                return;
            }

            try
            {
                if (!File.Exists(NatFrpService.FrpcPath))
                {
                    if (!DarkDialog.Confirm(owner, "下载 frpc",
                            "第一次用要先下载樱花的 frpc（约 10 MB），现在下？", "下载", "取消"))
                        return;

                    status.Text = "正在下载 frpc…";
                    await NatFrpService.EnsureFrpcAsync(
                        new Progress<string>(m => status.Text = m));
                }

                var gamePort = MinecraftLanLog.FindRecentPort(DateTime.Now);
                if (gamePort == null)
                {
                    DarkDialog.Info(owner, "没找到局域网端口",
                        "先回游戏：ESC → 对局域网开放 → 创造局域网世界。\n" +
                        "聊天框出现「本地游戏已在端口 xxxxx 上开启」之后，再回来点开始。");
                    return;
                }

                var local = int.TryParse(localBox.Text.Trim(), out var lp) && lp > 0 ? lp : 25565;
                _forwarder?.Dispose();
                _forwarder = new PortForwarder(local, gamePort.Value);
                try { _forwarder.Start(); }
                catch (Exception ex)
                {
                    _forwarder = null;
                    DarkDialog.Info(owner, "本地端口被占用",
                        "端口 " + local + " 起不来：" + ex.Message + "\n换个本地端口再试。");
                    return;
                }

                NatFrpService.Start(token, tunnel);
                status.Text = "已启动：游戏端口 " + gamePort + " → 本地 " + local;
            }
            catch (Exception ex)
            {
                DarkDialog.Info(owner, "启动失败", ex.Message);
            }
        }

        void StopNet()
        {
            NatFrpService.Stop();
            _forwarder?.Dispose();
            _forwarder = null;
            status.Text = "已停止";
        }

        void CopyAddress()
        {
            var addr = addrBox.Text.Trim();
            if (addr == "")
            {
                DarkDialog.Toast(owner, "先填连接地址");
                return;
            }

            try
            {
                Clipboard.SetText(addr);
                DarkDialog.Toast(owner, "连接地址已复制");
            }
            catch { }
        }

        PageHost.Children.Clear();

        var intro = new StackPanel();
        intro.Children.Add(Text("用你自己的樱花账号，把「对局域网开放」的世界映射到公网。", 10.5, "#B9C9DC"));
        intro.Children.Add(Text("启动器只负责拉起隧道，不代注册、不共享账号；访问密钥只存在本机。", 9.5, "#6B7C92"));
        PageHost.Children.Add(Card("樱花联机（实验）", intro));

        var form = new StackPanel();
        form.Children.Add(Field("访问密钥", tokenBox));
        form.Children.Add(Field("隧道 ID", tunnelBox));
        form.Children.Add(Field("连接地址", addrBox));
        form.Children.Add(Field("本地端口", localBox));
        form.Children.Add(Text("连接地址在樱花面板的隧道详情里，形如 cn-xx.natfrp.cloud:12345；本地端口默认 25565，跟隧道里填的一致就行。", 9, "#6B7C92"));
        PageHost.Children.Add(Card("隧道", form));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 10) };
        actions.Children.Add(Btn("保存", 70, Save));
        actions.Children.Add(Btn("开始联机", 90, StartNet));
        actions.Children.Add(Btn("停止", 70, StopNet));
        actions.Children.Add(Btn("复制地址", 90, CopyAddress));

        var opPanel = new StackPanel();
        opPanel.Children.Add(actions);
        opPanel.Children.Add(status);
        opPanel.Children.Add(logText);
        PageHost.Children.Add(Card("操作", opPanel));

        var help = new StackPanel();
        help.Children.Add(Text("1. 樱花面板建一条 TCP 隧道，本地端口填 25565", 9.5, "#B9C9DC"));
        help.Children.Add(Text("2. 第一次点「开始联机」会提示下载 frpc", 9.5, "#B9C9DC"));
        help.Children.Add(Text("3. 进游戏 → 对局域网开放 → 回来点「开始联机」", 9.5, "#B9C9DC"));
        help.Children.Add(Text("4. 把连接地址发给朋友，他们在游戏里「直接连接」粘贴", 9.5, "#B9C9DC"));
        help.Children.Add(Text("免费额度：10 Mbps / 2 条隧道 / 每月 5 GiB；需要实名。朋友没正版的话，要装 LAN World Plug-n-Play 这类联机 mod。", 9, "#6B7C92"));

        var panelBtn = Btn("打开樱花面板", 120, () =>
        {
            try { Process.Start(new ProcessStartInfo("https://www.natfrp.com/user/") { UseShellExecute = true }); }
            catch { }
        });
        panelBtn.Margin = new Thickness(0, 8, 0, 0);
        help.Children.Add(panelBtn);
        PageHost.Children.Add(Card("怎么用", help));

        Refresh();
    }

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

        // 未解锁时颜色完全不可见，只留一个空格子；解锁后才露出来
        stack.Children.Add(unlocked
            ? new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(7),
                Background = theme.Id == "rainbow"
                    ? ThemeService.RainbowBrush()
                    : new SolidColorBrush(color),
                Margin = new Thickness(0, 0, 0, 8)
            }
            : new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(7),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 8)
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

        // 只有已解锁的才给提示，内容是这个主题怎么来的；未解锁的不弹任何东西
        if (unlocked) card.ToolTip = ThemeService.Hint(theme);

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

    private async void CheckUpdateNow()
    {
        var win = Window.GetWindow(this);
        var latest = await UpdateService.GetLatestAsync();

        if (latest == null)
        {
            DarkDialog.Info(win, "检查更新",
                "没能拿到更新信息，可能是网络问题。" +
                Environment.NewLine + Environment.NewLine +
                "GitHub 有时候会比较慢，过一会儿再试。");
            return;
        }

        if (!UpdateService.IsNewer(latest.Tag, AppSettings.Version))
        {
            DarkDialog.Info(win, "检查更新",
                "已经是最新版本了（" + AppSettings.Version + "）。");
            return;
        }

        var notes = latest.Notes ?? "";
        if (notes.Length > 500) notes = notes[..500] + "…";

        DarkDialog.Info(win, "发现新版本 " + latest.Tag.TrimStart('v', 'V'),
            "当前版本：" + AppSettings.Version +
            Environment.NewLine + Environment.NewLine + notes);
    }

    private void ExportSettings()
    {
        var src = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "settings.json");

        if (!File.Exists(src))
        {
            DarkDialog.Info(Window.GetWindow(this), "导出设置", "还没有可导出的设置。");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出设置",
            FileName = "hearth-settings.json",
            Filter = "设置文件 (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.Copy(src, dialog.FileName, overwrite: true);
            DarkDialog.Toast(Window.GetWindow(this), "设置已导出");
        }
        catch (Exception ex)
        {
            DarkDialog.Info(Window.GetWindow(this), "导出失败", ex.Message);
        }
    }

    private void ImportSettings()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入设置",
            Filter = "设置文件 (*.json)|*.json"
        };
        if (dialog.ShowDialog() != true) return;

        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "settings.json");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(dialog.FileName, dest, overwrite: true);
            DarkDialog.Info(Window.GetWindow(this), "设置已导入",
                "部分设置（比如主题、下载源）重启启动器后生效。");
        }
        catch (Exception ex)
        {
            DarkDialog.Info(Window.GetWindow(this), "导入失败", ex.Message);
        }
    }

    private void ShowOther()
    {
        // 启动器：更新 / 公告 / 缓存 / 数据收集
        var launcher = new StackPanel();

        launcher.Children.Add(Field("启动器更新", UiKit.ChoiceGroup(
            new[] { "自动检查", "关闭更新提示" },
            AppSettings.UpdateMode == "off" ? 1 : 0,
            idx => AppSettings.Set("updateMode", idx == 1 ? "off" : "auto"))));

        launcher.Children.Add(Field("启动器公告", UiKit.ChoiceGroup(
            new[] { "显示所有公告", "不显示公告" },
            AppSettings.Announcements == "off" ? 1 : 0,
            idx => AppSettings.Set("announcements", idx == 1 ? "off" : "on"))));

        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "cache");
        launcher.Children.Add(Field("缓存文件夹", Btn("打开", 70, () =>
        {
            Directory.CreateDirectory(cacheDir);
            OpenFolder(cacheDir);
        })));

        var telemetry = new CheckBox
        {
            Content = "允许匿名数据收集",
            IsChecked = AppSettings.Telemetry != "off",
            Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0)),
            FontSize = 10.5,
            Margin = new Thickness(0, 4, 0, 0)
        };
        telemetry.Checked += (_, _) => AppSettings.Set("telemetry", "on");
        telemetry.Unchecked += (_, _) => AppSettings.Set("telemetry", "off");
        launcher.Children.Add(telemetry);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(Btn("检查更新", 100, CheckUpdateNow));
        var exportBtn = Btn("导出设置", 100, ExportSettings);
        exportBtn.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(exportBtn);
        var importBtn = Btn("导入设置", 100, ImportSettings);
        importBtn.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(importBtn);
        launcher.Children.Add(buttons);

        PageHost.Children.Add(Card("启动器", launcher));

        // 皮肤站：离线账号靠它显示皮肤
        var skin = new StackPanel();
        var urlBox = new TextBox
        {
            Text = AuthlibService.ServerUrl,
            FontSize = 10.5,
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            Width = 340,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        urlBox.TextChanged += (_, _) => AppSettings.Set("authlibUrl", urlBox.Text.Trim());

        var urlRow = new StackPanel { Orientation = Orientation.Horizontal };
        var urlLabel = Text("皮肤站地址", 10.5, "#B9C9DC");
        urlLabel.VerticalAlignment = VerticalAlignment.Center;
        urlLabel.Margin = new Thickness(0, 0, 10, 0);
        urlRow.Children.Add(urlLabel);
        urlRow.Children.Add(urlBox);
        skin.Children.Add(urlRow);

        skin.Children.Add(Text("填第三方皮肤站的地址，比如 https://littleskin.cn/api/yggdrasil\n" +
                               "填了之后启动游戏会自动挂载 authlib-injector，离线账号也能显示皮肤站的皮肤",
            9.5, "#6B7C92"));

        PageHost.Children.Add(Card("皮肤站（离线换皮肤）", skin));

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
        // 客户端 ID 默认藏起来：普通用户不需要看，也不该改
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

        var advancedBody = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
        advancedBody.Children.Add(msRow);
        advancedBody.Children.Add(Text("只有自己注册了 Azure 应用才需要改，留空就用内置的", 9.5, "#6B7C92"));

        var advancedToggle = new CheckBox
        {
            Content = "高级选项",
            IsChecked = false,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDC)),
            FontSize = 10
        };
        advancedToggle.Checked += (_, _) => advancedBody.Visibility = Visibility.Visible;
        advancedToggle.Unchecked += (_, _) => advancedBody.Visibility = Visibility.Collapsed;

        loginStack.Children.Add(advancedToggle);
        loginStack.Children.Add(advancedBody);
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
