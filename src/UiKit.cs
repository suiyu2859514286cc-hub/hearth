using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BlockLauncher;

/// <summary>界面小组件</summary>
public static class UiKit
{
    private static readonly Brush Accent = new SolidColorBrush(Color.FromArgb(0xCC, 0x25, 0x63, 0xEB));
    private static readonly Brush Ring = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA3));
    private static readonly Brush TextOn = new SolidColorBrush(Color.FromRgb(0xE8, 0xF0, 0xFA));
    private static readonly Brush TextOff = new SolidColorBrush(Color.FromRgb(0xB9, 0xC9, 0xDC));

    /// <summary>圆点单选：● 选项　○ 选项</summary>
    public static StackPanel ChoiceGroup(string[] options, int current, Action<int> onPick)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var items = new List<(Ellipse Dot, Border Ring, TextBlock Text)>();
        var state = new[] { current };

        void Refresh()
        {
            for (var i = 0; i < items.Count; i++)
            {
                var on = i == state[0];
                items[i].Dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                items[i].Ring.BorderBrush = on ? Accent : Ring;
                items[i].Text.Foreground = on ? TextOn : TextOff;
            }
        }

        for (var i = 0; i < options.Length; i++)
        {
            var idx = i;

            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };

            var ring = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                BorderBrush = Ring,
                BorderThickness = new Thickness(1.5),
                Background = Brushes.Transparent,
                Child = dot,
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = options[i],
                FontSize = 11,
                Foreground = TextOff,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0)
            };

            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 20, 4),
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent
            };
            item.Children.Add(ring);
            item.Children.Add(text);
            item.MouseLeftButtonUp += (_, _) =>
            {
                state[0] = idx;
                Refresh();
                onPick(idx);
            };

            items.Add((dot, ring, text));
            panel.Children.Add(item);
        }

        Refresh();
        return panel;
    }
}
