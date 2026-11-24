using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockLauncher;

/// <summary>披风选择：每个披风显示贴图 + 名字，像官网那样</summary>
public partial class CapePickerWindow : Window
{
    private readonly List<SkinService.CapeInfo> _capes;

    /// <summary>用户选了哪个（null = 取消）</summary>
    public SkinService.CapeInfo? Selected { get; private set; }

    public CapePickerWindow(List<SkinService.CapeInfo> capes)
    {
        InitializeComponent();
        _capes = capes;

        RootPanel.Style = null;
        Loaded += async (_, _) =>
        {
            CenterOnScreen();
            WindowAnim.PlayIn(RootPanel);
            await BuildAsync();
        };
    }

    private void CenterOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + Math.Max(0, (wa.Width - ActualWidth) / 2);
        Top = wa.Top + Math.Max(0, (wa.Height - ActualHeight) / 2);
    }

    private async System.Threading.Tasks.Task BuildAsync()
    {
        CapeGrid.Children.Clear();

        foreach (var cape in _capes)
        {
            var card = new Border
            {
                Width = 160,
                Margin = new Thickness(0, 0, 10, 10),
                Background = new SolidColorBrush(cape.Active
                    ? Color.FromArgb(0x44, 0x3B, 0x82, 0xF6)
                    : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(cape.Active
                    ? Color.FromArgb(0xAA, 0x66, 0xC8, 0xFF)
                    : Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 10, 10, 8),
                Cursor = Cursors.Hand
            };

            var stack = new StackPanel();

            var imageBox = new Border
            {
                Height = 110,
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true
            };

            var preview = await SkinService.LoadCapePreviewAsync(cape.Url);
            if (preview != null)
            {
                var img = new Image { Source = preview, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                imageBox.Child = img;
            }

            stack.Children.Add(imageBox);

            var name = new TextBlock
            {
                Text = cape.Alias,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 7, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(name);

            var state = new TextBlock
            {
                Text = cape.Active ? "使用中" : "点击更换",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(cape.Active
                    ? Color.FromRgb(0x9C, 0xC8, 0xFF)
                    : Color.FromRgb(0x6B, 0x7C, 0x92)),
                Margin = new Thickness(0, 2, 0, 0)
            };
            stack.Children.Add(state);

            card.Child = stack;
            card.MouseLeftButtonUp += (_, _) =>
            {
                Selected = cape;
                Close();
            };

            CapeGrid.Children.Add(card);
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
