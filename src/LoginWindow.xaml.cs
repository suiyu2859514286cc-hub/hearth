using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockLauncher;

/// <summary>微软正版登录：设备码流程，带 Xbox + Minecraft 档案</summary>
public partial class LoginWindow : Window
{
    private CancellationTokenSource? _cts;

    public string? AccountName { get; private set; }
    public string? AccountUuid { get; private set; }
    public string? AccessToken { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
        Loaded += async (_, _) => await StartLoginAsync();
        Closed += (_, _) => _cts?.Cancel();
    }

    private async Task StartLoginAsync()
    {
        if (string.IsNullOrEmpty(MicrosoftAuth.ClientId))
        {
            CodeText.Text = "------";
            UrlText.Text = "还没配置微软应用 ID";
            SetStatus("设置 → 其他 → 填「微软客户端 ID」后再来", "#FBBF24");
            return;
        }

        _cts = new CancellationTokenSource();
        try
        {
            SetStatus("正在向微软要验证码…", "#8FA0B8");

            var deviceCode = await MicrosoftAuth.RequestDeviceCodeAsync(_cts.Token);

            CodeText.Text = deviceCode.UserCode;
            UrlText.Text = deviceCode.VerificationUri;

            // 自动复制，省得回来手动复
            var copied = false;
            try
            {
                Clipboard.SetText(deviceCode.UserCode);
                copied = true;
            }
            catch { }

            SetStatus(copied
                ? "验证码已自动复制，去浏览器粘贴（Ctrl+V）就行"
                : "在浏览器里输入这个码，然后等它自己完成", "#8FA0B8");

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(deviceCode.VerificationUri)
                {
                    UseShellExecute = true
                });
            }
            catch { }

            var account = await MicrosoftAuth.WaitForLoginAsync(deviceCode,
                new Progress<string>(m => SetStatus(m, "#8FA0B8")), _cts.Token);

            MicrosoftAuth.Save(account);
            AccountName = account.Name;
            AccountUuid = account.Uuid;
            AccessToken = account.AccessToken;

            SetStatus("登录成功：" + account.Name, "#4ADE80");
            await Task.Delay(800);

            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "#F87171");

            // 登录失败时把完整信息写下来，方便排查
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hearth-login-error.txt"),
                    DateTime.Now + Environment.NewLine + ex.ToString());
            }
            catch { }
        }
    }

    private void SetStatus(string text, string color)
    {
        StatusText.Text = text;
        try { StatusText.Foreground = (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(CodeText.Text); SetStatus("验证码已复制", "#8FA0B8"); } catch { }
    }

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UrlText.Text)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
