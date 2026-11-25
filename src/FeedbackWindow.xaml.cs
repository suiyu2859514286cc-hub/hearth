using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace BlockLauncher;

public partial class FeedbackWindow : Window
{
    private readonly KonamiWatcher _konami = new();

    public FeedbackWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void ContentBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        Placeholder.Visibility = string.IsNullOrEmpty(ContentBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 只在反馈窗口里认，别影响主界面。
        // 中文输入法开着时，b/a 会被 IME 吞掉，WPF 报的是 ImeProcessed，
        // 真正的键在 ImeProcessedKey 里。
        var key = e.Key switch
        {
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.System => e.SystemKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };

        if (_konami.Feed(key)) UnlockKonami();
    }

    private void UnlockKonami()
    {
        if (!ThemeService.UnlockKonami())
        {
            DarkDialog.Toast(this, "这串密码你已经用过了");
            return;
        }

        var main = Owner ?? Application.Current.MainWindow;
        var yes = DarkDialog.Confirm(main, "彩蛋",
            "解锁了隐藏主题「秘技」。" + Environment.NewLine + Environment.NewLine +
            "现在就换上吗？",
            "换上", "先留着");

        if (yes)
        {
            var theme = ThemeService.Themes.FirstOrDefault(t => t.Id == "konami");
            if (theme != null)
            {
                ThemeService.Use(theme);
                if (main is MainWindow mw) mw.ApplyTheme();
            }
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = ContentBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "写点东西再发吧";
            return;
        }

        var report = BuildReport(text);

        try
        {
            // 1. 本地留一份（万一剪贴板没粘上）
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hearth", "feedback");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(file, report);

            // 2. 复制到剪贴板，让用户直接粘到群里
            Clipboard.SetText(report);

            ThemeService.MarkFeedbackSent();

            var main = Owner ?? Application.Current.MainWindow;

            // 问一句：发给群，或者提到 GitHub
            var wantGithub = DarkDialog.Confirm(main, "反馈已复制",
                "内容已经复制到剪贴板了。" +
                Environment.NewLine + Environment.NewLine +
                "· 点「提到 GitHub」会打开已经填好的 Issue 页面（需要 GitHub 账号，登录后点一下提交）" +
                Environment.NewLine +
                "· 点「发群里」就把刚才那段粘到群里" +
                Environment.NewLine + Environment.NewLine +
                "里面已经带了启动器版本、系统信息和最近日志。",
                "提到 GitHub", "发群里", false);

            if (wantGithub)
            {
                OpenGithubIssue(text, report);
                StatusText.Text = "已在浏览器打开 Issue 页面";
            }
            else
            {
                StatusText.Text = "已复制，粘贴到群里发一下";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "复制失败：" + ex.Message;
        }
    }

    /// <summary>打开预填好内容的 GitHub Issue 新建页</summary>
    private static void OpenGithubIssue(string userText, string report)
    {
        try
        {
            var firstLine = userText.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
            if (firstLine.Length > 50) firstLine = firstLine[..50];

            var title = Uri.EscapeDataString("[Bug] " + firstLine);
            var body = Uri.EscapeDataString(report);

            var url = "https://github.com/suiyu2859514286cc-hub/hearth/issues/new" +
                      "?labels=bug&title=" + title + "&body=" + body;

            // 太长的话 URL 会被截断，退回复制方案
            if (url.Length > 8000)
            {
                Clipboard.SetText(report);
                DarkDialog.Info(null, "内容有点长",
                    "这次反馈内容太长，放不进链接。已经复制到剪贴板了，粘贴到 GitHub 页面里就行。" +
                    Environment.NewLine + Environment.NewLine +
                    "https://github.com/suiyu2859514286cc-hub/hearth/issues/new");
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DarkDialog.Info(null, "打开失败", ex.Message + Environment.NewLine + Environment.NewLine +
                "可以直接去 https://github.com/suiyu2859514286cc-hub/hearth/issues 手动提");
        }
    }

    /// <summary>组装一份完整的反馈文本：环境 + 日志尾部 + 用户描述</summary>
    private static string BuildReport(string userText)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("【Hearth 反馈】");
        sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("启动器：Hearth " + AppSettings.Version);
        sb.AppendLine("系统：" + Environment.OSVersion + " / " + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位"));
        sb.AppendLine("游戏目录：" + VersionService.McDir);

        try
        {
            var launcherLog = Path.Combine(Path.GetTempPath(), "hearth-launch.log");
            if (File.Exists(launcherLog))
            {
                var tail = File.ReadAllText(launcherLog);
                if (tail.Length > 1200) tail = "…" + tail[^1200..];
                sb.AppendLine();
                sb.AppendLine("--- 最近的启动日志 ---");
                sb.AppendLine(tail.Trim());
            }
        }
        catch { }

        sb.AppendLine();
        sb.AppendLine("--- 问题描述 ---");
        sb.AppendLine(userText);

        return sb.ToString();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
