using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace BlockLauncher;

public partial class AccountWindow : Window
{
    private MicrosoftAuth.Account? _account;
    private BitmapImage? _avatar;

    public bool LoggedOut { get; private set; }

    public AccountWindow()
    {
        InitializeComponent();
        // 主窗口偏右时 CenterOwner 会把钱包窗口推到屏幕外，改成主屏居中
        ContentRendered += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + Math.Max(0, (wa.Width - ActualWidth) / 2);
            Top = wa.Top + Math.Max(0, (wa.Height - ActualHeight) / 2);
        };

        Loaded += (_, _) => WindowAnim.PlayIn(RootPanel);
        Loaded += async (_, _) => await BuildAsync();
    }

    private static TextBlock Text(string text, double size = 11.5, string color = "#E8F0FA", bool bold = false)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = (Brush)new BrushConverter().ConvertFromString(color)!,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    private Border Card(string? title, params UIElement[] items)
    {
        var stack = new StackPanel();
        if (!string.IsNullOrEmpty(title))
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA0, 0xB8)),
                Margin = new Thickness(2, 0, 0, 4)
            });

        foreach (var i in items) stack.Children.Add(i);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack
        };
    }

    private Button Btn(string content, double width, Action onClick, string background = "#22FFFFFF")
    {
        var b = new Button
        {
            Content = content,
            Width = width,
            Background = (Brush)new BrushConverter().ConvertFromString(background)!,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 6, 0, 6),
            FontSize = 10.5,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button Item(string text, Action onClick)
    {
        var b = new Button { Content = text, Style = (Style)FindResource("Item") };
        b.Click += (_, _) => onClick();
        return b;
    }

    private async System.Threading.Tasks.Task BuildAsync()
    {
        // 用主窗口这个实例缓存的账号，不读文件（多开时各用各的）
        _account = (Owner as MainWindow)?.CurrentAccount ?? MicrosoftAuth.Load();
        var all = MicrosoftAuth.LoadAll();
        Body.Children.Clear();

        if (_account == null)
        {
            Body.Children.Add(Text("还没有登录的账号。", 12, "#B9C9DC"));
            var addFirst = Btn("添加账号", 110, () => AddAccount());
            addFirst.HorizontalAlignment = HorizontalAlignment.Left;
            addFirst.Margin = new Thickness(0, 12, 0, 0);
            Body.Children.Add(addFirst);
            return;
        }

        // 头像
        _avatar = await SkinService.LoadAvatarAsync(_account.Uuid, _account.Name);

        var avatarBox = new Border
        {
            Width = 88,
            Height = 88,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Top
        };

        if (_avatar != null)
            avatarBox.Child = new Image { Source = _avatar, Stretch = Stretch.UniformToFill };

        var nameBlock = Text(_account.Name, 20, "#FFFFFF", true);
        nameBlock.Margin = new Thickness(0, 0, 0, 2);
        var uuidBlock = Text(_account.Uuid, 9.5, "#6B7C92");

        var head = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.Children.Add(avatarBox);

        var info = new StackPanel { Margin = new Thickness(16, 6, 0, 0) };
        info.Children.Add(nameBlock);
        info.Children.Add(Text("微软账号 · 正版", 10, "#4ADE80"));
        info.Children.Add(uuidBlock);
        Grid.SetColumn(info, 1);
        head.Children.Add(info);
        Body.Children.Add(head);

        // 账号列表：可以有好几个，点一下切换
        var accountRows = new StackPanel();
        foreach (var acc in all)
        {
            var isCurrent = acc.Uuid == _account.Uuid;

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal };

            var badge = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(isCurrent
                    ? Color.FromArgb(0x55, 0x3B, 0x82, 0xF6)
                    : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock
            {
                Text = acc.Name.Length > 0 ? acc.Name[..1].ToUpperInvariant() : "?",
                FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            left.Children.Add(badge);

            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            nameStack.Children.Add(Text(acc.Name, 12, isCurrent ? "#FFFFFF" : "#D6E2F0", true));
            nameStack.Children.Add(Text(isCurrent ? "当前账号" : "点击切换", 9.5, isCurrent ? "#9CC8FF" : "#6B7C92"));
            left.Children.Add(nameStack);

            row.Children.Add(left);

            if (isCurrent)
            {
                var del = Btn("移除", 60, () => RemoveAccount(acc), "#33EF4444");
                del.HorizontalAlignment = HorizontalAlignment.Right;
                del.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(del, 1);
                row.Children.Add(del);
            }
            else
            {
                var use = Btn("切换", 60, () => SwitchAccount(acc), "#333B82F6");
                use.HorizontalAlignment = HorizontalAlignment.Right;
                use.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(use, 1);
                row.Children.Add(use);
            }

            row.Cursor = isCurrent ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
            accountRows.Children.Add(row);
        }

        var addBtn = Btn("+ 添加账号", 110, () => AddAccount());
        addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        addBtn.Margin = new Thickness(0, 6, 0, 2);
        accountRows.Children.Add(addBtn);

        Body.Children.Add(Card("账号（" + all.Count + "）", accountRows));

        // 皮肤
        Body.Children.Add(Card("皮肤",
            Item("修改皮肤…", ChangeSkin),
            Item("刷新皮肤", async () =>
            {
                _avatar = await SkinService.LoadAvatarAsync(_account.Uuid, _account.Name);
                await BuildAsync();
            }),
            Item("保存皮肤文件…", SaveSkin)));

        // 披风
        Body.Children.Add(Card("披风",
            Item("查看 / 更换披风", ShowCapes),
            Item("刷新披风列表", ShowCapes)));

        // 账户
        Body.Children.Add(Card("账户",
            Item("修改玩家名 / 修改密码（打开官网）", () =>
                Open("https://www.minecraft.net/msaprofile")),
            Item("使用 CDKEY 兑换奖励", () =>
                Open("https://www.minecraft.net/redeem"))));
    }

    /// <summary>切换到另一个账号</summary>
    private async void SwitchAccount(MicrosoftAuth.Account target)
    {
        MicrosoftAuth.SetCurrent(target.Uuid);

        if (Owner is MainWindow main)
        {
            main.ApplyAccountFromOutside();   // 主窗口重新读文件 → 更新它自己的缓存
            _account = main.CurrentAccount;
        }

        await BuildAsync();
    }

    /// <summary>登录一个新账号</summary>
    private async void AddAccount()
    {
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() != true) return;

        // LoginWindow 里已经 Save 过了，Save 会自动设为当前
        if (Owner is MainWindow main)
        {
            main.ApplyAccountFromOutside();
            _account = main.CurrentAccount;
        }

        await BuildAsync();
    }

    /// <summary>移除账号（先确认）</summary>
    private async void RemoveAccount(MicrosoftAuth.Account target)
    {
        if (!DarkDialog.Confirm(this, "移除账号",
                "从启动器里移除「" + target.Name + "」？\n\n只删本地登录信息，不会影响这个微软账号本身。",
                "移除", "取消", true))
            return;

        MicrosoftAuth.Remove(target.Uuid);

        if (Owner is MainWindow main)
            main.ApplyAccountFromOutside();

        await BuildAsync();
    }

    private void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private async void ChangeSkin()
    {
        if (_account == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择皮肤文件",
            Filter = "PNG 图片 (*.png)|*.png"
        };

        if (dialog.ShowDialog(this) != true) return;

        // 64x32 是旧版手臂，64x64 看是否透明判断 slim
        var variant = "classic";
        try
        {
            using var fs = File.OpenRead(dialog.FileName);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = fs;
            bmp.EndInit();

            if (bmp.PixelWidth == 64 && bmp.PixelHeight == 64)
                variant = LooksSlim(dialog.FileName) ? "slim" : "classic";
        }
        catch { }

        var error = await SkinService.UploadSkinAsync(_account.AccessToken, dialog.FileName, variant);
        if (string.IsNullOrEmpty(error))
        {
            DarkDialog.Toast(this, "皮肤已更换");
            _avatar = await SkinService.LoadAvatarAsync(_account.Uuid, _account.Name);
            await BuildAsync();
        }
        else
        {
            DarkDialog.Info(this, "换皮肤失败", error);
        }
    }

    /// <summary>64x64 皮肤里，手臂宽度 3 像素就是细手臂（slim）</summary>
    private static bool LooksSlim(string path)
    {
        try
        {
            var bmp = new BitmapImage(new Uri(path));
            var px = new byte[4 * bmp.PixelWidth * bmp.PixelHeight];
            bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
            var alphaAt = (int x, int y) => px[(y * bmp.PixelWidth + x) * 4 + 3];
            return alphaAt(50, 16) == 0 && alphaAt(54, 16) > 0;
        }
        catch { return false; }
    }

    private async void SaveSkin()
    {
        if (_account == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "保存皮肤",
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = _account.Name + "-skin.png"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bytes = await SkinService.LoadSkinBytesAsync(_account.Uuid);
            if (bytes == null)
            {
                DarkDialog.Info(this, "保存失败", "拿不到皮肤文件");
                return;
            }
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            DarkDialog.Toast(this, "皮肤已保存");
        }
        catch (Exception ex)
        {
            DarkDialog.Info(this, "保存失败", ex.Message);
        }
    }

    private async void ShowCapes()
    {
        if (_account == null) return;

        var (capes, error) = await SkinService.GetCapesAsync(_account.AccessToken);

        if (!string.IsNullOrEmpty(error))
        {
            DarkDialog.Info(this, "拿披风失败", error);
            return;
        }

        if (capes.Count == 0)
        {
            DarkDialog.Info(this, "披风", "这个账号没有可用的披风。");
            return;
        }

        var picker = new CapePickerWindow(capes) { Owner = this };
        picker.ShowDialog();

        if (picker.Selected == null) return;

        if (picker.Selected.Active)
        {
            DarkDialog.Toast(this, "已经在用这个披风了");
            return;
        }

        var setError = await SkinService.SetCapeAsync(_account.AccessToken, picker.Selected.Id);
        if (string.IsNullOrEmpty(setError))
            DarkDialog.Toast(this, "披风换成了 " + picker.Selected.Alias);
        else
            DarkDialog.Info(this, "换披风失败", setError);
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (!DarkDialog.Confirm(this, "退出登录",
                "退出后要重新走一遍微软验证码登录，确定吗？", "退出", "取消", true))
            return;

        MicrosoftAuth.Clear();
        SkinService.LoadAvatarAsync("", "").Wait(200);
        LoggedOut = true;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
