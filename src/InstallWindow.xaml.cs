using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockLauncher;

public partial class InstallWindow : Window
{
    private readonly string _versionId;
    private CancellationTokenSource? _cts;
    private bool _running;

    /// <summary>自己主动关窗、把下载交给主窗口，不算用户取消</summary>
    private bool _selfClosed;

    /// <summary>窗口关闭后，外面靠这个判断要不要刷新已安装列表</summary>
    public bool Installed { get; private set; }

    public InstallWindow(string versionId, string sub, string? autoPick = null)
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
        _versionId = versionId;
        VersionText.Text = versionId;
        VersionTypeText.Text = sub;
        TitleText.Text = "安装 " + versionId;
        BuildLoaders();

        if (!string.IsNullOrEmpty(autoPick))
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => ShowPicker(autoPick!)));
        }
    }

    /// <summary>照 PCL 的安装页：Forge / NeoForge / Fabric / OptiFine 各一行</summary>
    private void BuildLoaders()
    {
        LoaderList.Children.Clear();
        foreach (var (name, state) in new[]
        {
            ("Forge", "可以添加"),
            ("NeoForge", "可以添加"),
            ("Fabric", "可以添加"),
            ("OptiFine", "去官网"),
        })
        {
            LoaderList.Children.Add(BuildLoaderRow(name, state));
        }
    }

    private Border BuildLoaderRow(string name, string state)
    {
        // OptiFine 官方不给直链，PCL 也是打开官网让用户自己下
        var isOptiFine = name == "OptiFine";
        var enabled = state != "无";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });

        var left = new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = enabled ? new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(left);

        var right = new TextBlock
        {
            Text = state,
            FontSize = 11,
            Foreground = enabled ? new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(15, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow,
            Child = grid
        };

        if (isOptiFine)
        {
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            row.MouseLeftButtonUp += (_, _) => OpenOptiFine();
        }
        else if (enabled)
        {
            row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
            row.MouseLeftButtonUp += (_, _) => ShowPicker(name);
        }

        return row;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        SelectView.Visibility = Visibility.Collapsed;
        ProgressView.Visibility = Visibility.Visible;
        StartAsync();
    }

    // ===== 选加载器版本 =====

    private async void ShowPicker(string loader)
    {
        try
        {
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "blocklauncher.log"),
                DateTime.Now.ToString("HH:mm:ss") + " ShowPicker: [" + loader + "]\n");
        }
        catch { }

        LoaderListPanel.Visibility = Visibility.Collapsed;
        PickerPanel.Visibility = Visibility.Visible;
        PickerTitle.Text = loader + " 版本";
        PickerList.Children.Clear();
        PickerList.Children.Add(new TextBlock
        {
            Text = "正在获取版本列表…",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
            Margin = new Thickness(2, 8, 0, 0)
        });

        try
        {
            List<LoaderService.LoaderVersion> versions = loader switch
            {
                "Fabric" => await LoaderService.GetFabricVersionsAsync(_versionId),
                "Forge" => await LoaderService.GetForgeVersionsAsync(_versionId),
                "NeoForge" => await LoaderService.GetNeoForgeVersionsAsync(_versionId),
                _ => new List<LoaderService.LoaderVersion>()
            };

            PickerList.Children.Clear();
            if (versions.Count == 0)
            {
                PickerList.Children.Add(new TextBlock
                {
                    Text = "这个版本没有可用的 " + loader,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                    Margin = new Thickness(2, 8, 0, 0)
                });
                return;
            }

            foreach (var v in versions.Take(40))
                PickerList.Children.Add(BuildPickerRow(loader, v));
        }
        catch (Exception ex)
        {
            PickerList.Children.Clear();
            PickerList.Children.Add(new TextBlock
            {
                Text = "获取失败：" + ex.Message,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0x9F, 0x80)),
                Margin = new Thickness(2, 8, 0, 0)
            });
        }
    }

    private Border BuildPickerRow(string loader, LoaderService.LoaderVersion version)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = version.Version,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(name);

        var tag = new TextBlock
        {
            Text = version.Stable ? (loader == "Fabric" ? "稳定版" : "推荐") : "",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tag, 1);
        grid.Children.Add(tag);

        var row = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 5),
            Cursor = Cursors.Hand,
            Child = grid
        };
        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));
        row.MouseLeave += (_, _) => row.Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
        row.MouseLeftButtonUp += (_, _) => StartLoaderInstall(loader, version.ApiName);
        return row;
    }

    private void OpenOptiFine()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://optifine.net/downloads")
            {
                UseShellExecute = true
            });
        }
        catch { }

        HintText.Text = "OptiFine 要在官网下载安装器，装完会自己出现在版本选择里";
    }

    private void BackToLoaders_Click(object sender, RoutedEventArgs e)
    {
        PickerPanel.Visibility = Visibility.Collapsed;
        LoaderListPanel.Visibility = Visibility.Visible;
    }

    private void StartLoaderInstall(string loader, string version)
    {
        SelectView.Visibility = Visibility.Collapsed;
        ProgressView.Visibility = Visibility.Visible;
        AssetsCheck.Visibility = Visibility.Collapsed;
        TitleText.Text = "安装 " + loader;
        BuildSteps();
        StatusText.Text = _versionId + " + " + loader + " " + version;
        InstallLoaderAsync(loader, version);
    }

    private async void InstallLoaderAsync(string loader, string version)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();

        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.BeginTask("安装 " + loader, "准备中…");

        var progress = new Progress<(string Msg, double Pct)>(p =>
        {
            PctText.Text = Math.Round(p.Pct, 1).ToString("0.0") + " %";
            StatusText.Text = p.Msg;
            main?.UpdateTask(p.Pct, p.Msg);
        });

        try
        {
            string id = loader switch
            {
                "Fabric" => await LoaderService.InstallFabricAsync(_versionId, version, progress, _cts.Token),
                "NeoForge" => await LoaderService.InstallNeoForgeAsync(_versionId, version, progress, _cts.Token),
                _ => await LoaderService.InstallForgeAsync(_versionId, version, progress, _cts.Token)
            };

            Installed = true;
            StatusText.Text = "装好了：" + id + "，去「版本选择」就能启动";
            CancelBtn.Visibility = Visibility.Collapsed;
            DoneBtn.Visibility = Visibility.Visible;
            main?.EndTask(true, loader + " 装好了");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "已取消";
            CancelBtn.Visibility = Visibility.Collapsed;
            DoneBtn.Visibility = Visibility.Visible;
            main?.EndTask(false, "已取消");
        }
        catch (Exception ex)
        {
            StatusText.Text = "安装失败：" + ex.Message;
            CancelBtn.Visibility = Visibility.Collapsed;
            DoneBtn.Visibility = Visibility.Visible;
            main?.EndTask(false, "安装失败：" + ex.Message);
        }
        finally
        {
            _running = false;
        }
    }

    // ===== 下载清单（照 PCL：左边总进度，右边任务列表）=====

    private static readonly (string Stage, string Label)[] Steps =
    {
        ("json", "下载版本信息"),
        ("client", "下载客户端"),
        ("libraries", "下载支持库文件"),
        ("natives", "解压运行库"),
        ("assets", "下载资源文件"),
    };

    private readonly System.Collections.Generic.List<(string Stage, TextBlock Icon, TextBlock State)> _rows = new();
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private int _lastDone;
    private double _lastSeconds;
    private string _lastStage = "json";
    private int _lastTotal = 1;

    private void BuildSteps()
    {
        StepList.Children.Clear();
        _rows.Clear();

        foreach (var (stage, label) in Steps)
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };

            var icon = new TextBlock
            {
                Text = "…",
                Width = 24,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var name = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD6, 0xE2, 0xF0)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 0, 0, 0)
            };

            var state = new TextBlock
            {
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8))
            };

            grid.Children.Add(icon);
            grid.Children.Add(name);
            grid.Children.Add(state);
            StepList.Children.Add(grid);
            _rows.Add((stage, icon, state));
        }
    }

    private static double StagePercent(string stage, int done, int total)
    {
        var frac = total <= 0 ? 0 : (double)done / total;
        return stage switch
        {
            "json" => 5 * frac,
            "client" => 5 + 10 * frac,
            "libraries" => 15 + 30 * frac,
            "natives" => 45 + 5 * frac,
            "assets" => 50 + 50 * frac,
            _ => 100
        };
    }

    private double OnStage(string stage, int done, int total)
    {
        var index = Array.FindIndex(Steps, s => s.Stage == stage);

        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (i < index)
            {
                row.Icon.Text = "✓";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                row.State.Text = "";
            }
            else if (i == index)
            {
                row.Icon.Text = stage == "done" ? "✓" : "●";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xC8, 0xFF));
                row.State.Text = total > 0 ? done + " / " + total : "";
                row.State.Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xC8, 0xFF));
            }
            else
            {
                row.Icon.Text = "…";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x7C, 0x92));
                row.State.Text = "";
            }
        }

        if (stage == "done")
        {
            foreach (var row in _rows)
            {
                row.Icon.Text = "✓";
                row.Icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                row.State.Text = "";
            }
        }

        var pct = StagePercent(stage, done, total);
        PctText.Text = Math.Round(pct, 1).ToString("0.0") + " %";

        // 剩余文件：当前阶段还剩多少
        RemainText.Text = Math.Max(0, total - done).ToString();

        // 速度：按「每秒完成多少个文件」估算
        if (_clock.IsRunning)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            var delta = seconds - _lastSeconds;
            if (delta >= 0.5)
            {
                var speed = (done - _lastDone) / delta;
                SpeedText.Text = speed < 0 ? "0 个/秒" : speed.ToString("0.0") + " 个/秒";
                _lastDone = done;
                _lastSeconds = seconds;
            }
        }

        if (stage != _lastStage)
        {
            _lastStage = stage;
            _lastDone = done;
            _lastSeconds = _clock.Elapsed.TotalSeconds;
        }

        return pct;
    }

    private async void StartAsync()
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();

        BuildSteps();
        _clock.Restart();
        _lastDone = 0;
        _lastSeconds = 0;
        _lastStage = "json";

        // 交给主窗口的右下角浮层，自己这个选择窗退场
        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        main?.ShowDownloadBall("安装 " + _versionId);
        if (main != null) main.CancelCurrentDownload = () => _cts?.Cancel();

        // 先标记「是我自己关的」，否则 OnClosing 会把下载当场取消
        _selfClosed = true;
        Close();

        var progress = new Progress<(string Msg, double Pct)>(p =>
        {
            StatusText.Text = p.Msg;
            main?.UpdateDownloadBall(_lastStage, _lastDone, _lastTotal, p.Pct, p.Msg);
            main?.ReportTaskBytes(InstallService.DownloadedBytes);
        });

        var stages = new Progress<(string Stage, int Done, int Total)>(p =>
        {
            var pct = OnStage(p.Stage, p.Done, p.Total);
            _lastStage = p.Stage;
            _lastDone = p.Done;
            _lastTotal = p.Total;
            main?.UpdateDownloadBall(p.Stage, p.Done, p.Total, pct);
        });

        var ok = false;
        var message = "";
        var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hearth-install.log");
        void Trace(string text)
        {
            try
            {
                System.IO.File.AppendAllText(logPath,
                    DateTime.Now.ToString("HH:mm:ss") + "  [" + _versionId + "] " + text + Environment.NewLine);
            }
            catch { }
        }

        Trace("开始安装，资源文件=" + (AssetsCheck.IsChecked == true));

        try
        {
            await InstallService.InstallAsync(_versionId, AssetsCheck.IsChecked == true, progress, _cts.Token, stages);
            Installed = true;
            ok = true;
            message = "安装完成";
            StatusText.Text = "安装完成，去「版本选择」就能启动";
            Trace("完成");
        }
        catch (OperationCanceledException)
        {
            message = "已取消";
            StatusText.Text = "已取消";
            Trace("已取消");
        }
        catch (Exception ex)
        {
            message = "安装失败：" + ex.Message;
            StatusText.Text = message;
            Trace("失败: " + ex);
        }
        finally
        {
            _running = false;
        }

        if (ok) main?.RefreshInstalledList();
        main?.FinishDownloadBall(ok, message);

        // 失败要说清楚，别让球红一下就没了
        if (!ok && main != null)
            DarkDialog.Info(main, "安装没有完成", message);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        CancelBtn.IsEnabled = false;
        StatusText.Text = "正在取消…";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        // 只有用户自己关窗口才取消下载；交出任务时不算
        if (_running && !_selfClosed) _cts?.Cancel();
        base.OnClosing(e);
    }
}
