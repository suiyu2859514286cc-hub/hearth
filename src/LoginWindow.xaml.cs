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

    private void OfflineEntry_Click(object sender, RoutedEventArgs e)
    {
        OfflinePanel.Visibility = Visibility.Visible;
        FooterHint.Text = "离线模式下只有这个名字跟着你";
        OfflineNameBox.Text = AppSettings.Get("offlineName", "");
        OfflineNameBox.Focus();
        OfflineNameBox.SelectAll();

        // 停止正版流程
        _cts?.Cancel();
        SetStatus("已切换到离线登录", "#8FA0B8");
    }

    private void OfflineCancel_Click(object sender, RoutedEventArgs e)
    {
        OfflinePanel.Visibility = Visibility.Collapsed;
        FooterHint.Text = "登录成功后可游玩正版服务器";
    }

    private void OfflineConfirm_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = (OfflineNameBox.Text ?? "").Trim();

            if (name.Length < 3)
            {
                OfflineStatus.Text = "名字至少 3 个字符";
                return;
            }

            if (name.Length > 16)
            {
                OfflineStatus.Text = "名字最多 16 个字符";
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_]+$"))
            {
                OfflineStatus.Text = "只能用英文字母、数字和下划线（不支持中文）";
                return;
            }

            OfflineStatus.Text = "";
            AppSettings.Set("offlineName", name);

            AccountName = name;
            AccountUuid = OfflineUuid(name);
            AccessToken = null;      // 离线没有令牌

            // 不是以 ShowDialog 打开时不能设 DialogResult，直接关
            try { DialogResult = true; }
            catch { }

            Close();
        }
        catch (Exception ex)
        {
            OfflineStatus.Text = "出错了：" + ex.Message;
        }
    }

    /// <summary>离线模式的 UUID 算法，跟原版一致（MD5 of "OfflinePlayer:" + name）</summary>
    public static string OfflineUuid(string name)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var bytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + name));

        // 按 UUID 风格设置版本位，跟官方一致
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..];
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
