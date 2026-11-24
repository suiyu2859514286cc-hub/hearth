using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace BlockLauncher;

/// <summary>可编辑布局：保存/加载各组件在 Canvas 上的坐标</summary>
public static class LayoutService
{
    public record Box(string Id, double X, double Y, double W, double H);

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth");
    private static readonly string FilePath = Path.Combine(Dir, "layout.json");

    public static Dictionary<string, Box> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var list = JsonSerializer.Deserialize<List<Box>>(File.ReadAllText(FilePath));
            var dict = new Dictionary<string, Box>();
            if (list != null) foreach (var b in list) dict[b.Id] = b;
            return dict;
        }
        catch { return new(); }
    }

    public static void Save(IEnumerable<Box> boxes)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(boxes, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>把元素放到 Canvas 指定位置（选择 Canvas.Left/Top 或 Margin）</summary>
    public static void Apply(FrameworkElement el, Box b)
    {
        Canvas.SetLeft(el, b.X);
        Canvas.SetTop(el, b.Y);
        if (b.W > 0) el.Width = b.W;
        if (b.H > 0) el.Height = b.H;
    }
}
