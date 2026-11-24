using System;
using System.Windows;
using System.Windows.Controls;

namespace BlockLauncher;

/// <summary>第三方账号（皮肤站）登录</summary>
public partial class ThirdPartyWindow : Window
{
    public string? AccessToken { get; private set; }
    public string? AccountName { get; private set; }
    public string? AccountUuid { get; private set; }
    public string? ServerUrl { get; private set; }

    public ThirdPartyWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);

        foreach (var site in YggdrasilService.KnownSites)
            SiteCombo.Items.Add(site.Name);

        // 上次用过的优先
        var saved = AppSettings.Get("authlibUrl", "");
        var idx = 0;
        for (var i = 0; i < YggdrasilService.KnownSites.Length; i++)
            if (YggdrasilService.KnownSites[i].Url == saved) { idx = i; break; }

        SiteCombo.SelectedIndex = idx;
    }

    private void Site_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SiteCombo.SelectedIndex < 0) return;
        var site = YggdrasilService.KnownSites[SiteCombo.SelectedIndex];
        CustomUrlBox.Visibility = Visibility.Collapsed;
        CustomUrlBox.Text = site.Url;
    }

    private void CustomSite_Click(object sender, RoutedEventArgs e)
    {
        CustomUrlBox.Visibility = Visibility.Visible;
        CustomUrlBox.Text = AppSettings.Get("authlibUrl", "");
        CustomUrlBox.Focus();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var url = CustomUrlBox.Visibility == Visibility.Visible
            ? CustomUrlBox.Text.Trim()
            : (SiteCombo.SelectedIndex >= 0 ? YggdrasilService.KnownSites[SiteCombo.SelectedIndex].Url : "");

        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "皮肤站地址不对，要填到 /api/yggdrasil 这一层";
            return;
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            StatusText.Text = "邮箱和密码都要填";
            return;
        }

        LoginBtn.IsEnabled = false;
        LoginBtn.Content = "登录中…";
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x8F, 0xA8, 0xC4));
        StatusText.Text = "正在连皮肤站…";

        var result = await YggdrasilService.AuthenticateAsync(url, email, password);

        LoginBtn.IsEnabled = true;
        LoginBtn.Content = "登录";

        if (result.Profile == null || string.IsNullOrEmpty(result.AccessToken))
        {
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71));
            StatusText.Text = result.Error ?? "登录失败";
            return;
        }

        AccessToken = result.AccessToken;
        AccountName = result.Profile.Name;
        AccountUuid = result.Profile.Id;
        ServerUrl = url;

        AppSettings.Set("authlibUrl", url);
        AppSettings.Set("thirdPartyName", AccountName);
        AppSettings.Set("thirdPartyUuid", AccountUuid);

        DialogResult = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
