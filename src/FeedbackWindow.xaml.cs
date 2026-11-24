using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace BlockLauncher;

public partial class FeedbackWindow : Window
{
    public FeedbackWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
    }

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        var text = ContentBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "写点东西再发吧";
            return;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hearth", "feedback");
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
            File.WriteAllText(file,
                "时间：" + DateTime.Now + Environment.NewLine +
                "启动器：Hearth " + AppSettings.Version + Environment.NewLine +
                "系统：" + Environment.OSVersion + Environment.NewLine +
                "----------------" + Environment.NewLine +
                text);

            ThemeService.MarkFeedbackSent();
            DarkDialog.Toast(this, "收到了，谢谢");
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "保存失败：" + ex.Message;
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
