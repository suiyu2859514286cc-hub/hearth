using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BlockLauncher;

public partial class VersionSettingsWindow : Window
{
    private readonly string _versionId;
    private string _page = "overview";



    public VersionSettingsWindow(string versionId, string initialPage = "overview")
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
        _versionId = versionId;
        TitleText.Text = "版本设置 - " + versionId;
        ShowPage(initialPage);

        // 无边框窗口的 CenterScreen 有时不生效，渲染完再按工作区居中一次
        ContentRendered += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + Math.Max(0, (wa.Width - ActualWidth) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - ActualHeight) / 2);
        };
    }

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    // ===== 元数据（描述/收藏/内存/Java 这些，统一交给 VersionMeta）=====

    private string Get(string key, string fallback = "") => VersionMeta.Get(_versionId, key, fallback);

    private void Set(string key, string value) => VersionMeta.Set(_versionId, key, value);

    // ===== 导航 =====

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) ShowPage(tag);
    }

    private void ShowPage(string page)
    {
        _page = page;
        foreach (var b in new[] { NavOverview, NavSettings, NavModify, NavMods, NavExport })
        {
            if (b == null) continue;
            var on = (string)b.Tag == page;
            b.Background = on ? new SolidColorBrush(Color.FromArgb(0x4D, 0x3B, 0x82, 0xF6)) : Brushes.Transparent;
            b.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6));
        }

        PageHost.Children.Clear();
        switch (page)
        {
            case "settings": ShowSettings(); break;
            case "modify": ShowModify(); break;
            case "mods": ShowMods(); break;
            case "export": ShowExport(); break;
            default: ShowOverview(); break;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
        if (!string.IsNullOrEmpty(title))
            stack.Children.Add(Text(title, 12, "#B9C9DC", true));
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

    private Grid Field(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = Text(label, 11, "#B9C9DC");
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>
    /// 分段按钮。原本用 ComboBox，但它在深色主题下白底白字改不干净，干脆不用。
    /// </summary>
    private (StackPanel Panel, Func<int> Selected) Segmented(string[] options, int current)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var buttons = new List<Button>();
        var state = new[] { current };

        void Refresh()
        {
            for (var j = 0; j < buttons.Count; j++)
            {
                var on = j == state[0];
                buttons[j].Background = on
                    ? new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB))
                    : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
                buttons[j].Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6));
            }
        }

        for (var i = 0; i < options.Length; i++)
        {
            var idx = i;
            var b = new Button
            {
                Content = options[i],
                Style = (Style)FindResource("Btn"),
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(14, 6, 14, 6),
                FontSize = 11
            };
            b.Click += (_, _) => { state[0] = idx; Refresh(); };
            buttons.Add(b);
            panel.Children.Add(b);
        }

        Refresh();
        return (panel, () => state[0]);
    }

    private Button Btn(string content, double width = 0, Action? onClick = null, string bg = "#22FFFFFF")
    {
        var b = new Button
        {
            Content = content,
            Style = (Style)FindResource("Btn"),
            Background = (Brush)new BrushConverter().ConvertFromString(bg)!,
            Margin = new Thickness(0, 0, 8, 0)
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

    // ===== 概览 =====

    private void ShowOverview()
    {
        var mc = VersionService.McDir;
        var versionDir = Path.Combine(mc, "versions", _versionId);
        var gameDir = VersionMeta.GameDir(_versionId);   // 隔离时是版本自己的文件夹
        var (java, loader) = DescribeVersion(mc, _versionId);

        // 头部：图标 + 版本名
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 46, Height = 46, CornerRadius = new CornerRadius(9),
            Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x1F, 0x3D), Color.FromRgb(0x3D, 0x2A, 0x55), 45)
        };
        iconBorder.Child = new TextBlock
        {
            Text = "⛏", FontSize = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(iconBorder);

        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Text(_versionId, 15, "#FFFFFF", true));
        info.Children.Add(Text(loader + " · Java " + java, 10.5, "#8FA0B8"));
        Grid.SetColumn(info, 1);
        head.Children.Add(info);

        PageHost.Children.Add(Card(null, head));

        // 个性化
        var desc = Get("desc");
        var descBox = new TextBox
        {
            Text = desc,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 11
        };

        var personalize = new StackPanel();
        personalize.Children.Add(Field("版本描述", descBox));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Btn("保存描述", 100, () =>
        {
            Set("desc", descBox.Text);
            DarkDialog.Toast(this, "描述已保存");
        }));
        actions.Children.Add(Btn("加入收藏夹", 100, () =>
        {
            Set("fav", Get("fav") == "1" ? "0" : "1");
            DarkDialog.Toast(this, Get("fav") == "1" ? "已加入收藏夹" : "已取消收藏");
        }));
        personalize.Children.Add(actions);

        PageHost.Children.Add(Card("个性化", personalize));

        // 快捷方式
        var shortcuts = new UniformGrid { Columns = 4, Margin = new Thickness(0, 4, 0, 0) };
        shortcuts.Children.Add(Btn("版本文件夹", 0, () => OpenFolder(versionDir)));
        shortcuts.Children.Add(Btn("存档文件夹", 0, () => OpenFolder(Path.Combine(gameDir, "saves"))));
        shortcuts.Children.Add(Btn("Mod 文件夹", 0, () => OpenFolder(Path.Combine(gameDir, "mods"))));
        shortcuts.Children.Add(Btn("截图文件夹", 0, () => OpenFolder(Path.Combine(gameDir, "screenshots"))));
        PageHost.Children.Add(Card("快捷方式", shortcuts));

        // 高级管理
        var advanced = new UniformGrid { Columns = 3, Margin = new Thickness(0, 4, 0, 0) };
        advanced.Children.Add(Btn("导出启动脚本", 0, () => ExportLaunchScript(mc, versionDir)));
        advanced.Children.Add(Btn("补全文件", 0, () =>
            DarkDialog.Toast(this, "补全文件需要重新走一遍下载，版本列表里重新安装即可")));
        advanced.Children.Add(Btn("删除版本", 0, () => DeleteVersion(versionDir), "#33EF4444"));
        PageHost.Children.Add(Card("高级管理", advanced));
    }

    private void ExportLaunchScript(string mc, string versionDir)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存启动脚本",
                FileName = _versionId + ".bat",
                Filter = "批处理 (*.bat)|*.bat"
            };
            if (dialog.ShowDialog(this) != true) return;

            var java = LaunchService.FindJava(17) ?? "javaw";
            var script = "@echo off\r\n" +
                         "cd /d \"" + mc + "\"\r\n" +
                         "\"" + java + "\" -Xmx4096M -Djava.library.path=\"" +
                         Path.Combine(versionDir, "natives") + "\" -cp \"" +
                         "请用启动器启动（classpath 太长，这里只做占位）\" net.minecraft.client.main.Main\r\n" +
                         "pause\r\n";
            File.WriteAllText(dialog.FileName, script);
            DarkDialog.Toast(this, "脚本已保存（classpath 需要启动器拼，脚本里是占位）");
        }
        catch (Exception ex)
        {
            DarkDialog.Toast(this, "导出失败：" + ex.Message);
        }
    }

    private void DeleteVersion(string versionDir)
    {
        if (!DarkDialog.Confirm(this, "删除版本", "确定删除版本 " + _versionId + " 吗？只删这个版本的文件夹。", "删除", "取消", true)) return;

        try
        {
            Directory.Delete(versionDir, true);
            VersionMeta.Remove(_versionId);
            Close();
        }
        catch (Exception ex)
        {
            DarkDialog.Toast(this, "删除失败：" + ex.Message);
        }
    }

    private static (string Java, string Loader) DescribeVersion(string mc, string id)
    {
        var jsonPath = Path.Combine(mc, "versions", id, id + ".json");
        var java = "17";
        var loader = "原版";
        try
        {
            var text = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("javaVersion", out var jv) && jv.TryGetProperty("majorVersion", out var jm))
                java = jm.GetInt32().ToString();
            var lower = id.ToLowerInvariant();
            if (lower.Contains("fabric-loader")) loader = "Fabric";
            else if (text.Contains("neoforged", StringComparison.OrdinalIgnoreCase)) loader = "NeoForge";
            else if (lower.Contains("-forge-")) loader = "Forge";
        }
        catch { }
        return (java, loader);
    }

    // ===== 设置 =====

    private void ShowSettings()
    {
        var settings = new StackPanel();
        settings.Children.Add(Text("这些设置只对这个版本生效", 10.5, "#7FB0FF"));
        settings.Children.Add(new Border { Height = 10 });

        var isoRow = new StackPanel { Orientation = Orientation.Horizontal };
        var isoToggle = new ToggleSwitch(VersionMeta.IsIsolated(_versionId));
        var isoLabel = Text(VersionMeta.IsIsolated(_versionId) ? "开（存到版本文件夹）" : "关（共用 .minecraft）",
            10.5, "#B9C9DC");
        isoLabel.VerticalAlignment = VerticalAlignment.Center;
        isoLabel.Margin = new Thickness(10, 0, 0, 0);
        isoToggle.Toggled += on =>
        {
            Set("isolation", on ? "1" : "0");
            isoLabel.Text = on ? "开（存到版本文件夹）" : "关（共用 .minecraft）";
        };
        isoRow.Children.Add(isoToggle);
        isoRow.Children.Add(isoLabel);
        settings.Children.Add(Field("版本隔离", isoRow));

        var titleBox = new TextBox
        {
            Text = Get("windowTitle"),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, Padding = new Thickness(8, 6, 8, 6), FontSize = 11
        };
        settings.Children.Add(Field("游戏窗口标题", titleBox));

        var javaPick = Get("java") switch { "8" => 1, "17" => 2, "21" => 3, _ => 0 };
        var javaChoice = UiKit.ChoiceGroup(new[] { "自动", "Java 8", "Java 17", "Java 21" },
            javaPick, i => javaPick = i);
        settings.Children.Add(Field("Java", javaChoice));

        // 内存：跟随全局 / 自动配置 / 自定义
        var savedMode = Get("memoryMode", Get("memory", "") != "" ? "custom" : "global");
        var modeIndex = savedMode switch { "auto" => 1, "custom" => 2, _ => 0 };
        var customValue = int.TryParse(Get("memoryValue", Get("memory", "4096")), out var cv) && cv > 0 ? cv : 4096;

        var sliderText = Text(customValue + " MB", 11, "#E8F0FA");
        sliderText.VerticalAlignment = VerticalAlignment.Center;
        sliderText.Margin = new Thickness(12, 0, 0, 0);
        var slider = new Slider
        {
            Minimum = 1024,
            Maximum = 16384,
            Value = customValue,
            TickFrequency = 512,
            IsSnapToTickEnabled = true,
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        slider.ValueChanged += (_, e) => sliderText.Text = (int)e.NewValue + " MB";

        var sliderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(118, 2, 0, 6),
            Visibility = modeIndex == 2 ? Visibility.Visible : Visibility.Collapsed
        };
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(sliderText);

        var memChoice = UiKit.ChoiceGroup(
            new[]
            {
                "跟随全局（" + AppSettings.DefaultMemory + " MB）",
                "自动配置（" + MemoryUtil.Auto() + " MB）",
                "自定义"
            },
            modeIndex,
            i =>
            {
                modeIndex = i;
                sliderRow.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
            });

        settings.Children.Add(Field("内存", memChoice));
        settings.Children.Add(sliderRow);

        var loginPick = Get("login") == "offline" ? 1 : 0;
        var loginChoice = UiKit.ChoiceGroup(new[] { "正版或离线", "仅离线" },
            loginPick, i => loginPick = i);
        settings.Children.Add(Field("登录方式", loginChoice));

        var serverBox = new TextBox
        {
            Text = Get("server"),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White, Padding = new Thickness(8, 6, 8, 6), FontSize = 11
        };
        settings.Children.Add(Field("自动进入服务器", serverBox));

        var save = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        save.Children.Add(Btn("保存设置", 110, () =>
        {
            Set("windowTitle", titleBox.Text);
            Set("java", javaPick switch { 1 => "8", 2 => "17", 3 => "21", _ => "auto" });
            Set("memoryMode", modeIndex switch { 1 => "auto", 2 => "custom", _ => "global" });
            Set("memoryValue", ((int)slider.Value).ToString());
            Set("login", loginPick == 1 ? "offline" : "auto");
            Set("server", serverBox.Text);
            DarkDialog.Toast(this, "设置已保存");
        }, "#CC2563EB"));
        settings.Children.Add(save);

        PageHost.Children.Add(Card("启动设置", settings));

        // Java 提示
        var javaMajor = DescribeVersion(VersionService.McDir, _versionId).Java;
        var found = LaunchService.FindJava(int.Parse(javaMajor));
        if (found == null)
        {
            PageHost.Children.Add(Card(null, new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Child = Text("本机还没有 Java " + javaMajor + "，启动时会自动下载", 10.5, "#FFD8A0")
            }));
        }
    }

    // ===== 修改（组件列表）=====

    // 待安装的加载器（选了版本但还没装）
    private (string Loader, string Version)? _pendingLoader;
    private bool _wantFabricApi;

    private void ShowModify()
    {
        var mc = VersionService.McDir;

        // 这里会被反复调用（选了版本要刷新），先清空，否则卡片会一层层叠上去
        PageHost.Children.Clear();

        var list = new StackPanel();

        var rows = new List<(string Name, string Detail, bool Installed, string? RemoveId)>();

        rows.Add(("Minecraft", _versionId.Replace("-forge-", "").Replace("-neoforge-", ""), true, null));

        var lower = _versionId.ToLowerInvariant();
        var isFabric = lower.Contains("fabric-loader");
        var isForge = lower.Contains("-forge-") && !IsNeoForge(mc, _versionId);
        var isNeoForge = IsNeoForge(mc, _versionId);

        // 已经选了某个加载器，就别再让选不兼容的
        var pendingName = _pendingLoader?.Loader;

        string StateOf(string name, bool installed, bool conflict, string conflictWith, string pendingText)
        {
            if (installed) return "已安装";
            if (conflict) return "与 " + conflictWith + " 不兼容";
            if (pendingName == name) return "已选 " + _pendingLoader!.Value.Version + "，待安装";
            if (pendingName != null) return "与 " + pendingName + " 不兼容";
            return "可以添加";
        }

        rows.Add(("Forge",
            StateOf("Forge", isForge, isFabric || isNeoForge, isFabric ? "Fabric" : "NeoForge", ""),
            isForge, isForge ? _versionId : null));
        rows.Add(("NeoForge",
            StateOf("NeoForge", isNeoForge, isFabric || isForge, isFabric ? "Fabric" : "Forge", ""),
            isNeoForge, isNeoForge ? _versionId : null));
        rows.Add(("Fabric",
            StateOf("Fabric", isFabric, isForge || isNeoForge, "Forge", ""),
            isFabric, isFabric ? _versionId : null));
        var optiFineState = isFabric
            ? "与 Fabric 不兼容"
            : (pendingName != null ? "与 " + pendingName + " 不兼容" : "可以添加");

        rows.Add(("OptiFine", optiFineState, false, null));

        foreach (var (name, detail, installed, removeId) in rows)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Text(name, 12, "#E8F0FA", true));
            var d = Text("  " + detail, 10.5, installed ? "#9CC8FF" : "#6B7C92");
            d.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(d);
            grid.Children.Add(left);

            UIElement? action = null;

            if (removeId != null)
            {
                var del = Btn("删除", 64, () =>
                {
                    if (!DarkDialog.Confirm(this, "删除加载器", "删除这个加载器？只删它的版本文件夹。", "删除", "取消", true)) return;
                    try
                    {
                        Directory.Delete(Path.Combine(mc, "versions", removeId), true);
                        Close();
                    }
                    catch (Exception ex) { DarkDialog.Toast(this, ex.Message); }
                }, "#33EF4444");
                action = del;
            }
            else if (detail == "可以添加")
            {
                var addLoader = name;
                action = Btn("添加", 64, () =>
                {
                    if (addLoader == "OptiFine") PickOptiFineVersion();
                    else PickLoaderVersion(addLoader);
                }, "#333B82F6");
            }
            else if (detail.StartsWith("已选", StringComparison.Ordinal))
            {
                action = Btn("移除", 64, () =>
                {
                    _pendingLoader = null;
                    _wantFabricApi = false;
                    ShowModify();
                }, "#33FFFFFF");
            }

            if (action is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(fe, 1);
                grid.Children.Add(fe);
            }

            list.Children.Add(grid);
        }

        // 选了 Fabric 就顺便问要不要 Fabric API
        if (_pendingLoader?.Loader == "Fabric")
        {
            var apiRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var check = new CheckBox
            {
                Content = "同时安装 Fabric API（大部分 Fabric 模组依赖它）",
                IsChecked = _wantFabricApi,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0)),
                FontSize = 10.5,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            check.Checked += (_, _) => _wantFabricApi = true;
            check.Unchecked += (_, _) => _wantFabricApi = false;
            apiRow.Children.Add(check);
            list.Children.Add(apiRow);
        }

        if (_pendingLoader != null)
        {
            var go = Btn("开始安装", 96, () => InstallPendingAsync(), "#CC22C55E");
            go.HorizontalAlignment = HorizontalAlignment.Left;
            go.Margin = new Thickness(0, 10, 0, 0);
            list.Children.Add(go);
        }
        else
        {
            list.Children.Add(Text("先选一个要加的加载器，选好版本后再点开始安装（会新建一个版本，原版保持不变）", 9.5, "#6B7C92", false));
        }

        PageHost.Children.Add(Card("组件", list));

        // 已经装了 Fabric 的版本，单独给一张 Fabric API 卡片
        if (isFabric && _pendingLoader == null) PageHost.Children.Add(BuildFabricApiCard());
    }

    /// <summary>点「添加」：拉这个加载器的版本列表让用户选，选完不装，先记下来</summary>
    private async void PickLoaderVersion(string loader)
    {
        var baseVersion = BaseGameVersion();
        if (baseVersion == null)
        {
            DarkDialog.Info(this, "选不了", "读不出这个版本对应的 Minecraft 版本号");
            return;
        }

        try
        {
            var versions = loader switch
            {
                "Fabric" => await LoaderService.GetFabricVersionsAsync(baseVersion),
                "NeoForge" => await LoaderService.GetNeoForgeVersionsAsync(baseVersion),
                _ => await LoaderService.GetForgeVersionsAsync(baseVersion)
            };

            if (versions.Count == 0)
            {
                DarkDialog.Info(this, "没有可用版本", "在 " + baseVersion + " 上找不到 " + loader + " 的版本");
                return;
            }

            var names = versions.Select(v => v.Version).ToArray();
            var picked = DarkDialog.Pick(this, "选择 " + loader + " 版本（" + baseVersion + "）", names);
            if (picked < 0) return;

            _pendingLoader = (loader, names[picked]);
            _wantFabricApi = loader == "Fabric";
            ShowModify();
        }
        catch (Exception ex)
        {
            DarkDialog.Info(this, "获取版本列表失败", ex.Message);
        }
    }

    /// <summary>选 OptiFine 版本并下载到 mods（它是作为模组用的，不是独立加载器）</summary>
    private async void PickOptiFineVersion()
    {
        var baseVersion = BaseGameVersion();
        if (baseVersion == null) return;

        var versions = await LoaderService.GetOptiFineVersionsAsync(baseVersion);
        if (versions.Count == 0)
        {
            DarkDialog.Info(this, "没有可用版本", "在 " + baseVersion + " 上找不到 OptiFine");
            return;
        }

        var names = versions.Select(v => v.Display + "（" + v.FileName + "）").ToArray();
        var picked = DarkDialog.Pick(this, "选择 OptiFine 版本（" + baseVersion + "）", names);
        if (picked < 0) return;

        var modsDir = Path.Combine(VersionMeta.GameDir(_versionId), "mods");
        Directory.CreateDirectory(modsDir);

        // 原版没有模组加载器，OptiFine 放 mods 不会生效
        var hasLoader = _versionId.Contains("forge") || _versionId.Contains("fabric");
        if (!hasLoader)
        {
            var go = DarkDialog.Confirm(this, "先装 Forge",
                "OptiFine 要跟 Forge 一起用。要不要先给这个版本加 Forge？\n\n（点确定会跳到加载器安装）",
                "去装 Forge", "先放着", false);
            if (go) PickLoaderVersion("Forge");
            return;
        }

        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.BeginTask("下载 OptiFine " + versions[picked].Display, "准备中…");

        var progress = new Progress<(string Msg, double Pct)>(p => main?.UpdateTask(p.Pct, p.Msg));

        try
        {
            await LoaderService.DownloadOptiFineAsync(versions[picked], modsDir, progress);
            main?.EndTask(true, "OptiFine 下载完成");
            DarkDialog.Toast(this, "OptiFine 已放进 mods");
            ShowModify();
        }
        catch (Exception ex)
        {
            main?.EndTask(false, "下载失败：" + ex.Message);
            DarkDialog.Info(this, "下载失败", ex.Message);
        }
    }

    /// <summary>开始安装选好的加载器（可选带 Fabric API）</summary>
    private async void InstallPendingAsync()
    {
        if (_pendingLoader == null) return;

        var (loader, version) = _pendingLoader.Value;
        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.BeginTask("安装 " + loader + " " + version, "准备中…");

        var progress = new Progress<(string Msg, double Pct)>(p => main?.UpdateTask(p.Pct, p.Msg));

        try
        {
            var id = loader switch
            {
                "Fabric" => await LoaderService.InstallFabricAsync(_versionId, version, progress),
                "NeoForge" => await LoaderService.InstallNeoForgeAsync(_versionId, version, progress),
                _ => await LoaderService.InstallForgeAsync(_versionId, version, progress)
            };

            if (_wantFabricApi && loader == "Fabric")
            {
                var modsDir = Path.Combine(VersionMeta.GameDir(id), "mods");
                await DownloadFabricApiAsync(id, modsDir, main);
            }

            main?.EndTask(true, loader + " 装好了：" + id);
            _pendingLoader = null;
            _wantFabricApi = false;
            ShowModify();
        }
        catch (Exception ex)
        {
            main?.EndTask(false, "安装失败：" + ex.Message);
            DarkDialog.Info(this, "安装失败", ex.Message);
        }
    }

    /// <summary>从 Modrinth 下载对应版本的 Fabric API</summary>
    private async System.Threading.Tasks.Task DownloadFabricApiAsync(string versionId, string modsDir, MainWindow? main)
    {
        var baseVersion = BaseGameVersion();
        if (baseVersion == null) return;

        var files = await ModrinthService.GetFilesAsync("fabric-api");
        var hit = files.FirstOrDefault(x =>
            x.Loaders.Contains("fabric") && x.GameVersions.Contains(baseVersion));

        if (hit == null) return;

        Directory.CreateDirectory(modsDir);
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            var pct = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            main?.UpdateTask(pct, "Fabric API " + (p.Done / 1024.0 / 1024.0).ToString("0.0") + " MB");
        });

        await ModrinthService.DownloadAsync(hit.Url, modsDir, hit.FileName, progress);
    }

    /// <summary>Fabric API 卡片：检测 mods 里有没有，没有就给一键安装</summary>
    private Border BuildFabricApiCard()
    {
        var modsDir = Path.Combine(VersionMeta.GameDir(_versionId), "mods");
        var installed = Directory.Exists(modsDir) &&
                        Directory.GetFiles(modsDir, "fabric-api*.jar").Length > 0;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(Text("Fabric API", 12, "#E8F0FA", true));
        var state = Text("  " + (installed ? "已安装" : "很多 Fabric 模组依赖它"), 10.5, installed ? "#9CC8FF" : "#6B7C92");
        state.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(state);
        grid.Children.Add(left);

        var btn = Btn(installed ? "已安装" : "安装", 64, async () =>
        {
            if (installed) return;
            await InstallFabricApiAsync(modsDir);
        }, "#333B82F6");
        btn.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(btn, 1);
        grid.Children.Add(btn);

        return Card("前置模组", grid);
    }

    private async System.Threading.Tasks.Task InstallFabricApiAsync(string modsDir)
    {
        var baseVersion = BaseGameVersion();
        if (baseVersion == null)
        {
            DarkDialog.Info(this, "装不了", "读不出这个版本对应的 Minecraft 版本号");
            return;
        }

        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.BeginTask("安装 Fabric API", "正在查最新版本…");

        try
        {
            var files = await ModrinthService.GetFilesAsync("fabric-api");
            var hit = files.FirstOrDefault(x =>
                x.Loaders.Contains("fabric") && x.GameVersions.Contains(baseVersion));

            if (hit == null)
            {
                main?.EndTask(false, "没找到适配 " + baseVersion + " 的 Fabric API");
                DarkDialog.Info(this, "没找到", "Modrinth 上没有适配 " + baseVersion + " 的 Fabric API");
                return;
            }

            Directory.CreateDirectory(modsDir);
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                var pct = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                main?.UpdateTask(pct, p.Done / 1024.0 / 1024.0 + " MB");
            });

            await ModrinthService.DownloadAsync(hit.Url, modsDir, hit.FileName, progress);
            main?.EndTask(true, "Fabric API 装好了");
            DarkDialog.Toast(this, "Fabric API 已下载到 mods");
            ShowModify();
        }
        catch (Exception ex)
        {
            main?.EndTask(false, "安装失败：" + ex.Message);
            DarkDialog.Info(this, "安装失败", ex.Message);
        }
    }

    /// <summary>这个版本对应的原版版本号：加载器版本看 inheritsFrom，原版版本就是它自己</summary>
    private string? BaseGameVersion()
    {
        try
        {
            var path = Path.Combine(VersionService.McDir, "versions", _versionId, _versionId + ".json");
            if (!File.Exists(path)) return _versionId;

            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            var parent = node?["inheritsFrom"]?.GetValue<string>();

            // 没有 inheritsFrom = 原版，基础版本就是它自己
            return string.IsNullOrEmpty(parent) ? _versionId : parent;
        }
        catch { return _versionId; }
    }

    private static bool IsNeoForge(string mc, string id)
    {
        try
        {
            var p = Path.Combine(mc, "versions", id, id + ".json");
            return File.Exists(p) && File.ReadAllText(p).Contains("neoforged", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ===== Mod 管理 =====

    private void ShowMods()
    {
        var gameDir = VersionMeta.GameDir(_versionId);
        var modsDir = Path.Combine(gameDir, "mods");

        var top = new StackPanel();
        var search = new TextBox
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = Brushes.White,
            Padding = new Thickness(10, 7, 10, 7),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 10)
        };
        // 用 Tag 模拟占位符
        search.Tag = "搜索 Mod 名称 / 文件名";
        top.Children.Add(search);

        var buttons = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(Btn("打开文件夹", 0, () => OpenFolder(modsDir)));
        buttons.Children.Add(Btn("从文件安装", 0, () => InstallModFromFile(modsDir)));
        buttons.Children.Add(Btn("下载新 Mod", 0, () =>
            DarkDialog.Toast(this, "模组下载还没接（Modrinth 搜索需要单独做）")));
        top.Children.Add(buttons);

        var listHost = new StackPanel();
        top.Children.Add(listHost);
        PageHost.Children.Add(Card(null, top));

        void Render()
        {
            listHost.Children.Clear();
            var files = new List<string>();
            try { if (Directory.Exists(modsDir)) files = Directory.GetFiles(modsDir, "*.jar").ToList(); }
            catch { }

            var keyword = search.Text.Trim();
            if (!string.IsNullOrEmpty(keyword))
                files = files.Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();

            listHost.Children.Add(Text("全部（" + files.Count + "）", 11, "#B9C9DC", true));

            if (files.Count == 0)
            {
                listHost.Children.Add(Text("这个文件夹里还没有 Mod", 10.5, "#6B7C92"));
                return;
            }

            foreach (var file in files.Take(200))
            {
                var (name, version, desc) = ReadModMeta(file);
                var row = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                var title = new StackPanel { Orientation = Orientation.Horizontal };
                title.Children.Add(Text(name, 11.5, "#E8F0FA", true));
                if (!string.IsNullOrEmpty(version))
                    title.Children.Add(Text("  " + version, 10, "#8FA0B8"));
                row.Children.Add(title);
                row.Children.Add(Text(desc, 9.5, "#6B7C92"));
                row.Children.Add(Text(Path.GetFileName(file), 9, "#54657A"));
                listHost.Children.Add(row);
            }
        }

        search.TextChanged += (_, _) => Render();
        Render();
    }

    private void InstallModFromFile(string modsDir)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Mod 文件",
            Filter = "Mod (*.jar)|*.jar|所有文件 (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            Directory.CreateDirectory(modsDir);
            foreach (var file in dialog.FileNames)
                File.Copy(file, Path.Combine(modsDir, Path.GetFileName(file)), true);
            DarkDialog.Toast(this, "已安装 " + dialog.FileNames.Length + " 个 Mod");
            ShowPage("mods");
        }
        catch (Exception ex)
        {
            DarkDialog.Toast(this, "安装失败：" + ex.Message);
        }
    }

    /// <summary>读 jar 里的模组信息：Fabric 读 fabric.mod.json，Forge 读 mods.toml</summary>
    private static (string Name, string Version, string Desc) ReadModMeta(string jar)
    {
        var fallback = Path.GetFileNameWithoutExtension(jar);
        try
        {
            using var zip = ZipFile.OpenRead(jar);

            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                using var stream = fabric.Open();
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;
                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? fallback : fallback;
                var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                var desc = root.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                return (name, version, desc.Length > 120 ? desc[..120] : desc);
            }

            var toml = zip.GetEntry("META-INF/mods.toml");
            if (toml != null)
            {
                using var reader = new StreamReader(toml.Open());
                var text = reader.ReadToEnd();
                var name = System.Text.RegularExpressions.Regex.Match(text, "displayName\\s*=\\s*\"([^\"]+)\"");
                var version = System.Text.RegularExpressions.Regex.Match(text, "^\\s*version\\s*=\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.Multiline);
                var desc = System.Text.RegularExpressions.Regex.Match(text, "description\\s*=\\s*\"([^\"]+)\"");
                return (
                    name.Success ? name.Groups[1].Value : fallback,
                    version.Success ? version.Groups[1].Value : "",
                    desc.Success ? desc.Groups[1].Value : "");
            }
        }
        catch { }
        return (fallback, "", "");
    }

    // ===== 导出 =====

    /// <summary>0 = CurseForge，1 = Modrinth，2 = 原始 zip</summary>
    private int _exportFormat;

    private void ShowExport()
    {
        var mc = VersionService.McDir;
        var items = new (string Label, string Folder, bool Default)[]
        {
            ("Mod", "mods", true),
            ("配置文件", "config", true),
            ("资源包", "resourcepacks", false),
            ("光影包", "shaderpacks", false),
            ("截图", "screenshots", false),
            ("单人游戏存档", "saves", false),
        };

        var panel = new StackPanel();
        var checks = new List<(CheckBox Box, string Folder)>();

        foreach (var (label, folder, def) in items)
        {
            var cb = new CheckBox
            {
                Content = label + "（" + CountFiles(Path.Combine(mc, folder)) + " 个文件）",
                IsChecked = def,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA)),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 5)
            };
            checks.Add((cb, folder));
            panel.Children.Add(cb);
        }

        panel.Children.Add(Text("导出格式", 10.5, "#B9C9DC", true));

        _exportFormat = 0;
        var formatChoice = UiKit.ChoiceGroup(
            new[] { "CurseForge", "Modrinth", "原始 zip" },
            _exportFormat,
            i => _exportFormat = i);
        formatChoice.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(formatChoice);

        var hint = Text("", 9.5, "#6B7C92");
        hint.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(hint);

        void UpdateHint()
        {
            hint.Text = _exportFormat switch
            {
                1 => "Modrinth 格式：modrinth.index.json + overrides，能被 Modrinth App 直接导入",
                2 => "原始 zip：只打包选中的文件夹，不写任何清单，适合自己备份",
                _ => "CurseForge 格式：manifest.json + overrides，能被 CurseForge App 直接导入"
            };
        }

        // ChoiceGroup 的回调只改字段，这里补一次 UI 刷新
        foreach (var child in formatChoice.Children.OfType<StackPanel>())
        {
            child.MouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateHint));
        }
        UpdateHint();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Btn("开始导出", 120, () => DoExport(mc, checks), "#CC2563EB"));
        panel.Children.Add(actions);

        PageHost.Children.Add(Card("导出整合包", panel));
    }

    private static int CountFiles(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length : 0; }
        catch { return 0; }
    }

    /// <summary>从版本 ID 里拆出：MC 版本、加载器、加载器版本</summary>
    private (string McVersion, string LoaderKey, string LoaderVersion) ParseExportVersion()
    {
        var id = _versionId;
        var lower = id.ToLowerInvariant();

        if (lower.Contains("fabric-loader-"))
        {
            var rest = id[(lower.IndexOf("fabric-loader-", StringComparison.Ordinal) + "fabric-loader-".Length)..];
            var parts = rest.Split('-');
            if (parts.Length >= 2) return (parts[1], "fabric-loader", parts[0]);
            return (rest, "fabric-loader", "");
        }

        if (lower.Contains("-neoforge-"))
        {
            var idx = lower.IndexOf("-neoforge-", StringComparison.Ordinal);
            return (id[..idx], "neoforge", id[(idx + "-neoforge-".Length)..]);
        }

        if (lower.Contains("-forge-"))
        {
            var idx = lower.IndexOf("-forge-", StringComparison.Ordinal);
            return (id[..idx], "forge", id[(idx + "-forge-".Length)..]);
        }

        return (id, "", "");
    }

    private void DoExport(string mc, List<(CheckBox Box, string Folder)> checks)
    {
        var suffix = _exportFormat switch { 1 => "-modrinth", 2 => "-files", _ => "" };

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = _exportFormat switch
            {
                1 => "导出 Modrinth 整合包",
                2 => "导出文件包",
                _ => "导出 CurseForge 整合包"
            },
            FileName = _versionId + suffix + ".zip",
            Filter = "整合包 (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
            using var zip = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);

            var (mcVersion, loaderKey, loaderVer) = ParseExportVersion();
            var options = new JsonSerializerOptions { WriteIndented = true };

            if (_exportFormat == 0)
            {
                // CurseForge：manifest.json
                var loaderId = loaderKey switch
                {
                    "fabric-loader" => "fabric-" + loaderVer,
                    "forge" => "forge-" + loaderVer,
                    "neoforge" => "neoforge-" + loaderVer,
                    _ => ""
                };

                var manifest = new
                {
                    minecraft = new
                    {
                        version = mcVersion,
                        modLoaders = string.IsNullOrEmpty(loaderId)
                            ? Array.Empty<object>()
                            : new object[] { new { id = loaderId, primary = true } }
                    },
                    manifestType = "minecraftModpack",
                    manifestVersion = 1,
                    name = _versionId,
                    version = "1.0",
                    author = "Hearth",
                    files = Array.Empty<object>(),
                    overrides = "overrides"
                };

                using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest, options));
            }
            else if (_exportFormat == 1)
            {
                // Modrinth：modrinth.index.json
                var deps = new Dictionary<string, string> { ["minecraft"] = mcVersion };
                if (!string.IsNullOrEmpty(loaderKey) && !string.IsNullOrEmpty(loaderVer))
                    deps[loaderKey] = loaderVer;

                var index = new
                {
                    formatVersion = 1,
                    game = "minecraft",
                    versionId = "1.0",
                    name = _versionId,
                    summary = "Exported from Hearth",
                    files = Array.Empty<object>(),
                    dependencies = deps
                };

                using var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open());
                writer.Write(JsonSerializer.Serialize(index, options));
            }

            // 原始 zip 直接铺在根目录，其他两种放 overrides/
            var prefix = _exportFormat == 2 ? "" : "overrides/";

            foreach (var (box, folder) in checks)
            {
                if (box.IsChecked != true) continue;
                var source = Path.Combine(mc, folder);
                if (!Directory.Exists(source)) continue;

                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(mc, file).Replace('\\', '/');
                    var e = zip.CreateEntry(prefix + rel);
                    using var input = File.OpenRead(file);
                    using var output = e.Open();
                    input.CopyTo(output);
                }
            }

            DarkDialog.Toast(this, "导出完成：" + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            DarkDialog.Toast(this, "导出失败：" + ex.Message);
        }
    }
}
