using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockLauncher;

public partial class ResourceDownloadWindow : Window
{
    private readonly ModrinthService.SearchHit _hit;
    private readonly string? _mcVersion;
    private string? _currentVersion;
    private readonly string _folder;

    private readonly List<Border> _fileRows = new();
    private List<ModrinthService.FileInfo> _files = new();
    private ModrinthService.FileInfo? _selected;
    private string _filter = "全部";
    private string _target = "version";
    private string? _customPath;

    public ResourceDownloadWindow(ModrinthService.SearchHit hit, string? mcVersion, string? currentVersion)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);

        _hit = hit;
        _mcVersion = mcVersion;
        _currentVersion = currentVersion;
        _folder = hit.ProjectType switch
        {
            "resourcepack" => "resourcepacks",
            "shader" => "shaderpacks",
            "datapack" => "datapacks",
            _ => "mods"
        };

        TitleText.Text = hit.Title + "（" + hit.Slug + "）";
        BuildHeader();
        BuildTargets();
        LoadFilesAsync();

        // 这个环境的无边框窗口居中不可靠，直接放左上角，标题栏可拖
        Left = 40;
        Top = 40;
    }

    private void BuildHeader()
    {
        NameText.Text = _hit.Title;
        DescText.Text = _hit.Description;

        var meta = new List<string>();
        if (_hit.Categories.Length > 0) meta.Add(string.Join(" · ", _hit.Categories.Take(3)));
        meta.Add("下载 " + FormatCount(_hit.Downloads));
        if (DateTime.TryParse(_hit.Updated, out var d)) meta.Add("更新 " + d.ToString("yyyy-MM-dd"));
        meta.Add("Modrinth");
        MetaText.Text = string.Join("  ·  ", meta);

        if (!string.IsNullOrEmpty(_hit.IconUrl))
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(_hit.IconUrl, UriKind.Absolute);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                IconBox.Child = new System.Windows.Controls.Image { Source = bmp, Stretch = Stretch.UniformToFill };
            }
            catch { }
        }
    }

    private static string FormatCount(long n)
    {
        if (n >= 100_000_000) return (n / 100_000_000.0).ToString("0.0") + " 亿";
        if (n >= 10_000) return (n / 10_000.0).ToString("0.0") + " 万";
        return n.ToString();
    }

    private Button? _targetBtn;

    private void BuildTargets()
    {
        _targetBtn = new Button
        {
            Style = (Style)FindResource("Btn"),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 10.5,
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6))
        };
        _targetBtn.Click += (_, _) => ShowTargetMenu();
        TargetPanel.Children.Add(_targetBtn);
        RefreshTargetLabel();
    }

    private void RefreshTargetLabel()
    {
        if (_targetBtn == null) return;
        var label = _target switch
        {
            "global" => "全局 .minecraft",
            "custom" => _customPath ?? "选文件夹…",
            _ => _currentVersion ?? "当前版本"
        };
        _targetBtn.Content = "下载到 " + label + "  ▾";
        // 原路径改到按钮文字里已经能看出来，不再另挂悬停提示
    }

    /// <summary>目标选择菜单：当前版本 / 其它已装版本 / 全局 / 浏览文件夹</summary>
    private void ShowTargetMenu()
    {
        if (_targetBtn == null) return;

        var items = new List<(string Label, Action? OnClick)>();

        void AddItem(string header, string type, string? version = null, string? path = null)
        {
            var isCurrent = _target == type &&
                            (type != "version" || version == _currentVersion) &&
                            (type != "custom" || path == _customPath);

            items.Add(((isCurrent ? "✓ " : "    ") + header, () =>
            {
                _target = type;
                if (type == "version") _currentVersion = version;
                if (type == "custom") _customPath = path;
                RefreshTargetLabel();
            }));
        }

        if (!string.IsNullOrEmpty(_currentVersion))
            AddItem(_currentVersion + "（当前）", "version", _currentVersion);

        var others = GetInstalledVersions()
            .Where(v => !string.Equals(v, _currentVersion, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var v in others) AddItem(v, "version", v);

        items.Add(("-", null));
        AddItem("全局 .minecraft", "global");

        items.Add(("-", null));
        items.Add(("浏览文件夹…", () =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择下载到哪个文件夹" };
            if (dialog.ShowDialog(this) == true)
            {
                _target = "custom";
                _customPath = dialog.FolderName;
                RefreshTargetLabel();
            }
        }));

        MenuPopup.Show(_targetBtn, items);
    }

    private static List<string> GetInstalledVersions()
    {
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions");
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetDirectories(dir)
                .Select(Path.GetFileName)
                .Where(n => n != null && File.Exists(Path.Combine(dir, n, n + ".json")))
                .Select(n => n!)
                .OrderBy(n => n)
                .ToList();
        }
        catch { return new List<string>(); }
    }

    private async void LoadFilesAsync()
    {
        GroupList.Children.Add(new TextBlock
        {
            Text = "正在获取文件列表…",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
            Margin = new Thickness(2, 6, 0, 0)
        });

        try
        {
            _files = await ModrinthService.GetFilesAsync(_hit.Id);
            GroupList.Children.Clear();

            if (_files.Count == 0)
            {
                GroupList.Children.Add(new TextBlock
                {
                    Text = "这个项目没有可下载的文件",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                    Margin = new Thickness(2, 6, 0, 0)
                });
                return;
            }

            BuildVersionFilter();
            RenderGroups();

            // 默认选中第一个兼容当前版本的
            var first = _files.FirstOrDefault(f =>
                string.IsNullOrEmpty(_mcVersion) || f.GameVersions.Contains(_mcVersion)) ?? _files[0];
            SelectFile(first);
        }
        catch (Exception ex)
        {
            GroupList.Children.Clear();
            GroupList.Children.Add(new TextBlock
            {
                Text = "获取失败：" + ex.Message,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x9F, 0x80)),
                Margin = new Thickness(2, 6, 0, 0)
            });
        }
    }

    private void BuildVersionFilter()
    {
        VersionFilter.Children.Clear();

        var versions = _files.SelectMany(f => f.GameVersions)
            .Distinct()
            .OrderByDescending(v => ParseMc(v))
            .ToList();

        VersionFilter.Children.Add(MakeFilter("全部"));
        foreach (var v in versions.Take(12))
            VersionFilter.Children.Add(MakeFilter(v));
    }

    private Button MakeFilter(string label)
    {
        var b = new Button
        {
            Content = label,
            Style = (Style)FindResource("Btn"),
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(13, 5, 13, 5),
            FontSize = 10.5,
            Tag = label
        };
        b.Click += (_, _) =>
        {
            _filter = label;
            RefreshFilters();
            RenderGroups();
        };
        return b;
    }

    private void RefreshFilters()
    {
        foreach (var child in VersionFilter.Children.OfType<Button>())
        {
            var on = (string)child.Tag == _filter;
            child.Background = on
                ? new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB))
                : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
            child.Foreground = on ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xC8, 0xD6, 0xE6));
        }
    }

    private static Version ParseMc(string v)
    {
        var clean = new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        return Version.TryParse(clean, out var parsed) ? parsed : new Version(0, 0);
    }

    private static string LoaderName(string loader) => loader.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => loader
    };

    /// <summary>按「加载器 + 游戏版本」分组，像 PCL 那样一行组一张卡片</summary>
    private void RenderGroups()
    {
        GroupList.Children.Clear();
        _fileRows.Clear();

        var pairs = new List<(string Loader, string GameVersion, ModrinthService.FileInfo File)>();
        foreach (var file in _files)
        {
            foreach (var gv in file.GameVersions)
            {
                foreach (var loader in file.Loaders.DefaultIfEmpty(""))
                    pairs.Add((LoaderName(loader), gv, file));
            }
        }

        if (_filter != "全部")
            pairs = pairs.Where(p => p.GameVersion == _filter).ToList();

        var groups = pairs
            .GroupBy(p => (p.Loader, p.GameVersion))
            .OrderByDescending(g => ParseMc(g.Key.GameVersion))
            .ThenBy(g => g.Key.Loader)
            .ToList();

        foreach (var group in groups)
        {
            GroupList.Children.Add(BuildGroup(group.Key.Loader, group.Key.GameVersion, group.Select(g => g.File).Distinct().ToList()));
        }

        if (groups.Count == 0)
            GroupList.Children.Add(new TextBlock
            {
                Text = "这个版本没有文件",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                Margin = new Thickness(2, 6, 0, 0)
            });
    }

    private Border BuildGroup(string loader, string gameVersion, List<ModrinthService.FileInfo> files)
    {
        var title = new TextBlock
        {
            Text = (string.IsNullOrEmpty(loader) ? "" : loader + " ") + gameVersion,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA))
        };

        var arrow = new TextBlock
        {
            Text = "▾",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
            Margin = new Thickness(0, 1, 8, 0)
        };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(arrow);
        headerPanel.Children.Add(title);
        headerPanel.Children.Add(new TextBlock
        {
            Text = files.Count + " 个",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
            Margin = new Thickness(8, 2, 0, 0)
        });

        var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var file in files)
        {
            var row = BuildFileRow(file);
            _fileRows.Add(row);
            content.Children.Add(row);
        }

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Cursor = Cursors.Hand,
            Child = headerPanel
        };
        header.MouseLeftButtonUp += (_, _) =>
        {
            var visible = content.Visibility == Visibility.Visible;
            content.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
            arrow.Text = visible ? "▸" : "▾";
        };

        var card = new StackPanel();
        card.Children.Add(header);
        card.Children.Add(content);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8),
            Child = card
        };
    }

    private Border BuildFileRow(ModrinthService.FileInfo file)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = file.Name,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var meta = new List<string> { file.FileName };
        if (file.Downloads > 0) meta.Add("下载 " + FormatCount(file.Downloads) + " 次");
        if (DateTime.TryParse(file.Date, out var d)) meta.Add("更新于 " + d.ToString("yyyy-MM-dd"));
        if (file.Size > 0) meta.Add((file.Size / 1024.0 / 1024.0).ToString("0.0") + " MB");

        stack.Children.Add(new TextBlock
        {
            Text = string.Join(" · ", meta),
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var row = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 8, 10, 8),
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = file
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        row.MouseLeave += (_, _) =>
        {
            if (!ReferenceEquals(row.Tag, _selected) || _selected == null)
                row.Background = Brushes.Transparent;
        };
        row.MouseLeftButtonUp += (_, _) => SelectFile(file);
        return row;
    }

    private void SelectFile(ModrinthService.FileInfo file)
    {
        _selected = file;
        DownloadBtn.IsEnabled = true;

        foreach (var row in _fileRows)
        {
            var on = ReferenceEquals(row.Tag, file);
            row.Background = on
                ? new SolidColorBrush(Color.FromArgb(0x44, 0x25, 0x63, 0xEB))
                : Brushes.Transparent;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            DarkDialog.Toast(this, "先选一个文件");
            return;
        }

        string root;
        string? directFolder = null;
        if (_target == "custom" && !string.IsNullOrEmpty(_customPath))
        {
            root = _customPath;          // 自己选的文件夹，直接放这儿
            directFolder = "";
        }
        else if (_target == "version" && !string.IsNullOrEmpty(_currentVersion))
        {
            root = VersionMeta.GameDir(_currentVersion);
        }
        else
        {
            root = VersionService.McDir;
        }

        DownloadBtn.IsEnabled = false;
        DownloadBtn.Content = "下载中…";

        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.BeginTask("下载 " + _selected.FileName, "开始下载…");

        var progress = new Progress<(long Done, long Total)>(p =>
        {
            var pct = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
            main?.ReportTaskBytes(p.Done);
            var doneMb = p.Done / 1024.0 / 1024.0;
            var totalMb = p.Total / 1024.0 / 1024.0;
            main?.UpdateTask(pct, p.Total > 0
                ? doneMb.ToString("0.0") + " / " + totalMb.ToString("0.0") + " MB"
                : doneMb.ToString("0.0") + " MB");
        });

        try
        {
            var destDir = directFolder == "" ? root : Path.Combine(root, _folder);
            await ModrinthService.DownloadAsync(_selected.Url, destDir, _selected.FileName, progress);
            main?.EndTask(true, "下载完成");
            DarkDialog.Toast(this, "已下载到 " + destDir);
            Close();
        }
        catch (Exception ex)
        {
            main?.EndTask(false, "下载失败：" + ex.Message);
            DarkDialog.Toast(this, "下载失败：" + ex.Message);
        }
        finally
        {
            DownloadBtn.IsEnabled = true;
            DownloadBtn.Content = "下载";
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
