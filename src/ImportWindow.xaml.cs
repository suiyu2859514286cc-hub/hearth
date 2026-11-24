using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BlockLauncher;

public partial class ImportWindow : Window
{
    private PackService.PackInfo? _pack;
    private CancellationTokenSource? _cts;
    private bool _running;

    /// <summary>导入完成后外面用它判断要不要刷新版本列表</summary>
    public bool Imported { get; private set; }

    public ImportWindow(string? initialPath = null)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WindowAnim.PlayIn(RootPanel);
            if (!string.IsNullOrEmpty(initialPath)) Inspect(initialPath);
        };
    }

    private void PickZip_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择整合包",
            Filter = "整合包 (*.zip;*.mrpack)|*.zip;*.mrpack|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true) Inspect(dialog.FileName);
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择整合包文件夹或 .minecraft 目录" };
        if (dialog.ShowDialog(this) == true) Inspect(dialog.FolderName);
    }

    private void Inspect(string path)
    {
        _pack = PackService.Inspect(path);
        if (_pack == null)
        {
            PackName.Text = "没认出来这是什么";
            PackDetail.Text = "需要 CurseForge 的 manifest.json、Modrinth 的 modrinth.index.json，或者包含 versions 的 .minecraft 目录。";
            StartBtn.IsEnabled = false;
            return;
        }

        PackName.Text = _pack.Name;
        NameRow.Visibility = _pack.Type == "minecraft" ? Visibility.Collapsed : Visibility.Visible;
        NameBox.Text = _pack.Name;
        var typeName = _pack.Type switch
        {
            "modrinth" => "Modrinth 整合包",
            "curseforge" => "CurseForge 整合包",
            _ => "完整 .minecraft 目录"
        };
        var parts = new System.Collections.Generic.List<string> { typeName };
        if (!string.IsNullOrEmpty(_pack.McVersion)) parts.Add("MC " + _pack.McVersion);
        if (!string.IsNullOrEmpty(_pack.Loader))
            parts.Add(_pack.Loader + " " + _pack.LoaderVersion);
        if (_pack.FileCount > 0) parts.Add(_pack.FileCount + " 个文件");

        PackDetail.Text = string.Join(" · ", parts) + "\n来源：" + _pack.SourcePath;
        StartBtn.IsEnabled = true;
        StatusText.Text = "";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_pack == null || _running) return;

        _running = true;
        StartBtn.IsEnabled = false;
        _cts = new CancellationTokenSource();

        var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
        var title = "导入 " + (_pack?.Name ?? "整合包");
        main?.BeginTask(title, "准备导入…");
        if (main != null) main.CancelCurrentDownload = () => _cts?.Cancel();

        var progress = new Progress<(string Msg, double Pct)>(p =>
        {
            Bar.Value = p.Pct;
            StatusText.Text = p.Msg;
            main?.UpdateTask(p.Pct, p.Msg);
        });

        var ok = false;
        var result = "";

        if (_pack == null)
        {
            StatusText.Text = "整合包信息还没读出来，稍等一下再点";
            return;
        }

        try
        {
            var name = NameRow.Visibility == Visibility.Visible ? NameBox.Text : null;
            result = await PackService.InstallAsync(_pack, progress, name, _cts.Token);
            Imported = true;
            ok = true;
            StatusText.Text = result;
        }
        catch (OperationCanceledException)
        {
            result = "已取消";
            StatusText.Text = result;
        }
        catch (Exception ex)
        {
            result = "导入失败：" + ex.Message;
            StatusText.Text = result;
        }
        finally
        {
            _running = false;
            StartBtn.IsEnabled = true;
        }

        main?.EndTask(ok, result);
        if (main != null) main.CancelCurrentDownload = null;
        if (!ok) DarkDialog.Info(main, "导入没有完成", result);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_running) _cts?.Cancel();
        base.OnClosing(e);
    }
}
