using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockLauncher;

/// <summary>
/// 统一的下拉菜单：不用 ContextMenu（它的模板会被系统主题盖成白色），全部自己画。
/// 用法：MenuPopup.Show(按钮, new[] { ("选项", (Action?)xxx), ("-", null), ("另一个", ...) })；
/// label 传 "-" 表示分隔线。
/// </summary>
public static class MenuPopup
{
    private static Popup? _current;

    public static void Show(FrameworkElement target, IEnumerable<(string Label, Action? OnClick)> items)
    {
        _current?.SetCurrentValue(Popup.IsOpenProperty, false);

        var panel = new StackPanel { MinWidth = 180 };

        foreach (var (label, action) in items)
        {
            if (label == "-")
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF))
                });
                continue;
            }

            var row = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };

            row.Child = new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA)),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (action != null)
            {
                var act = action;
                row.MouseEnter += (_, _) =>
                    row.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x25, 0x63, 0xEB));
                row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (_, _) =>
                {
                    if (_current != null) _current.IsOpen = false;
                    act();
                };
            }
            else
            {
                row.Opacity = 0.5;
            }

            panel.Children.Add(row);
        }

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xFA, 0x16, 0x1E, 0x2A)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(5),
            Child = panel
        };

        _current = new Popup
        {
            Child = border,
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            VerticalOffset = 4
        };

        _current.IsOpen = true;
    }
}
