using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BlockLauncher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        GameFolderService.Load();
        UpdateFolderLabel();
        GameFolderService.Changed += () =>
        {
            UpdateFolderLabel();
            LoadInstalled();
        };
        LoadInstalled();
        LoadDownloadable();
        BuildMods();
        BuildHistory();
        PrepareSplash();

        Loaded += (_, _) =>
        {
            AcrylicHelper.Enable(this, 0xCC0B0F16);
            DwmHelper.Setup(this);
            NavPanel.Visibility = Visibility.Collapsed;
            NavColumn.Width = new GridLength(0);
            DetailColumn.Width = new GridLength(0);
            // 强制从首页开始
            HomeView.Visibility = Visibility.Visible;
            VersionsView.Visibility = Visibility.Collapsed;
            SetDailyQuote();
            ApplySavedLayout();
            ApplyTheme();
            ApplyTitleBar();
            ApplyAccount();

            if (StatusBarText != null)
                StatusBarText.Text = (AppSettings.DownloadSource == "official" ? "下载源：官方" : "下载源：镜像")
                                     + "   ·   v" + AppSettings.Version;

            // 界面画完了，让启动画面裂开
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(420);
                FinishSplash();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            // 后台查一下有没有新版本，不挡界面
            _ = CheckUpdateAsync();
            // 调试/快捷方式用：--versions 进版本选择，--download 进下载页
            if (Environment.GetCommandLineArgs().Contains("--versions"))
                NavVersions_Click(this, new RoutedEventArgs());
            if (Environment.GetCommandLineArgs().Contains("--download"))
                NavDownload_Click(this, new RoutedEventArgs());

            // 调试用：--settings=版本id 直接打开版本设置
            var settingsArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase));
            if (settingsArg != null)
            {
                var sid = settingsArg["--settings=".Length..];
                var pageArg = Environment.GetCommandLineArgs()
                    .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
                var page = pageArg?["--page=".Length..] ?? "overview";
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var win = new VersionSettingsWindow(sid, page);
                    win.ShowDialog();
                }));
            }

            // 调试用：--folder=路径 添加并切换游戏文件夹
            var folderArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--folder=", StringComparison.OrdinalIgnoreCase));
            if (folderArg != null)
                Dispatcher.BeginInvoke(new Action(() => GameFolderService.Add(folderArg["--folder=".Length..])));

            // 调试用：--dl=关键词 直接打开某个资源的下载窗口
            var dlArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--dl=", StringComparison.OrdinalIgnoreCase));
            if (dlArg != null)
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    var hits = await ModrinthService.SearchAsync(dlArg["--dl=".Length..], null, "mod", 5);
                    if (hits.Count > 0) OpenResourceDownload(hits[0], null);
                }));

            // 调试用：--prefs 直接打开设置
            var prefsArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--prefs", StringComparison.OrdinalIgnoreCase));
            if (prefsArg != null)
            {
                var prefsPage = prefsArg.Contains('=') ? prefsArg[(prefsArg.IndexOf('=') + 1)..] : "launch";
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NavSettings_Click(this, new RoutedEventArgs());
                    SettingsView.SelectPage(prefsPage);
                }));
            }

            // 调试用：--resource 直接打开资源中心
            if (Environment.GetCommandLineArgs().Contains("--resource"))
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NavDownload_Click(this, new RoutedEventArgs());
                    ShowDownloadTab("resources");
                }));

            // 调试用：--float 显示下载悬浮窗（假进度，看样式）
            if (Environment.GetCommandLineArgs().Contains("--float"))
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ShowDownloadBall("安装 1.21.1");
                    UpdateDownloadBall("libraries", 240, 240, 45, "正在下载库文件…");
                    UpdateDownloadBall("assets", 3850, 3890, 76.4, "正在下载资源…");

                    if (Environment.GetCommandLineArgs().Contains("--detail"))
                    {
                        var d = new DownloadDetailWindow(this) { Owner = this };
                        d.ShowDialog();
                    }
                }));

            // 调试用：--capes 直接打开披风选择（看预览）
            if (Environment.GetCommandLineArgs().Contains("--capes"))
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    var acc = MicrosoftAuth.Load();
                    if (acc == null) return;
                    var (capes, _) = await SkinService.GetCapesAsync(acc.AccessToken);
                    new CapePickerWindow(capes).ShowDialog();
                }));

            // 调试用：--account 直接打开账户窗口
            if (Environment.GetCommandLineArgs().Contains("--account"))
                Dispatcher.BeginInvoke(new Action(() => new AccountWindow { Owner = this }.ShowDialog()));

            // 调试用：--login 直接打开登录窗口
            if (Environment.GetCommandLineArgs().Contains("--login"))
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var lw = new LoginWindow { Owner = this };
                    if (lw.ShowDialog() == true)
                    {
                        SetAccountUI(lw.AccountName);
                        DarkDialog.Toast(this, "登录成功：" + lw.AccountName);
                    }
                }));

            // 调试用：--help 打开反馈窗口
            if (Environment.GetCommandLineArgs().Contains("--help"))
                Dispatcher.BeginInvoke(new Action(() => new FeedbackWindow().ShowDialog()));

            // 调试用：--themes 把主题解锁状态写到临时文件
            if (Environment.GetCommandLineArgs().Contains("--themes"))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var t in ThemeService.Themes)
                        sb.AppendLine(t.Name.PadRight(6) + " " + t.Id.PadRight(12) +
                                      " unlocked=" + ThemeService.IsUnlocked(t));
                    sb.AppendLine("---");
                    sb.AppendLine("launchCount = " + ThemeService.LaunchCount);
                    sb.AppendLine("versions    = " + ThemeService.InstalledVersionCount());
                    sb.AppendLine("loaders     = " + ThemeService.LoaderKinds());
                    sb.AppendLine("folders     = " + GameFolderService.Folders.Count);
                    sb.AppendLine("premium     = " + ThemeService.IsPremium);
                    sb.AppendLine("nightOwl    = " + AppSettings.GetBool("nightOwl", false));
                    System.IO.File.WriteAllText(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "theme-check.txt"), sb.ToString());
                    Application.Current.Shutdown();
                }));
            }

            // 调试用：--themehint 弹主题解锁提示
            if (Environment.GetCommandLineArgs().Contains("--themehint"))
                Dispatcher.BeginInvoke(new Action(() => DarkDialog.Info(this,
                    "「潮汐」还没解锁", ThemeService.Hint(ThemeService.Themes[1]))));

            // 调试用：--confirm 弹一个深色确认框
            if (Environment.GetCommandLineArgs().Contains("--confirm"))
                Dispatcher.BeginInvoke(new Action(() =>
                    DarkDialog.Confirm(this, "删除版本", "确定删除版本 1.20.1 吗？只删这个版本的文件夹。", "删除", "取消", true)));

            // 调试用：--toast=文字 弹一条深色提示
            var toastArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--toast=", StringComparison.OrdinalIgnoreCase));
            if (toastArg != null)
                Dispatcher.BeginInvoke(new Action(() => DarkDialog.Toast(this, toastArg["--toast=".Length..])));

            // 调试用：--import 或 --import=路径 打开导入整合包窗口
            var importArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--import", StringComparison.OrdinalIgnoreCase));
            if (importArg != null)
            {
                var importPath = importArg.Contains('=')
                    ? importArg[(importArg.IndexOf('=') + 1)..]
                    : null;
                Dispatcher.BeginInvoke(new Action(() => OpenImport(importPath)));
            }

            // 调试用：--install=1.20.1 直接打开安装窗口
            var installArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("--install=", StringComparison.OrdinalIgnoreCase));
            if (installArg != null)
            {
                var id = installArg["--install=".Length..];
                var pick = Environment.GetCommandLineArgs()
                    .FirstOrDefault(a => a.StartsWith("--pick=", StringComparison.OrdinalIgnoreCase))?["--pick=".Length..];
                Dispatcher.BeginInvoke(new Action(() => OpenInstall(id, "自动测试", pick)));
            }
            StartBreathing();
            StartFadeIn();
        };
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleEditMode();
                e.Handled = true;
            }
        };
    }

    // ===== 日志 =====
    private static void Log(string msg)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hearth.log"),
                $"{DateTime.Now:HH:mm:ss} {msg}\n");
        }
        catch { }
    }

    // ===== 你知道吗 =====
    private static readonly string[] Tips =
    {
        "苦力怕原本是猪的模型做坏了的产物，Notch 觉得效果不错就留下来了。",
        "末影人（Enderman）的设计灵感来自恐怖游戏里的「瘦长鬼影」（Slender Man）。",
        "用钻石镐挖一块黑曜石需要 9.4 秒，用下界合金镐要 8.35 秒。",
        "下界合金装备掉进熔岩里不会被烧毁，会被弹出来。",
        "Minecraft 最早只有 32 种方块，现在有 800 多种。",
        "鸡下蛋的时候不需要附近的公鸡。",
        "信标（Beacon）的光束垂直向上可以照亮 384 格。",
        "末影珍珠传送会扣 5 点生命值（2.5 颗心）。",
        "在 1.9 版本之后，剑有冷却时间，连续点击反而伤害更低。",
        "潜影贝的子弹打中同类会分裂出新的潜影贝。",
        "铁傀儡会给村庄里的小孩送花。",
        "猫可以吓退苦力怕，也能把幻翼吓跑。",
        "用剪刀给羊剪毛，羊毛会掉一地；用染料还能给羊染色。",
        "下界传送门用黑曜石 10 块就能搭成，最小尺寸是 4×5。",
        "信标可以用铁块、金块、钻石块、绿宝石块或下界合金块激活。",
        "僵尸村名可以被治愈成正常村民，前提是它没被杀死。",
        "海绵能吸走附近的水，但湿海绵要放进熔炉烘干才能继续用。",
        "末地城的潜影贝会在被杀死后掉落潜影壳。",
        "按 F3 可以打开调试界面，看到坐标、区块和生物群系。",
        "在 1.13 之后，水里的物品会慢慢飘向同一个地方。",
    };

    private int _tipIndex = -1;

    private void SetDailyQuote()
    {
        if (QuoteText == null) return;
        if (_tipIndex < 0)
            _tipIndex = (int)(DateTime.Today.DayOfYear + DateTime.Today.Year) % Tips.Length;
        QuoteText.Text = Tips[_tipIndex];
    }

    private void QuoteBox_Click(object sender, MouseButtonEventArgs e)
    {
        if (_editMode) return;   // 编辑模式下点击是拖拽，不切换
        if (Tips.Length == 0) return;
        // 换一条，不跟当前重复
        int old = _tipIndex;
        int next = old;
        var rnd = new Random();
        for (int i = 0; i < 8 && next == old; i++) next = rnd.Next(Tips.Length);
        _tipIndex = next;
        QuoteText.Text = Tips[_tipIndex];
    }

    // ===== 模组轮换 =====
    private List<ModrinthService.ModInfo> _allMods = new();
    private int _modBatch = 0;
    private const int BatchSize = 6;
    private System.Windows.Threading.DispatcherTimer? _modTimer;

    private List<ModrinthService.ModInfo> CurrentBatch()
    {
        if (_allMods.Count <= BatchSize) return _allMods;
        int total = (_allMods.Count + BatchSize - 1) / BatchSize;
        int start = (_modBatch % total) * BatchSize;
        return _allMods.Skip(start).Take(BatchSize).ToList();
    }

    private void StartModRotation()
    {
        _modTimer?.Stop();
        _modTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(2)
        };
        _modTimer.Tick += async (_, _) =>
        {
            if (_allMods.Count <= BatchSize) return;
            _modBatch++;
            await FadeSwapMods();
        };
        _modTimer.Start();
    }

    /// <summary>淡出 → 换内容 → 淡入</summary>
    private async Task FadeSwapMods()
    {
        if (ModGrid == null) return;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        ModGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(230);

        RenderRealMods(CurrentBatch());

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        ModGrid.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    // ===== 呼吸光效 =====
    private void StartBreathing()
    {
        try
        {
            var glow = GoBox?.FindName("GoGlow") as System.Windows.Media.Effects.DropShadowEffect;
            if (glow == null) return;
            var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
            var anim = new DoubleAnimation(0.25, 0.75, TimeSpan.FromMilliseconds(1600))
            { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(anim, glow);
            Storyboard.SetTargetProperty(anim, new PropertyPath("Opacity"));
            sb.Children.Add(anim);
            sb.Begin();
        }
        catch (Exception ex) { Log("呼吸光效失败: " + ex.Message); }
    }

    // ===== 启动淡入 =====
    private void StartFadeIn()
    {
        try
        {
            Opacity = 0;
            var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            BeginAnimation(OpacityProperty, anim);
        }
        catch { }
    }

    // ===== 版本列表 =====
    private List<VersionService.VersionInfo> _allVersions = new();
    private const int PerGroup = 40;

    private void RenderVersions()
    {
        if (DownloadList == null) return;
        DownloadList.Children.Clear();

        var groups = _allVersions
            .GroupBy(v => v.TypeName)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Date).ToList());

        // PCL 的置顶「最新版本」：最新正式版，若预览版更新则再带一个
        var pinned = new List<VersionService.VersionInfo>();
        groups.TryGetValue("正式版", out var releases);
        groups.TryGetValue("预览版", out var previews);
        if (releases is { Count: > 0 }) pinned.Add(releases[0]);
        if (previews is { Count: > 0 } && (releases is not { Count: > 0 } || previews[0].Date > releases[0].Date))
            pinned.Add(previews[0]);

        if (pinned.Count > 0)
        {
            AddGroup("最新版本", pinned.Count, true, pinned, false);
        }

        // 其余按 PCL 的四类分组
        var summary = new List<string>();
        foreach (var name in new[] { "正式版", "预览版", "远古版", "愚人节版" })
        {
            if (!groups.TryGetValue(name, out var list) || list.Count == 0) continue;
            summary.Add(name + "=" + list.Count);
            // 正式版默认展开，其他三类像 PCL 那样先收着，点标题再展开
            AddGroup(name, list.Count, false, list, name != "正式版");
        }
        Log("版本分组: " + string.Join(", ", summary));

        if (DownloadCount != null)
            DownloadCount.Text = $"共 {_allVersions.Count} 个版本";
    }

    private void OpenInstall(string versionId, string sub, string? autoPick = null)
    {
        var win = new InstallWindow(versionId, sub, autoPick) { Owner = this };
        win.ShowDialog();
        if (!win.Installed) return;

        // 装完把「版本选择」的列表刷新一遍，下次进去就能看到
        LoadInstalled();
    }

    private static (string Name, string Sub, string Loader, string Java, string Mem, string State, string Ago, bool Ready) RowOf(VersionService.VersionInfo v)
        => (v.Id, v.Sub, "原版", v.Java, "4096",
            v.Installed ? "已安装" : "未下载",
            v.Installed ? "本机" : "—",
            v.Installed);

    /// <summary>一组版本：标题可点击折叠，行按 PCL 的分类排在一起</summary>
    private void AddGroup(string title, int count, bool pinned, List<VersionService.VersionInfo> list, bool collapsed)
    {
        var rows = new List<UIElement>();
        var start = DownloadList.Children.Count;

        foreach (var v in list.Take(PerGroup))
        {
            var row = BuildRow(RowOf(v), () => OpenInstall(v.Id, v.Sub));
            rows.Add(row);
            DownloadList.Children.Add(row);
        }
        if (list.Count > PerGroup)
        {
            var more = new TextBlock
            {
                Text = $"… 还有 {list.Count - PerGroup} 个，先显示最近 {PerGroup} 个",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x73, 0x8A)),
                Margin = new Thickness(4, 2, 0, 6)
            };
            rows.Add(more);
            DownloadList.Children.Add(more);
        }

        if (collapsed)
            foreach (var r in rows) r.Visibility = Visibility.Collapsed;

        DownloadList.Children.Insert(start, BuildGroupHeader(title, count, pinned, rows, collapsed));
    }

    private Border BuildGroupHeader(string title, int count, bool pinned, List<UIElement> rows, bool collapsed)
    {
        var arrow = new TextBlock
        {
            Text = collapsed ? "▸" : "▾",
            FontSize = 9,
            Margin = new Thickness(0, 1, 6, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x73, 0x8A))
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(arrow);
        sp.Children.Add(new TextBlock
        {
            Text = pinned ? "★ " + title : title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = pinned
                ? new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF))
                : new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0))
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{count} 个",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x73, 0x8A)),
            Margin = new Thickness(8, 2, 0, 0)
        });

        var header = new Border
        {
            Margin = new Thickness(2, 14, 0, 7),
            Padding = new Thickness(0, 3, 0, 3),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = sp
        };

        header.MouseLeftButtonUp += (_, _) =>
        {
            if (rows.Count == 0) return;
            bool hide = rows[0].Visibility == Visibility.Visible;
            foreach (var r in rows) r.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            arrow.Text = hide ? "▸" : "▾";
        };

        return header;
    }

    /// <summary>版本选择页：只列本机 .minecraft 里已经装好的版本</summary>
    private void LoadInstalled()
    {
        if (InstalledList == null) return;
        InstalledList.Children.Clear();

        var items = new List<(string Id, DateTime When)>();
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions");
            if (Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir))
                {
                    var id = Path.GetFileName(d);
                    if (!File.Exists(Path.Combine(d, id + ".json"))) continue;
                    items.Add((id, Directory.GetLastWriteTime(d)));
                }
            }
        }
        catch (Exception ex) { Log("读取已安装版本失败: " + ex.Message); }

        items = items.OrderByDescending(x => x.When).ToList();

        // 像 PCL 那样分两组：装过 Mod 的算整合包，其余算常规版本
        var packs = new List<(string Id, DateTime When)>();
        var regular = new List<(string Id, DateTime When)>();
        foreach (var it in items)
        {
            var modsDir = Path.Combine(VersionService.McDir, "versions", it.Id, "mods");
            var hasMods = false;
            try
            {
                hasMods = Directory.Exists(modsDir) &&
                          Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch { }

            (hasMods ? packs : regular).Add(it);
        }

        void AddGroup(string title, List<(string Id, DateTime When)> list)
        {
            if (list.Count == 0) return;

            var head = new TextBlock
            {
                Text = title + "（" + list.Count + "）",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                Margin = new Thickness(2, list == packs ? 0 : 14, 0, 6)
            };
            InstalledList.Children.Add(head);

            foreach (var (id, when) in list)
            {
                var (java, loader) = DescribeVersion(id);
                InstalledList.Children.Add(BuildRow((
                    id,
                    "已安装 · " + when.ToString("yyyy-MM-dd"),
                    loader,
                    java,
                    "4096",
                    "已安装",
                    "本机",
                    true)));
            }
        }

        AddGroup("整合包", packs);
        AddGroup("常规版本", regular);

        if (VersionCount != null)
            VersionCount.Text = items.Count > 0 ? $"{items.Count} 个已安装版本" : "";
        if (EmptyInstalled != null)
            EmptyInstalled.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>加载器矢量图标：自己画，不受字体影响，也不会被裁</summary>
    private static FrameworkElement BuildLoaderGlyph(string loader)
    {
        // 用 24×24 的坐标系画，再整体缩放到图标框里
        var stroke = new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
        var path = new System.Windows.Shapes.Path
        {
            Stroke = stroke,
            StrokeThickness = 1.7,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent
        };

        // 所有图形都画在 4–20 这个范围里（24×24 视框，四周留 4 个单位给描边和留白）
        path.Data = Geometry.Parse(loader switch
        {
            // Forge / 旧版加载器：铁砧（顶面宽、腰细、底座宽）
            "Forge" or "旧版加载器" => "M4,6 L20,6 L20,9 L16,9 L16,13 L18.5,15.5 L18.5,19 L5.5,19 L5.5,15.5 L8,13 L8,9 L4,9 Z",

            // Fabric / Quilt：编织网（横竖交错）
            "Fabric" or "Quilt" => "M5,7 L19,7 M5,12 L19,12 M5,17 L19,17 M7,5 L7,19 M12,5 L12,19 M17,5 L17,19",

            // NeoForge / 分不清的现代加载器：双层菱形
            "NeoForge" or "Forge / NeoForge" => "M12,3.5 L20.5,12 L12,20.5 L3.5,12 Z M12,7.5 L16.5,12 L12,16.5 L7.5,12 Z",

            // OptiFine：放大镜
            "OptiFine" => "M11,4.5 A6.5,6.5 0 1 1 11,17.5 A6.5,6.5 0 1 1 11,4.5 Z M15.8,15.8 L20.5,20.5",

            // 原版：等距立方体
            _ => "M12,3.5 L20.5,8 L20.5,16 L12,20.5 L3.5,16 L3.5,8 Z M12,3.5 L12,12 M12,12 L20.5,8 M12,12 L3.5,8"
        });

        // 不用 Viewbox：它按「含描边的实际边界」缩放，会让图形偏移、边缘被裁。
        // 直接给 Path 固定尺寸 + 居中，几何坐标是 24×24，留 2 个单位给描边
        path.Stretch = Stretch.Uniform;
        path.Width = 22;
        path.Height = 22;
        path.HorizontalAlignment = HorizontalAlignment.Center;
        path.VerticalAlignment = VerticalAlignment.Center;
        return path;
    }

    private static readonly Dictionary<string, BitmapImage?> PackIconCache = new();

    /// <summary>找整合包自带图标：icon.png / pack.png / logo.png</summary>
    private static BitmapImage? FindPackIcon(string versionId)
    {
        if (PackIconCache.TryGetValue(versionId, out var cached)) return cached;

        BitmapImage? result = null;
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions", versionId);

            // 也认 PCL 存的图标（versions<id>PCLLogo.png），这样从 PCL 搬过来的整合包能保留封面
            var candidates = new[]
            {
                "icon.png", "pack.png", "logo.png",
                "PCL/Logo.png", "PCL/logo.png", "PCL/Icon.png"
            };

            foreach (var name in candidates)
            {
                var path = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) continue;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                result = bmp;
                break;
            }
        }
        catch { }

        PackIconCache[versionId] = result;
        return result;
    }

    /// <summary>读版本 JSON，拿到真实的 Java 版本和加载器名（含 inheritsFrom 链）</summary>
    private static (string Java, string Loader) DescribeVersion(string id)
        => VersionDescribe.Describe(VersionService.McDir, id);

    private async void LoadDownloadable()
    {
        if (DownloadList == null) return;
        DownloadList.Children.Clear();
        if (DownloadCount != null) DownloadCount.Text = "加载中…";

        try
        {
            var list = await VersionService.GetVersionsAsync();
            if (list.Count > 0)
            {
                _allVersions = list;
                RenderVersions();
                return;
            }
        }
        catch (Exception ex) { Log("版本列表加载失败: " + ex.Message); }

        // 网络彻底不通时的兜底
        var demo = new (string Name, string Sub, string Loader, string Java, string Mem, string State, string Ago, bool Ready)[]
        {
            ("1.21.4",  "正式版 · 离线",  "原版", "21", "4096", "未下载", "—", false),
            ("1.20.1",  "正式版 · 离线",  "原版", "17", "4096", "未下载", "—", false),
            ("1.12.2",  "正式版 · 离线",  "原版", "8",  "3072", "未下载", "—", false),
        };
        DownloadList.Children.Clear();
        foreach (var d in demo) DownloadList.Children.Add(BuildRow(d));
        if (DownloadCount != null) DownloadCount.Text = "离线（网络不通）";
    }

    private Border BuildRow((string Name, string Sub, string Loader, string Java, string Mem, string State, string Ago, bool Ready) d, Action? onClick = null)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0D, 0x14, 0x1D)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 7),
            Cursor = Cursors.Hand
        };
        var g = new Grid();
        // 图标列用 Auto：跟着图标框实际宽度走，写死数字会把右边裁掉
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 图标优先级：版本自带的 icon.png（整合包）→ 加载器矢量图
        var (iconFrom, iconTo, iconEdge) = d.Loader switch
        {
            "Forge" => (Color.FromRgb(0x4A, 0x24, 0x0E), Color.FromRgb(0x7C, 0x3A, 0x12), Color.FromRgb(0xEA, 0x58, 0x0C)),
            "NeoForge" => (Color.FromRgb(0x11, 0x3A, 0x3A), Color.FromRgb(0x0E, 0x5A, 0x62), Color.FromRgb(0x06, 0xB6, 0xD4)),
            "Fabric" => (Color.FromRgb(0x2A, 0x1F, 0x4A), Color.FromRgb(0x3D, 0x2A, 0x6B), Color.FromRgb(0x8B, 0x5C, 0xF6)),
            "OptiFine" => (Color.FromRgb(0x2A, 0x2A, 0x0E), Color.FromRgb(0x4A, 0x4A, 0x12), Color.FromRgb(0xE0, 0xC0, 0x40)),
            _ => (Color.FromRgb(0x1F, 0x3A, 0x2A), Color.FromRgb(0x28, 0x52, 0x3A), Color.FromRgb(0x4A, 0xDE, 0x80))
        };

        // 两层叠起来：底层是「背景 + 边框」，上层是内容（内缩 2px，绝不碰到边框）
        var iconHost = new Grid { Width = 40, Height = 40 };

        // 边框用 Path 自己画（圆角矩形），不用 Border 的 BorderBrush——
        // 1px 的 Border 在 150% 缩放下，圆角处会被抗锯齿吃掉，看着就缺一条
        var frame = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M9,0.75 L31,0.75 A8.25,8.25 0 0 1 39.25,9 L39.25,31 A8.25,8.25 0 0 1 31,39.25 L9,39.25 A8.25,8.25 0 0 1 0.75,31 L0.75,9 A8.25,8.25 0 0 1 9,0.75 Z"),
            Stroke = new SolidColorBrush(iconEdge),
            StrokeThickness = 1.5,
            Fill = new LinearGradientBrush(iconFrom, iconTo, 45)
        };
        iconHost.Children.Add(frame);

        var contentHost = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Margin = new Thickness(3),
            ClipToBounds = true
        };
        iconHost.Children.Add(contentHost);

        var iconBox = iconHost;

        // 整合包自带图标
        var packIcon = d.Ready ? FindPackIcon(d.Name) : null;
        if (packIcon != null)
        {
            // 完整显示，不裁剪也不留黑边
            contentHost.Child = new Image
            {
                Source = packIcon,
                Stretch = Stretch.Uniform
            };
        }
        else
        {
            contentHost.Child = BuildLoaderGlyph(d.Loader);
        }

        Grid.SetColumn(iconBox, 0);
        g.Children.Add(iconBox);

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = d.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
        // 已安装的版本右边已经写了状态，这里就不重复贴徽标了；未下载的还留一个提醒
        if (!d.Ready)
        {
            title.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x2A, 0x11)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = d.State,
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))
                }
            });
        }
        info.Children.Add(title);
        info.Children.Add(new TextBlock
        {
            Text = $"{d.Loader} · Java {d.Java} · {d.Mem} MB",
            FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x73, 0x8A)), Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(info, 1);
        g.Children.Add(info);

        var state = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var subParts = d.Sub.Split('·');
        var typeText = subParts.Length > 0 ? subParts[0].Trim() : d.State;
        var dateText = subParts.Length > 1 ? subParts[1].Trim() : d.Ago;
        state.Children.Add(new TextBlock
        {
            Text = typeText,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = d.Ready
                ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80))
                : new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8))
        });

        state.Children.Add(new TextBlock
        {
            Text = dateText,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x73, 0x8A)),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(state, 2);
        g.Children.Add(state);

        row.Child = g;
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x1D, 0x28));
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0D, 0x14, 0x1D));
        row.MouseLeftButtonUp += (_, _) =>
        {
            if (onClick != null) onClick();
            else SwitchToVersion(d.Name, d.Sub, d.Loader, d.Mem + " MB");
        };
        return row;
    }

    // ===== 推荐（新版横向卡片） =====
    private async void BuildMods()
    {
        if (ModGrid == null) return;
        // 先占位
        ShowModSkeleton();
        try
        {
            var real = await ModrinthService.GetPopularModsAsync(24);
            if (real.Count > 0)
            {
                _allMods = real;
                _modBatch = 0;
                RenderRealMods(CurrentBatch());
                StartModRotation();
                return;
            }
        }
        catch (Exception ex) { Log("模组加载失败: " + ex.Message); }
        RenderFallbackMods();
    }

    private void ShowModSkeleton()
    {
        ModGrid.Children.Clear();
        for (int i = 0; i < 6; i++)
        {
            var b = new Border
            {
                MinHeight = 64,
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0x0C, 0x12, 0x1B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock
                {
                    Text = "加载中…", FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x59, 0x6C, 0x84)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            ModGrid.Children.Add(b);
        }
    }

    private void RenderRealMods(List<ModrinthService.ModInfo> mods)
    {
        ModGrid.Children.Clear();
        var palette = new[] { "#4F8DF7", "#8B5CF6", "#22C55E", "#F59E0B", "#EC4899", "#14B8A6" };
        int idx = 0;
        foreach (var m in mods)
        {
            var c1 = palette[idx % palette.Length];
            idx++;
            var card = new Border
            {
                MinHeight = 64,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0C, 0x12, 0x1B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 图标：有 icon_url 就用图片，否则用首字母色块
            FrameworkElement icon;
            if (!string.IsNullOrEmpty(m.IconUrl))
            {
                var img = new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x33)),
                    VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true
                };
                var image = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill };
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(m.IconUrl, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                    bmp.EndInit();
                    image.Source = bmp;
                }
                catch { }
                img.Child = image;
                icon = img;
            }
            else
            {
                var b = new Border
                {
                    Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                    Background = (Brush)new BrushConverter().ConvertFromString(c1)!,
                    VerticalAlignment = VerticalAlignment.Center
                };
                b.Child = new TextBlock
                {
                    Text = m.Title.Substring(0, 1), FontSize = 17, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                icon = b;
            }
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            var info = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = m.Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            if (m.Categories.Length > 0)
            {
                titleRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = m.Categories[0], FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6)) }
                });
            }
            info.Children.Add(titleRow);
            info.Children.Add(new TextBlock
            {
                Text = m.Description, FontSize = 9.5, Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC4)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = "下载 " + FormatNum(m.Downloads), FontSize = 8.5, Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92))
            });
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            var arrow = new TextBlock
            {
                Text = "›", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0x59, 0x6C, 0x84)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.6
            };
            Grid.SetColumn(arrow, 2);
            g.Children.Add(arrow);

            card.Child = g;

            // 点卡片进这个 mod 的下载页
            var captured = m;
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => OpenModDownload(captured);
            card.MouseEnter += (_, _) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x16, 0x22, 0x33));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x59, 0xA8, 0xFF));
                arrow.Opacity = 1;
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0C, 0x12, 0x1B));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                arrow.Opacity = 0.6;
            };
            ModGrid.Children.Add(card);
        }
    }

    /// <summary>推荐卡片点进去 → 打开这个 mod 的下载窗口</summary>
    private void OpenModDownload(ModrinthService.ModInfo mod)
    {
        if (string.IsNullOrEmpty(mod.Id))
        {
            DarkDialog.Toast(this, "这个项目暂时打不开，去「下载 → 资源中心」搜一下");
            return;
        }

        var hit = new ModrinthService.SearchHit(
            mod.Id,
            mod.Slug,
            mod.Title,
            mod.Description,
            mod.IconUrl,
            mod.Downloads,
            "",
            mod.Categories ?? Array.Empty<string>(),
            "mod",
            Array.Empty<string>());

        var win = new ResourceDownloadWindow(hit, null, _selectedVersion) { Owner = this };
        win.ShowDialog();
    }

    private static string FormatNum(long n)
    {
        if (n >= 100_000_000) return (n / 100_000_000.0).ToString("0.#") + " 亿";
        if (n >= 10_000) return (n / 10_000.0).ToString("0.#") + " 万";
        return n.ToString();
    }

    private void RenderFallbackMods()
    {
        ModGrid.Children.Clear();
        var mods = new (string Name, string Cat, string Desc, string Stat, string C1, string C2)[]
        {
            ("Sodium",      "性能优化", "大幅提升帧数，降低卡顿",   "4.2 亿", "#4F8DF7", "#2D5BC4"),
            ("Iris Shaders","光影",    "兼容 OptiFine 光影包",    "1.8 亿", "#8B5CF6", "#5B3DC4"),
            ("JEI",         "实用工具", "物品查询，配方大全",     "3.1 亿", "#22C55E", "#15803D"),
            ("Xaero's Map", "地图",     "小地图 + 世界地图",       "2.6 亿", "#F59E0B", "#B45309"),
            ("Create",      "机械",     "机械动力，齿轮与传送带",  "1.2 亿", "#EC4899", "#9D174D"),
            ("Fabric API",  "前置库",   "绝大多数 Fabric 模组需要", "6.8 亿", "#14B8A6", "#0F766E"),
        };
        foreach (var (name, cat, desc, stat, c1, c2) in mods)
        {
            var card = new Border
            {
                MinHeight = 64,
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0C, 0x12, 0x1B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(10),
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString(c1)!,
                    (Color)ColorConverter.ConvertFromString(c2)!, 135),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.Child = new TextBlock
            {
                Text = name.Substring(0, 1), FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            var info = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            titleRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(7, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = cat, FontSize = 8.5, Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6)) }
            });
            info.Children.Add(titleRow);
            info.Children.Add(new TextBlock
            {
                Text = desc, FontSize = 9.5, Margin = new Thickness(0, 3, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC4)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            info.Children.Add(new TextBlock
            {
                Text = "下载 " + stat, FontSize = 8.5, Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92))
            });
            Grid.SetColumn(info, 1);
            g.Children.Add(info);

            var arrow = new TextBlock
            {
                Text = "›", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(0x59, 0x6C, 0x84)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.6
            };
            Grid.SetColumn(arrow, 2);
            g.Children.Add(arrow);

            card.Child = g;
            card.MouseEnter += (_, _) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0xF0, 0x16, 0x22, 0x33));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x59, 0xA8, 0xFF));
                arrow.Opacity = 1;
            };
            card.MouseLeave += (_, _) =>
            {
                card.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0C, 0x12, 0x1B));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                arrow.Opacity = 0.6;
            };
            ModGrid.Children.Add(card);
        }
    }

    // ===== 历史游玩 =====
    // ===== 右侧栏：整合包推荐 + 服务器推荐 =====
    private void BuildHistory() => BuildSidePanel();

    private async void BuildSidePanel()
    {
        BuildPacks();
        BuildServers();
        await Task.CompletedTask;
    }

    private int _packOffset;

    private void PackRefresh_Click(object sender, RoutedEventArgs e)
    {
        _packOffset = (_packOffset + 8) % 80;
        BuildPacks();
    }

    private async void BuildPacks()
    {
        if (PackList == null) return;
        PackList.Children.Clear();

        PackList.Children.Add(new TextBlock
        {
            Text = "加载中…",
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x59, 0x6C, 0x84)),
            Margin = new Thickness(2, 2, 0, 0)
        });

        var packs = await ModrinthService.GetPopularAsync("modpack", 8, _packOffset);
        PackList.Children.Clear();

        if (packs.Count == 0)
        {
            PackList.Children.Add(new TextBlock
            {
                Text = "暂时拉不到推荐",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(0x59, 0x6C, 0x84)),
                Margin = new Thickness(2, 2, 0, 0)
            });
            return;
        }

        foreach (var pack in packs)
            PackList.Children.Add(BuildPackRow(pack));
    }

    private Border BuildPackRow(ModrinthService.ModInfo pack)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand
        };

        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            ClipToBounds = true
        };
        if (!string.IsNullOrEmpty(pack.IconUrl))
        {
            try
            {
                icon.Child = new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(pack.IconUrl)),
                    Stretch = Stretch.UniformToFill
                };
            }
            catch { }
        }
        g.Children.Add(icon);

        var info = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = pack.Title, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        info.Children.Add(new TextBlock
        {
            Text = FormatNum(pack.Downloads) + " 次下载", FontSize = 8.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8B, 0xA8)), Margin = new Thickness(0, 1, 0, 0)
        });
        Grid.SetColumn(info, 1);
        g.Children.Add(info);

        row.Child = g;
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x59, 0xA8, 0xFF));
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        row.MouseLeftButtonUp += (_, _) => OpenModDownload(pack);

        return row;
    }

    private async void BuildServers()
    {
        if (ServerList == null) return;
        ServerList.Children.Clear();

        var servers = ServerCatalog.Pick(8, _serverRng);
        foreach (var srv in servers)
            ServerList.Children.Add(BuildServerRow(srv));

        await Task.CompletedTask;
    }

    private readonly Random _serverRng = new();

    private void ServerRefresh_Click(object sender, RoutedEventArgs e) => BuildServers();

    /// <summary>点服务器 → 介绍页</summary>
    private void ShowServerInfo(ServerCatalog.Server srv)
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = srv.Host,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        body.Children.Add(new TextBlock
        {
            Text = srv.Description,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        var meta = new TextBlock
        {
            Text = "类型：" + srv.Note + "\n版本：" + srv.Version + "\n人数：" + srv.Players,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8B, 0xA8)),
            Margin = new Thickness(0, 12, 0, 0),
            LineHeight = 18
        };
        body.Children.Add(meta);

        DarkDialog.ShowCustom(this, srv.Name, body,
            ("关闭", false),
            ("复制地址", true),
            ok =>
            {
                if (!ok) return;
                try
                {
                    Clipboard.SetText(srv.Host);
                    DarkDialog.Toast(this, "已复制 " + srv.Host);
                }
                catch { }
            });
    }

    private Border BuildServerRow(ServerCatalog.Server srv)
    {
        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand
        };

        var info = new StackPanel();
        info.Children.Add(new TextBlock
        {
            Text = srv.Name, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White
        });
        info.Children.Add(new TextBlock
        {
            Text = srv.Host + "  ·  " + srv.Note, FontSize = 8.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x8B, 0xA8)), Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        row.Child = info;

        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x59, 0xA8, 0xFF));
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        row.MouseLeftButtonUp += (_, _) => ShowServerInfo(srv);

        return row;
    }


    private string? _pendingSelectVersion;

    // ===== 切换版本转场 =====
    private string? _selectedVersion;

    private async void SwitchToVersion(string name, string sub, string loader, string mem)
    {
        _selectedVersion = name;
        var panel = DetailHost;
        if (panel == null) return;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(110));
        panel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await Task.Delay(110);
        DetailName.Text = name;
        DetailSub.Text = sub;
        if (DetailLoader != null) DetailLoader.Text = loader;
        if (DetailMem != null) DetailMem.Text = mem;
        if (panel.RenderTransform is TranslateTransform tt)
        {
            var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            tt.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        panel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    // ===== 下载小球 =====

    /// <summary>当前下载的状态，给详情窗口读</summary>
    public class DownloadState
    {
        public string Title = "";
        public string Message = "准备中…";
        public double Percent;
        public bool Running;
        public bool Finished;
        public bool Ok;
        public DateTime Started = DateTime.UtcNow;

        /// <summary>有阶段清单（安装/导入这类多步骤任务）</summary>
        public bool Detailed;

        /// <summary>当前任务累计下载的字节数，用来算速度</summary>
        public long Bytes;

        public readonly List<(string Stage, string Label, int Done, int Total, bool DoneFlag)> Steps = new();
    }

    public DownloadState CurrentDownload { get; } = new();

    /// <summary>下载状态变了就通知（详情窗口订阅）</summary>
    public event Action? DownloadChanged;

    /// <summary>详情窗口点「取消下载」时调用</summary>
    public Action? CancelCurrentDownload;

    private void NotifyDownloadChanged()
    {
        try { DownloadChanged?.Invoke(); } catch { }
    }

    private static readonly (string Stage, string Label)[] DownloadSteps =
    {
        ("json", "下载版本信息"),
        ("client", "下载客户端"),
        ("libraries", "下载支持库文件"),
        ("natives", "解压运行库"),
        ("assets", "下载资源文件"),
    };

    /// <summary>通用任务（单文件下载、上传这类）：只有百分比和一句话</summary>
    public void BeginTask(string title, string message = "准备中…")
    {
        ShowDownloadBall(title);
        CurrentDownload.Detailed = false;
        CurrentDownload.Steps.Clear();
        CurrentDownload.Message = message;
        NotifyDownloadChanged();
    }

    /// <summary>任务上报已下载字节，详情窗口据此算速度</summary>
    public void ReportTaskBytes(long bytes)
    {
        CurrentDownload.Bytes = bytes;
    }

    public void UpdateTask(double pct, string? message = null)
    {
        if (DownloadBall == null) return;

        CurrentDownload.Percent = pct;
        if (message != null) CurrentDownload.Message = message;

        BallPct.Text = Math.Round(pct).ToString("0") + "%";
        NotifyDownloadChanged();
    }

    public void EndTask(bool ok, string message) => FinishDownloadBall(ok, message);

    /// <summary>开始一次下载：显示小球 + 转圈</summary>
    public void ShowDownloadBall(string title)
    {
        if (DownloadBall == null) return;

        CurrentDownload.Title = title;
        CurrentDownload.Message = "准备中…";
        CurrentDownload.Percent = 0;
        CurrentDownload.Running = true;
        CurrentDownload.Finished = false;
        CurrentDownload.Ok = false;
        CurrentDownload.Started = DateTime.UtcNow;
        CurrentDownload.Detailed = true;
        CurrentDownload.Steps.Clear();
        foreach (var (stage, label) in DownloadSteps)
            CurrentDownload.Steps.Add((stage, label, 0, 0, false));

        BallPct.Text = "0%";
        BallSpinner.Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xC8, 0xFF));
        BallSpinner.Visibility = Visibility.Visible;
        BallRing.Visibility = Visibility.Visible;
        BallPct.Visibility = Visibility.Visible;

        DownloadBall.Visibility = Visibility.Visible;

        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1))
        { RepeatBehavior = RepeatBehavior.Forever };
        BallSpin.BeginAnimation(RotateTransform.AngleProperty, spin);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240));
        DownloadBall.BeginAnimation(UIElement.OpacityProperty, fade);

        NotifyDownloadChanged();
    }

    /// <summary>刷新进度：百分比 + 当前在做什么 + 每个阶段的完成情况</summary>
    public void UpdateDownloadBall(string stage, int done, int total, double pct, string? message = null)
    {
        if (DownloadBall == null) return;

        CurrentDownload.Percent = pct;
        if (message != null) CurrentDownload.Message = message;

        var index = Array.FindIndex(DownloadSteps, s => s.Stage == stage);
        for (var i = 0; i < CurrentDownload.Steps.Count; i++)
        {
            var (st, label, d, t, doneFlag) = CurrentDownload.Steps[i];
            if (i < index) CurrentDownload.Steps[i] = (st, label, t, t, true);
            else if (i == index) CurrentDownload.Steps[i] = (st, label, done, total, stage == "done");
        }

        BallPct.Text = Math.Round(pct).ToString("0") + "%";
        NotifyDownloadChanged();
    }

    private bool _detailOpen;

    /// <summary>结束：变绿/红，停转，稍后小球自己消失（详情窗口开着就先不收）</summary>
    public async void FinishDownloadBall(bool ok, string message)
    {
        CurrentDownload.Running = false;
        CurrentDownload.Finished = true;
        CurrentDownload.Ok = ok;
        CurrentDownload.Message = message;
        if (ok) CurrentDownload.Percent = 100;

        NotifyDownloadChanged();

        if (DownloadBall == null) return;

        BallSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        BallSpinner.Stroke = new SolidColorBrush(ok
            ? Color.FromRgb(0x4A, 0xDE, 0x80)
            : Color.FromRgb(0xF8, 0x71, 0x71));
        BallPct.Text = ok ? "✓" : "!";
        BallPct.FontSize = 20;

        await Task.Delay(ok ? 2600 : 5000);

        // 用户正开着详情窗口的话，等他关掉再收球
        if (!_detailOpen && CurrentDownload.Finished) HideDownloadBall();
    }

    public void HideDownloadBall()
    {
        if (DownloadBall == null) return;
        BallSpinner.Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xC8, 0xFF));
        BallPct.FontSize = 12;
        DownloadBall.Visibility = Visibility.Collapsed;
        CurrentDownload.Running = false;
    }

    private void DownloadBall_Click(object sender, MouseButtonEventArgs e)
    {
        if (!CurrentDownload.Running && !CurrentDownload.Finished) return;

        _detailOpen = true;
        try
        {
            var win = new DownloadDetailWindow(this) { Owner = this };
            win.ShowDialog();
        }
        catch (Exception ex)
        {
            Log("打开下载详情失败: " + ex.Message);
        }
        finally
        {
            _detailOpen = false;
        }

        // 关掉详情时如果已经装完了，球这时候才收
        if (CurrentDownload.Finished) HideDownloadBall();
    }

    /// <summary>装完之后刷新「版本选择」的列表</summary>
    public void RefreshInstalledList() => LoadInstalled();

    // ===== 标题栏 =====

    /// <summary>标题栏按设置显示：无 / 默认 / 自定义文本</summary>
    public void ApplyTitleBar()
    {
        if (TitleBarContent == null) return;

        var mode = AppSettings.Get("titleBarMode", "default");
        if (mode == "none")
        {
            TitleBarContent.Visibility = Visibility.Collapsed;
            return;
        }

        TitleBarContent.Visibility = Visibility.Visible;

        if (mode == "text")
        {
            var text = AppSettings.Get("titleBarText", "").Trim();
            TitleLabel.Text = string.IsNullOrEmpty(text) ? "Hearth" : text;
        }
        else
        {
            TitleLabel.Text = "Hearth";
        }
    }

    // ===== 主题 =====

    /// <summary>把当前主题应用到窗口（叠加色 + 导航高亮）</summary>
    public void ApplyTheme()
    {
        var accent = ThemeService.Accent;

        if (ThemeTint != null)
        {
            if (ThemeService.IsRainbow)
            {
                ThemeTint.Fill = ThemeService.RainbowBrush();
                ThemeTint.Opacity = 0.22;
            }
            else
            {
                ThemeTint.Fill = new SolidColorBrush(accent);
                ThemeTint.Opacity = ThemeService.Current.Id == "default" ? 0.05 : 0.18;
            }
        }

        // 重新给导航上色
        var active = new[] { NavHome, NavVersions, NavDownload, NavSettings }
            .FirstOrDefault(b => b != null && b.Background is SolidColorBrush sb && sb.Color.A > 0x20 && sb.Color != Colors.Transparent);
        if (active != null) SetActiveNav(active);
    }

    // ===== 导航 =====

    /// <summary>切换视图时淡入 + 轻微上浮，别硬切</summary>
    private static void ShowWithAnimation(UIElement view)
    {
        view.Visibility = Visibility.Visible;

        if (!AppSettings.Animations)
        {
            view.Opacity = 1;
            view.RenderTransform = null;
            return;
        }

        view.Opacity = 0;

        var slide = new TranslateTransform(0, 10);
        view.RenderTransform = slide;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var move = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(420))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

        view.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, move);
    }

    private void SetActiveNav(Button? active)
    {
        foreach (var b in new[] { NavHome, NavVersions, NavDownload, NavSettings })
        {
            if (b == null) continue;
            bool on = ReferenceEquals(b, active);
            var accent = ThemeService.Accent;
            b.Background = on
                ? new SolidColorBrush(Color.FromArgb(0x4D, accent.R, accent.G, accent.B))
                : Brushes.Transparent;
            b.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0));
        }
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        ShowWithAnimation(HomeView);
        VersionsView.Visibility = Visibility.Collapsed;
        DownloadView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        DetailHost.Visibility = Visibility.Collapsed;
        if (NavPanel != null) NavPanel.Visibility = Visibility.Collapsed;
        if (NavColumn != null) NavColumn.Width = new GridLength(0);
        if (DetailColumn != null) DetailColumn.Width = new GridLength(0);
        SetActiveNav(NavHome);
    }

    private void NavVersions_Click(object sender, RoutedEventArgs e)
    {
        HomeView.Visibility = Visibility.Collapsed;
        DownloadView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ShowWithAnimation(VersionsView);
        DetailHost.Visibility = Visibility.Visible;
        if (NavPanel != null) NavPanel.Visibility = Visibility.Visible;
        if (NavColumn != null) NavColumn.Width = new GridLength(176);
        if (DetailColumn != null) DetailColumn.Width = new GridLength(278);
        SetActiveNav(NavVersions);
    }

    private void NavDownload_Click(object sender, RoutedEventArgs e)
    {
        HomeView.Visibility = Visibility.Collapsed;
        VersionsView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ShowWithAnimation(DownloadView);
        ShowDownloadTab("versions");
        DetailHost.Visibility = Visibility.Collapsed;
        if (NavPanel != null) NavPanel.Visibility = Visibility.Visible;
        if (NavColumn != null) NavColumn.Width = new GridLength(176);
        if (DetailColumn != null) DetailColumn.Width = new GridLength(0);
        SetActiveNav(NavDownload);
    }

    private void GoDownload_Click(object sender, RoutedEventArgs e) => NavDownload_Click(NavDownload, e);

    // ===== 下载页的两个小栏目 =====

    private string _resourceType = "mod";
    private string _downloadTab = "versions";

    private void DownloadTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) ShowDownloadTab(tag);
    }

    private void ShowDownloadTab(string tab)
    {
        _downloadTab = tab;
        if (VersionsTabContent != null)
            VersionsTabContent.Visibility = tab == "versions" ? Visibility.Visible : Visibility.Collapsed;
        if (ResourcesTabContent != null)
            ResourcesTabContent.Visibility = tab == "resources" ? Visibility.Visible : Visibility.Collapsed;

        StyleDownloadTab(TabVersions, tab == "versions");
        StyleDownloadTab(TabResources, tab == "resources");

        if (tab == "resources")
        {
            if (ResourceVersion != null && string.IsNullOrWhiteSpace(ResourceVersion.Text))
                ResourceVersion.Text = GuessMcVersion() ?? "";
            if (ResourceList != null && ResourceList.Children.Count == 0)
                ResourceSearch_Click(this, new RoutedEventArgs());
        }
    }

    private void StyleDownloadTab(Button? b, bool on)
    {
        if (b == null) return;
        b.Style = null;
        b.Background = on
            ? new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB))
            : Brushes.Transparent;
        b.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0));
        b.BorderThickness = new Thickness(0);
        b.Cursor = Cursors.Hand;
    }

    /// <summary>从当前选中的版本号里猜 MC 版本（1.20.1-forge-47.2.0 → 1.20.1）</summary>
    private string? GuessMcVersion()
    {
        var id = _selectedVersion;
        if (string.IsNullOrEmpty(id)) return null;
        var cut = id.IndexOf("-forge-", StringComparison.Ordinal);
        if (cut < 0) cut = id.IndexOf("-neoforge-", StringComparison.Ordinal);
        if (cut > 0) return id[..cut];
        if (id.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
        {
            // fabric-loader-0.20.1-1.20.1
            var parts = id.Split('-');
            return parts.Length >= 2 ? parts[^1] : null;
        }
        return id;
    }

    private void ResourceType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) _resourceType = tag;
        StyleResourceFilters();
        ResourceSearch_Click(sender, e);
    }

    private void StyleResourceFilters()
    {
        void Set(Button? b, bool on)
        {
            if (b == null) return;
            b.Background = on
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB))
                : Brushes.Transparent;
            b.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0));
        }
        Set(ResAll, _resourceType == "all");
        Set(ResMod, _resourceType == "mod");
        Set(ResPack, _resourceType == "resourcepack");
        Set(ResShader, _resourceType == "shader");
        Set(ResData, _resourceType == "datapack");
    }

    private async void ResourceSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ResourceList == null) return;
        StyleResourceFilters();

        ResourceList.Children.Clear();
        ResourceList.Children.Add(new TextBlock
        {
            Text = "正在搜索…",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
            Margin = new Thickness(4, 14, 0, 0)
        });

        try
        {
            var query = ResourceQuery?.Text?.Trim() ?? "";
            var ver = ResourceVersion?.Text?.Trim();
            var type = _resourceType == "all" ? "mod" : _resourceType;
            var hits = await ModrinthService.SearchAsync(query, string.IsNullOrEmpty(ver) ? null : ver, type);

            ResourceList.Children.Clear();
            if (hits.Count == 0)
            {
                ResourceList.Children.Add(new TextBlock
                {
                    Text = "没搜到，换个关键词或版本试试",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                    Margin = new Thickness(4, 14, 0, 0)
                });
                return;
            }

            foreach (var hit in hits) ResourceList.Children.Add(BuildResourceRow(hit, ver));
        }
        catch (Exception ex)
        {
            ResourceList.Children.Clear();
            ResourceList.Children.Add(new TextBlock
            {
                Text = "搜索失败：" + ex.Message,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x9F, 0x80)),
                Margin = new Thickness(4, 14, 0, 0)
            });
        }
    }

    private Border BuildResourceRow(ModrinthService.SearchHit hit, string? mcVersion)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 图标
        FrameworkElement icon;
        if (!string.IsNullOrEmpty(hit.IconUrl))
        {
            var img = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x33)),
                VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true
            };
            var image = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill };
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(hit.IconUrl, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                image.Source = bmp;
            }
            catch { }
            img.Child = image;
            icon = img;
        }
        else
        {
            var b = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x3A, 0x50)),
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = new TextBlock
            {
                Text = hit.Title.Length > 0 ? hit.Title[..1] : "?",
                FontSize = 16, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            icon = b;
        }
        g.Children.Add(icon);

        var info = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock { Text = hit.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
        if (hit.Categories.Length > 0)
            titleRow.Children.Add(new TextBlock
            {
                Text = "  " + string.Join(" · ", hit.Categories.Take(2)),
                FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                VerticalAlignment = VerticalAlignment.Center
            });
        info.Children.Add(titleRow);
        info.Children.Add(new TextBlock
        {
            Text = hit.Description,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC4)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        });
        info.Children.Add(new TextBlock
        {
            Text = "下载 " + FormatCount(hit.Downloads) + " · 更新 " + FormatDate(hit.Updated),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(info, 1);
        g.Children.Add(info);

        var action = new TextBlock
        {
            Text = "下载",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0)
        };
        Grid.SetColumn(action, 2);
        g.Children.Add(action);

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0D, 0x14, 0x1D)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Child = g
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x1D, 0x28));
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0D, 0x14, 0x1D));
        row.MouseLeftButtonUp += (_, _) => OpenResourceDownload(hit, mcVersion);

        return row;
    }

    private static string FormatCount(long n)
    {
        if (n >= 100_000_000) return (n / 100_000_000.0).ToString("0.0") + " 亿";
        if (n >= 10_000) return (n / 10_000.0).ToString("0.0") + " 万";
        return n.ToString();
    }

    private static string FormatDate(string iso)
    {
        return DateTime.TryParse(iso, out var d) ? d.ToString("yyyy-MM-dd") : "";
    }

    private void OpenResourceDownload(ModrinthService.SearchHit hit, string? mcVersion)
    {
        var ver = string.IsNullOrEmpty(mcVersion) ? GuessMcVersion() : mcVersion;
        var win = new ResourceDownloadWindow(hit, ver, _selectedVersion) { Owner = this };
        win.ShowDialog();
    }



    // ===== 版本设置 =====

    private void OpenVersionFolder_Click(object sender, RoutedEventArgs e)
    {
        var id = _selectedVersion;
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions", id);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
            { UseShellExecute = true });
        }
        catch { }
    }

    private void EditVersion_Click(object sender, RoutedEventArgs e)
    {
        var id = _selectedVersion;
        if (string.IsNullOrEmpty(id))
        {
            DarkDialog.Toast(this, "先在列表里选一个版本");
            return;
        }

        var win = new VersionSettingsWindow(id) { Owner = this };
        win.ShowDialog();
        LoadInstalled();
    }

    // ===== 游戏文件夹 =====

    private void UpdateFolderLabel()
    {
        if (FolderBtn == null) return;
        var name = GameFolderService.Current?.Name ?? "默认";
        FolderBtn.Content = "📁 " + name + " ▾";
    }

    private System.Windows.Controls.Primitives.Popup? _folderPopup;

    private void FolderBtn_Click(object sender, RoutedEventArgs e)
    {
        // 自绘面板，不用 ContextMenu（它的模板会被系统主题搞成白色）
        _folderPopup?.SetCurrentValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty, false);

        var panel = new StackPanel { MinWidth = 230 };

        Border MakeRow(string text, bool isCurrent, Action onClick)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Background = isCurrent
                    ? new SolidColorBrush(Color.FromArgb(0x40, 0x25, 0x63, 0xEB))
                    : Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            var label = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            row.Child = label;

            row.MouseEnter += (_, _) =>
            {
                if (!isCurrent)
                    row.Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
            };
            row.MouseLeave += (_, _) =>
            {
                if (!isCurrent) row.Background = Brushes.Transparent;
            };
            row.MouseLeftButtonUp += (_, _) =>
            {
                if (_folderPopup != null) _folderPopup.IsOpen = false;
                onClick();
            };

            return row;
        }

        foreach (var folder in GameFolderService.Folders)
        {
            var count = GameFolderService.CountVersions(folder.Path);
            var isCurrent = string.Equals(folder.Path, GameFolderService.CurrentPath, StringComparison.OrdinalIgnoreCase);
            var captured = folder.Path;

            panel.Children.Add(MakeRow(
                (isCurrent ? "✓  " : "     ") + folder.Name + "（" + count + " 个版本）",
                isCurrent,
                () => GameFolderService.Switch(captured)));
        }

        panel.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(4, 5, 4, 5),
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
        });

        panel.Children.Add(MakeRow("＋ 添加文件夹…", false, () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择游戏文件夹（.minecraft）" };
            if (dialog.ShowDialog(this) == true) GameFolderService.Add(dialog.FolderName);
        }));

        if (GameFolderService.Folders.Count > 1)
        {
            panel.Children.Add(MakeRow("移除当前文件夹（不删文件）", false,
                () => GameFolderService.Remove(GameFolderService.CurrentPath)));
        }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFA, 0x16, 0x1E, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(5)
        };
        border.Child = panel;

        _folderPopup = new System.Windows.Controls.Primitives.Popup
        {
            Child = border,
            PlacementTarget = FolderBtn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            VerticalOffset = 4
        };

        _folderPopup.IsOpen = true;
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        HomeView.Visibility = Visibility.Collapsed;
        VersionsView.Visibility = Visibility.Collapsed;
        DownloadView.Visibility = Visibility.Collapsed;
        ShowWithAnimation(SettingsView);
        DetailHost.Visibility = Visibility.Collapsed;
        if (NavPanel != null) NavPanel.Visibility = Visibility.Visible;
        if (NavColumn != null) NavColumn.Width = new GridLength(176);
        if (DetailColumn != null) DetailColumn.Width = new GridLength(0);
        SetActiveNav(NavSettings);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var win = new FeedbackWindow();
        win.ShowDialog();
    }

    // ===== 整合包导入 =====

    private void ImportPack_Click(object sender, RoutedEventArgs e) => OpenImport();

    private void OpenImport(string? path = null)
    {
        var win = new ImportWindow(path) { Owner = this };
        win.ShowDialog();
        if (!win.Imported) return;

        ThemeService.CountImportedPack();
        LoadInstalled();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        OpenImport(files[0]);
    }

    private void RefreshDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hearth", "versions_cache.json");
            if (File.Exists(cache)) File.Delete(cache);
        }
        catch { }
        LoadDownloadable();
    }

    private void StartGame_Click(object sender, RoutedEventArgs e) => NavVersions_Click(sender, e);

    // ===== 登录 =====
    private bool _loggedIn = false;
    private string? _playerName;

    private UpdateService.ReleaseInfo? _pendingUpdate;

    // ===== 启动画面（裂开转场） =====

    private bool _splashDone;
    private readonly System.Threading.Tasks.TaskCompletionSource _splashReady = new();

    private void PrepareSplash()
    {
        try
        {
            SplashBg.Source = new BitmapImage(new Uri("pack://application:,,,/assets/hero.jpg", UriKind.Absolute));
        }
        catch { }

        try
        {
            SplashIcon.Source = new BitmapImage(new Uri("pack://application:,,,/splash-icon.png", UriKind.Absolute));
        }
        catch { }

        SplashStatus.Text = "正在读取配置…";
        SetSplashProgress(35);
    }

    private void SetSplashProgress(double pct, string? text = null)
    {
        var anim = new DoubleAnimation(SplashBar.Width, Math.Max(0, Math.Min(170, 170 * pct / 100)),
            TimeSpan.FromMilliseconds(300))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        SplashBar.BeginAnimation(FrameworkElement.WidthProperty, anim);

        if (!string.IsNullOrEmpty(text)) SplashStatus.Text = text;
    }

    /// <summary>加载完成后：启动画面从中间十字裂开，四块飞出去</summary>
    private async void FinishSplash()
    {
        if (_splashDone) return;
        _splashDone = true;

        try
        {
            await FinishSplashCore();
        }
        catch
        {
            // 出什么问题都别把启动画面留在那儿
            SplashLayer.Visibility = Visibility.Collapsed;
        }
    }

    private async Task FinishSplashCore()
    {

        SetSplashProgress(100, "好了");
        await Task.Delay(340);

        var w = SplashLayer.ActualWidth;
        var h = SplashLayer.ActualHeight;
        if (w < 10 || h < 10)
        {
            SplashLayer.Visibility = Visibility.Collapsed;
            return;
        }

        RenderTargetBitmap shot;
        try
        {
            shot = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            shot.Render(SplashLayer);
            shot.Freeze();
        }
        catch
        {
            SplashLayer.Visibility = Visibility.Collapsed;
            return;
        }

        // 藏原件，上碎片
        SplashLayer.Background = Brushes.Transparent;
        foreach (var child in SplashLayer.Children.OfType<FrameworkElement>().ToList())
        {
            if (child == SplashShards) continue;
            child.Visibility = Visibility.Collapsed;
        }
        SplashShards.Visibility = Visibility.Visible;

        var halfW = w / 2;
        var halfH = h / 2;
        const int duration = 640;

        var quads = new (double X, double Y, double OutX, double OutY)[]
        {
            (0, 0, -w, -h),
            (halfW, 0, w, -h),
            (0, halfH, -w, h),
            (halfW, halfH, w, h),
        };

        foreach (var q in quads)
        {
            var img = new Image
            {
                Source = shot,
                Stretch = Stretch.None,
                Width = w,
                Height = h,
                Clip = new RectangleGeometry(new Rect(q.X, q.Y, halfW, halfH))
            };
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);

            var translate = new TranslateTransform(0, 0);
            img.RenderTransform = translate;
            SplashShards.Children.Add(img);

            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            translate.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(q.OutX, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(q.OutY, TimeSpan.FromMilliseconds(duration)) { EasingFunction = ease });
        }

        await Task.Delay(duration + 40);

        SplashLayer.Visibility = Visibility.Collapsed;
        SplashShards.Children.Clear();
    }

    /// <summary>启动后查一次 GitHub Releases</summary>
    private async Task CheckUpdateAsync()
    {
        try
        {
            // 设置里关掉了更新提示就不查
            if (AppSettings.UpdateMode == "off") return;

            // 等界面先画出来
            await Task.Delay(2500);
            if (_pendingUpdate != null) return;

            var latest = await UpdateService.GetLatestAsync();
            if (latest == null) return;
            if (!UpdateService.IsNewer(latest.Tag, AppSettings.Version)) return;

            _pendingUpdate = latest;

            // 状态栏右侧亮一个可点的提示
            if (UpdateBar != null)
            {
                UpdateBar.Visibility = Visibility.Visible;
                if (UpdateBarText != null)
                    UpdateBarText.Text = "有新版本 " + latest.Tag.TrimStart('v', 'V') + "，点这里更新";
            }
        }
        catch { }
    }

    private void UpdateBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (_pendingUpdate != null) ShowUpdateDialog(_pendingUpdate);
    }

    /// <summary>更新弹窗：说明 + 下载 + 安装</summary>
    private void ShowUpdateDialog(UpdateService.ReleaseInfo info)
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = "当前版本 " + AppSettings.Version + "   →   " + info.Tag.TrimStart('v', 'V'),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDC)),
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (!string.IsNullOrWhiteSpace(info.Notes))
        {
            // 更新说明可能很长，套个滚动条
            body.Children.Add(new ScrollViewer
            {
                MaxHeight = 300,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 2, 0, 0),
                Content = new TextBlock
                {
                    Text = info.Notes,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18
                }
            });
        }

        var progress = new System.Windows.Controls.ProgressBar
        {
            Height = 5,
            Margin = new Thickness(0, 14, 0, 0),
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            BorderThickness = new Thickness(0)
        };
        body.Children.Add(progress);

        DarkDialog.ShowCustom(this, "发现新版本",
            body,
            ("稍后再说", false),
            ("下载并安装", true),
            async ok =>
            {
                if (!ok) return;

                if (string.IsNullOrEmpty(info.DownloadUrl))
                {
                    DarkDialog.Info(this, "更新失败", "这个版本没有提供安装包");
                    return;
                }

                progress.Visibility = Visibility.Visible;

                try
                {
                    // 下载更新包 → 解压 → 生成替换脚本（在当前目录覆盖 Hearth.exe）
                    var runner = await UpdateService.PrepareUpdateAsync(info,
                        new Progress<(long Done, long Total)>(p =>
                        {
                            if (p.Total > 0)
                                progress.Value = 100.0 * p.Done / p.Total;
                        }));

                    // 用隐藏窗口跑替换脚本，然后自己退出
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c \"" + runner + "\"",
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });

                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    var reason = ex.Message;
                    if (reason.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
                        reason.Contains("canceled", StringComparison.OrdinalIgnoreCase))
                        reason = "下载超时了。可能是网络太慢或者 GitHub 这会儿连不上。\n\n" +
                                 "可以稍后再试，或者直接去下面的页面手动下载安装包：\n" +
                                 "github.com/suiyu2859514286cc-hub/hearth/releases/latest";

                    DarkDialog.Info(this, "下载更新失败", reason);
                }
            });
    }

    /// <summary>这个实例正在用的账号（账户窗口要读它，不能读文件）</summary>
    public MicrosoftAuth.Account? CurrentAccount => _account;

    /// <summary>给账户窗口用：账号变了就刷一下主界面</summary>
    public void ApplyAccountFromOutside() => ApplyAccount();

    /// <summary>当前实例正在用的账号（启动时读一次，之后只用这份，避免多开互相干扰）</summary>
    private MicrosoftAuth.Account? _account;
    private bool _offlineMode;
    private bool _thirdPartyMode;

    /// <summary>按已保存的账号刷新界面</summary>
    private void ApplyAccount()
    {
        _account = MicrosoftAuth.Load();

        if (_account != null && !string.IsNullOrEmpty(_account.Name))
        {
            SetAccountUI(_account.Name);
            _ = RefreshAvatarAsync();
            return;
        }

        // 没有正版账号，但之前用过离线名
        var offlineName = AppSettings.Get("offlineName", "");
        if (!string.IsNullOrEmpty(offlineName)) SetAccountUI(offlineName, offline: true);
        else SetAccountUI(null);
    }

    /// <summary>登录状态上屏。name 为空表示未登录；offline 表示离线账号</summary>
    private void SetAccountUI(string? name, bool offline = false)
    {
        var loggedIn = !string.IsNullOrEmpty(name);
        _loggedIn = loggedIn;
        _playerName = name;
        _offlineMode = offline;

        if (loggedIn)
        {
            var hint = offline ? "离线账号 · 只能玩单机" : "微软账号 · 正版";

            AccountName.Text = name!;
            AccountHint.Text = hint;
            AvatarText.Text = name!.Substring(0, 1).ToUpperInvariant();
            if (AccountName2 != null) AccountName2.Text = name!;
            if (AccountHint2 != null) AccountHint2.Text = hint;
            if (MsLogo2 != null) MsLogo2.Visibility = Visibility.Collapsed;
            if (AvatarText2 != null)
            {
                AvatarText2.Text = name!.Substring(0, 1).ToUpperInvariant();
                AvatarText2.Visibility = Visibility.Visible;
            }
            if (TitleText != null) TitleText.Text = "欢迎回来，" + name;
            if (OfflineEntry2 != null) OfflineEntry2.Visibility = Visibility.Collapsed;

            if (offline)
            {
                // 离线：名字下面显示 UUID，让人知道这是本地身份
                var uuid = LoginWindow.OfflineUuid(name!);
                AccountHint.Text = "离线 · " + uuid[..8];
                if (AccountHint2 != null) AccountHint2.Text = "离线 · " + uuid[..8];
                // 离线不解锁主题，也没有皮肤头像
                if (AvatarImage != null) { AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed; }
                if (AvatarImage2 != null) { AvatarImage2.Source = null; AvatarImage2.Visibility = Visibility.Collapsed; }
                if (AvatarText != null) AvatarText.Visibility = Visibility.Visible;
            }
            else
            {
                AppSettings.Set("premiumUnlocked", "1");   // 登录正版才解锁主题
                _ = RefreshAvatarAsync();
            }
        }
        else
        {
            AccountName.Text = "未登录";
            AccountHint.Text = "点击登录微软账号";
            AvatarText.Text = "?";
            if (OfflineEntry2 != null) OfflineEntry2.Visibility = Visibility.Visible;
            if (AccountName2 != null) AccountName2.Text = "未登录";
            if (AccountHint2 != null) AccountHint2.Text = "点击登录微软账号";
            if (MsLogo2 != null) MsLogo2.Visibility = Visibility.Visible;
            if (AvatarText2 != null) { AvatarText2.Text = ""; AvatarText2.Visibility = Visibility.Collapsed; }
            if (TitleText != null) TitleText.Text = "欢迎回来";
            if (AvatarImage != null) { AvatarImage.Source = null; AvatarImage.Visibility = Visibility.Collapsed; }
            if (AvatarImage2 != null) { AvatarImage2.Source = null; AvatarImage2.Visibility = Visibility.Collapsed; }
            if (AvatarText != null) AvatarText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>首页卡片上的「离线登录」入口</summary>
    private void OfflineEntry_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;   // 别再触发卡片本身的正版登录
        ShowAccountChooser();
    }

    /// <summary>给账户窗口用：从外面唤起登录方式选择</summary>
    public void ShowAccountChooserFromOutside() => ShowAccountChooser();

    /// <summary>选一种"非微软"的登录方式</summary>
    private void ShowAccountChooser()
    {
        var choice = DarkDialog.Choose(this, "不登录微软？",
            "· 纯离线：只填个名字，能玩单机，皮肤是默认的" + Environment.NewLine +
            "· 第三方账号：用皮肤站（LittleSkin / 红石）账号登录，能显示自己的皮肤，也能进支持皮肤站的服务器" + Environment.NewLine + Environment.NewLine +
            "注意：这两种都进不了正版服务器（Hypixel 之类）。",
            ("纯离线", false),
            ("第三方账号", true),
            ("算了", false));

        if (choice < 0 || choice == 2) return;   // 取消

        if (choice == 1)
        {
            var win = new ThirdPartyWindow { Owner = this };
            if (win.ShowDialog() == true) ApplyThirdPartyAccount(win);
            return;
        }

        var name = DarkDialog.Input(this, "离线登录",
            "填一个名字，只在游戏里显示。以后用同一个名字，存档和皮肤数据能对上。",
            AppSettings.Get("offlineName", ""),
            "3-16 位，只能用英文字母、数字、下划线");

        if (name == null) return;

        name = name.Trim();

        if (name.Length < 3 || name.Length > 16 ||
            !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_]+$"))
        {
            DarkDialog.Info(this, "名字不合适",
                "要用 3 到 16 位的英文字母、数字或下划线。\n\n比如：Steve、Alex_01");
            return;
        }

        AppSettings.Set("offlineName", name);
        SetAccountUI(name, offline: true);
        DarkDialog.Toast(this, "已用离线名字「" + name + "」");
    }

    /// <summary>第三方（皮肤站）账号登录成功后上屏</summary>
    private void ApplyThirdPartyAccount(ThirdPartyWindow win)
    {
        AppSettings.Set("thirdPartyToken", win.AccessToken ?? "");
        AppSettings.Set("thirdPartyName", win.AccountName ?? "");
        AppSettings.Set("thirdPartyUuid", win.AccountUuid ?? "");
        if (!string.IsNullOrEmpty(win.ServerUrl)) AppSettings.Set("authlibUrl", win.ServerUrl!);

        _thirdPartyMode = true;
        SetAccountUI(win.AccountName);

        DarkDialog.Toast(this, "已用皮肤站账号「" + win.AccountName + "」登录");
    }

    private async void Account_Click(object sender, MouseButtonEventArgs e)
    {
        if (_loggedIn)
        {
            // 离线 / 第三方账号：跟正版一样开账户页，里面能看到信息、退出、换方式
            if (_offlineMode || _thirdPartyMode)
            {
                var offlineWin = new AccountWindow(_playerName, _thirdPartyMode) { Owner = this };
                offlineWin.ShowDialog();

                if (offlineWin.LoggedOut)
                {
                    if (_thirdPartyMode)
                    {
                        AppSettings.Set("thirdPartyToken", "");
                        AppSettings.Set("thirdPartyName", "");
                        AppSettings.Set("thirdPartyUuid", "");
                    }
                    else
                    {
                        // 离线名字也要清掉，不然下次启动又自动登回离线
                        AppSettings.Set("offlineName", "");
                    }

                    _thirdPartyMode = false;
                    _offlineMode = false;
                    SetAccountUI(null);
                    DarkDialog.Toast(this, "已退出");
                }
                return;
            }

            var win = new AccountWindow { Owner = this };
            win.ShowDialog();

            if (win.LoggedOut) SetAccountUI(null);
            else await RefreshAvatarAsync();
            return;
        }

        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true)
        {
            var isOffline = string.IsNullOrEmpty(login.AccessToken);
            SetAccountUI(login.AccountName, offline: isOffline);
        }
    }

    /// <summary>拉真实皮肤头像</summary>
    private async Task RefreshAvatarAsync()
    {
        var acc = _account ?? MicrosoftAuth.Load();
        if (acc == null || AvatarImage == null) return;

        try
        {
            var bmp = await SkinService.LoadAvatarAsync(acc.Uuid, acc.Name);
            if (bmp != null)
            {
                AvatarImage.Source = bmp;
                AvatarImage.Visibility = Visibility.Visible;
                if (AvatarText != null) AvatarText.Visibility = Visibility.Collapsed;

                if (AvatarImage2 != null)
                {
                    AvatarImage2.Source = bmp;
                    AvatarImage2.Visibility = Visibility.Visible;
                }
                if (AvatarText2 != null) AvatarText2.Visibility = Visibility.Collapsed;
                if (MsLogo2 != null) MsLogo2.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
    }

    // ===== 启动动画 =====
    private bool _launching = false;

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_launching) return;

        var versionId = _selectedVersion;
        if (string.IsNullOrWhiteSpace(versionId))
        {
            ProgressText.Text = "先在上面选一个版本";
            LaunchProgress.Visibility = Visibility.Visible;
            await Task.Delay(1600);
            LaunchProgress.Visibility = Visibility.Collapsed;
            return;
        }

        // 没登录启动不了：游戏需要一个名字，也要知道用谁的身份
        if (string.IsNullOrWhiteSpace(_playerName))
        {
            var goLogin = DarkDialog.Confirm(this, "还没登录",
                "启动游戏之前要先选一种登录方式：" + Environment.NewLine + Environment.NewLine +
                "· 微软正版账号：能进正版服务器" + Environment.NewLine +
                "· 第三方账号：用皮肤站账号，能显示自己的皮肤" + Environment.NewLine +
                "· 纯离线：只填个名字，只能玩单机",
                "去登录", "算了", false);

            if (goLogin) ShowAccountChooser();
            return;
        }

        _launching = true;
        LaunchBtn.IsEnabled = false;
        var old = LaunchBtn.Content;
        LaunchBtn.Content = "启 动 中 …";
        LaunchProgress.Visibility = Visibility.Visible;

        // 可以中途取消
        _launchCts?.Dispose();
        _launchCts = new CancellationTokenSource();
        var launchToken = _launchCts.Token;
        if (LaunchCancelBtn != null) LaunchCancelBtn.Visibility = Visibility.Visible;
        ProgressText.Text = "正在检查 Java…";

        var steps = new (string Text, int To)[]
        {
            ("正在检查 Java…", 30),
            ("正在拼 classpath…", 62),
            ("正在启动游戏…", 100),
        };
        foreach (var (text, to) in steps)
        {
            ProgressText.Text = text;
            var anim = new DoubleAnimation(ProgressFill.Width, to, TimeSpan.FromMilliseconds(380))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, anim);
            await Task.Delay(400);
        }

        var player = string.IsNullOrWhiteSpace(_playerName) ? "Player" : _playerName;

        // 版本设置里填的内存要真的用上
        var mem = MemoryUtil.Resolve(versionId);

        // 正版：带上令牌，服务器才认得出你是谁
        string? token = null;
        string? accountUuid = null;
        var account = _account;
        if (account == null) account = MicrosoftAuth.Load();
        if (account != null)
        {
            try
            {
                if (account.ExpiresAt <= DateTime.UtcNow.AddMinutes(10))
                {
                    ProgressText.Text = "登录过期，正在刷新…";
                    account = await MicrosoftAuth.RefreshAsync(account.RefreshToken, CancellationToken.None);
                    MicrosoftAuth.Save(account);
                }

                token = account.AccessToken;
                accountUuid = account.Uuid;
                player = account.Name;
            }
            catch (Exception ex)
            {
                ProgressText.Text = "刷新登录失败，这次按离线启动：" + ex.Message;
                await Task.Delay(1200);
            }
        }

        // Java 下载不走小球，进度就显示在启动进度条下面
        var javaProgress = new Progress<(string Msg, double Pct)>(p => ProgressText.Text = p.Msg);

        var (ok, message) = await LaunchService.LaunchAsync(
            versionId, player, mem, javaProgress, launchToken, token, accountUuid,
            major => Dispatcher.Invoke(() => DarkDialog.Confirm(
                this,
                "需要 Java " + major,
                "这个版本要用 Java " + major + " 运行，本机没找到。\n\n要我自动下载一个吗？（约 180 MB，只下一次）",
                "下载", "取消")),
            null,
            exitCode => Dispatcher.BeginInvoke(new Action(() => OnGameExited(versionId, exitCode))));

        ProgressText.Text = message;

        if (ok)
        {
            PlayHistory.Add(versionId);
            BuildHistory();

            var count = ThemeService.CountLaunch();
            ProgressText.Text = message + "  （累计启动 " + count + " 次）";
            DarkDialog.Toast(this, "启动成功");
            await Task.Delay(900);
        }
        else
        {
            // 失败要说清楚，并把日志路径给出来
            var cancelled = launchToken.IsCancellationRequested;
            if (cancelled)
            {
                ProgressText.Text = "已取消启动";
            }
            else
            {
                ProgressText.Text = "启动失败";
                var logPath = Path.Combine(Path.GetTempPath(), "hearth-launch.log");
                var detail = message;
                try
                {
                    if (File.Exists(logPath))
                    {
                        var tail = File.ReadAllText(logPath);
                        if (tail.Length > 1500) tail = "…" + tail[^1500..];
                        detail += Environment.NewLine + Environment.NewLine + "最近的日志：" +
                                  Environment.NewLine + tail;
                    }
                }
                catch { }

                DarkDialog.Info(this, "启动失败", detail);
            }
            await Task.Delay(600);
        }

        if (LaunchCancelBtn != null) LaunchCancelBtn.Visibility = Visibility.Collapsed;
        _launchCts?.Dispose();
        _launchCts = null;

        ProgressFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        ProgressFill.Width = 0;
        LaunchProgress.Visibility = Visibility.Collapsed;
        LaunchBtn.Content = old;
        LaunchBtn.IsEnabled = true;
        _launching = false;
    }

    /// <summary>游戏进程退出：正常关闭不管，异常退出就分析一下</summary>
    private void OnGameExited(string versionId, int exitCode)
    {
        // 退出码 0 是正常关游戏，不打扰
        if (exitCode == 0) return;

        var result = CrashAnalyzer.Analyze(versionId, exitCode);

        // 认得出原因才弹窗；认不出也给一份，但语气放软
        ShowCrashReport(versionId, result);
    }

    private void ShowCrashReport(string versionId, CrashAnalyzer.Result result)
    {
        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = result.Reason,
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        body.Children.Add(new TextBlock
        {
            Text = "怎么办",
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF)),
            Margin = new Thickness(0, 14, 0, 4)
        });

        body.Children.Add(new TextBlock
        {
            Text = result.Suggestion,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDC)),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        });

        var tail = CrashAnalyzer.ReadTail(result.LogPath);
        if (!string.IsNullOrWhiteSpace(tail))
        {
            var box = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 0),
                MaxHeight = 180
            };

            box.Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = tail,
                    FontFamily = new FontFamily("Consolas, Microsoft YaHei UI"),
                    FontSize = 9.5,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            body.Children.Add(box);
        }

        DarkDialog.ShowCustom(this, result.Title, body,
            ("知道了", false),
            ("复制日志路径", true),
            ok =>
            {
                if (!ok) return;
                try
                {
                    Clipboard.SetText(result.LogPath);
                    DarkDialog.Toast(this, "路径已复制");
                }
                catch { }
            });
    }

    private CancellationTokenSource? _launchCts;

    private void LaunchCancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _launchCts?.Cancel();
            ProgressText.Text = "正在取消…";
            if (LaunchCancelBtn != null) LaunchCancelBtn.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    // ===== 布局编辑 =====
    private bool _editMode = false;
    private readonly Dictionary<FrameworkElement, Thumb> _handles = new();
    private FrameworkElement? _dragEl;
    private Point _dragStart;
    private double _startLeft, _startTop;

    private IEnumerable<(FrameworkElement El, string Id)> LayoutTargets()
    {
        if (TitleBox != null) yield return (TitleBox, "title");
        if (RecBox != null) yield return (RecBox, "rec");
        if (AccountBox2 != null) yield return (AccountBox2, "login");
        if (GoBox != null) yield return (GoBox, "go");
        if (QuoteBox != null) yield return (QuoteBox, "quote");
        if (SidePanel != null) yield return (SidePanel, "side");
    }

    private void ApplySavedLayout()
    {
        var saved = LayoutService.Load();
        if (saved.Count == 0) return;
        foreach (var (el, id) in LayoutTargets())
            if (saved.TryGetValue(id, out var box) && el.Parent is Canvas)
                LayoutService.Apply(el, box);
    }

    private void SaveCurrentLayout()
    {
        var list = new List<LayoutService.Box>();
        foreach (var (el, id) in LayoutTargets())
        {
            double x = el.Parent is Canvas ? Canvas.GetLeft(el) : 0;
            double y = el.Parent is Canvas ? Canvas.GetTop(el) : 0;
            if (double.IsNaN(x)) x = 0;
            if (double.IsNaN(y)) y = 0;
            list.Add(new LayoutService.Box(id, Math.Round(x), Math.Round(y), Math.Round(el.ActualWidth), Math.Round(el.ActualHeight)));
        }
        LayoutService.Save(list);
        Log("布局已保存: " + list.Count + " 项");
    }

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
        var targets = LayoutTargets().ToList();
        foreach (var (el, id) in targets)
        {
            if (_editMode)
            {
                el.Opacity = 0.92;
                el.Cursor = Cursors.SizeAll;
                el.PreviewMouseLeftButtonDown += EditMouseDown;
                el.PreviewMouseMove += EditMouseMove;
                el.PreviewMouseLeftButtonUp += EditMouseUp;
                if (el is Border b) b.BorderBrush = Brushes.DeepSkyBlue;
                AddResizeHandle(el);
            }
            else
            {
                el.Opacity = 1;
                el.Cursor = Cursors.Arrow;
                el.PreviewMouseLeftButtonDown -= EditMouseDown;
                el.PreviewMouseMove -= EditMouseMove;
                el.PreviewMouseLeftButtonUp -= EditMouseUp;
                RemoveResizeHandle(el);
            }
        }
        if (!_editMode) SaveCurrentLayout();
        Log(_editMode ? "编辑模式：拖动组件调整位置，Ctrl+E 退出并保存" : "已退出编辑模式，布局已保存");
    }

    private void AddResizeHandle(FrameworkElement target)
    {
        if (_handles.ContainsKey(target) || HomeCanvas == null) return;
        var t = new Thumb
        {
            Width = 13, Height = 13,
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(1),
            Cursor = Cursors.SizeNWSE
        };
        double x = Canvas.GetLeft(target); if (double.IsNaN(x)) x = 0;
        double y = Canvas.GetTop(target); if (double.IsNaN(y)) y = 0;
        Canvas.SetLeft(t, x + target.Width - 7);
        Canvas.SetTop(t, y + target.Height - 7);
        Canvas.SetZIndex(t, 100);
        t.DragDelta += (_, e) =>
        {
            target.Width = Math.Max(60, target.Width + e.HorizontalChange);
            target.Height = Math.Max(30, target.Height + e.VerticalChange);
            Canvas.SetLeft(t, Canvas.GetLeft(target) + target.Width - 7);
            Canvas.SetTop(t, Canvas.GetTop(target) + target.Height - 7);
        };
        HomeCanvas.Children.Add(t);
        _handles[target] = t;
    }

    private void RemoveResizeHandle(FrameworkElement target)
    {
        if (_handles.TryGetValue(target, out var t))
        {
            HomeCanvas.Children.Remove(t);
            _handles.Remove(target);
        }
    }

    private void EditMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_editMode || sender is not FrameworkElement el) return;
        _dragEl = el;
        _dragStart = e.GetPosition(this);
        _startLeft = Canvas.GetLeft(el); if (double.IsNaN(_startLeft)) _startLeft = 0;
        _startTop = Canvas.GetTop(el); if (double.IsNaN(_startTop)) _startTop = 0;
        el.CaptureMouse();
        e.Handled = true;
    }

    private void EditMouseMove(object sender, MouseEventArgs e)
    {
        if (!_editMode || _dragEl == null) return;
        var p = e.GetPosition(this);
        if (_dragEl.Parent is Canvas)
        {
            Canvas.SetLeft(_dragEl, _startLeft + (p.X - _dragStart.X));
            Canvas.SetTop(_dragEl, _startTop + (p.Y - _dragStart.Y));
        }
    }

    private void EditMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragEl != null) { _dragEl.ReleaseMouseCapture(); _dragEl = null; }
    }

    // ===== 窗口按钮 =====
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ToggleMax(); return; }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) => ToggleMax();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ToggleMax() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}