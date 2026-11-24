using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockLauncher;

/// <summary>滑轨开关：只有一个开/关的项用它，比两个按钮好看</summary>
public class ToggleSwitch : Border
{
    private readonly Border _knob;
    private readonly TranslateTransform _knobTransform = new();
    private bool _isOn;

    private static readonly Color OnColor = Color.FromArgb(0xCC, 0x25, 0x63, 0xEB);
    private static readonly Color OffColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

    public bool IsOn => _isOn;
    public event Action<bool>? Toggled;

    public ToggleSwitch(bool initial)
    {
        Width = 46;
        Height = 26;
        CornerRadius = new CornerRadius(13);
        Cursor = Cursors.Hand;
        Background = new SolidColorBrush(OffColor);

        _knob = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
            RenderTransform = _knobTransform
        };
        Child = _knob;

        Apply(initial, false);

        MouseLeftButtonUp += (_, _) =>
        {
            Apply(!_isOn, true);
            Toggled?.Invoke(_isOn);
        };
    }

    private void Apply(bool on, bool animate)
    {
        _isOn = on;
        var targetX = on ? Width - _knob.Width - 6 : 0;
        var color = new SolidColorBrush(on ? OnColor : OffColor);

        if (!animate || !AppSettings.Animations)
        {
            _knobTransform.X = targetX;
            Background = color;
            return;
        }

        var move = new DoubleAnimation(_knobTransform.X, targetX, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        _knobTransform.BeginAnimation(TranslateTransform.XProperty, move);

        var fade = new ColorAnimation(on ? OffColor : OnColor, on ? OnColor : OffColor,
            TimeSpan.FromMilliseconds(160));
        var brush = new SolidColorBrush(on ? OffColor : OnColor);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
        Background = brush;
    }
}
