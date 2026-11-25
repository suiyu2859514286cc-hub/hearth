using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace BlockLauncher;

/// <summary>
/// 主题（皮肤）。解锁条件全部隐藏，界面上只显示「已解锁 / 未解锁」。
/// </summary>
public static class ThemeService
{
    public record ThemeItem(string Id, string Name, string Hex, string Unlock);

    public static readonly List<ThemeItem> Themes = new()
    {
        new("default",  "经典",   "#3B82F6", ""),
        new("ocean",    "潮汐",   "#0EA5E9", "premium"),
        new("forest",   "林地",   "#22C55E", "5"),
        new("twilight", "暮光",   "#8B5CF6", "10"),
        new("lava",     "熔岩",   "#F97316", "20"),
        new("aurora",   "极光",   "#14B8A6", "50"),
        new("stardust", "星尘",   "#EC4899", "100"),

        // 隐藏款
        new("night",    "夜行",   "#4C1D95", "night"),
        new("threekit", "三修",   "#06B6D4", "loaders3"),
        new("collector","收藏家", "#A855F7", "versions10"),
        new("wanderer", "远行",   "#65A30D", "folders3"),
        new("artisan",  "匠人",   "#EA580C", "packs2"),

        // 限定节日
        new("snow",      "雪夜", "#BAE6FD", "festival:snow"),
        new("halloween", "南瓜", "#F59E0B", "festival:halloween"),
        new("newyear",   "新岁", "#EF4444", "festival:newyear"),
        new("valentine", "心动", "#F43F5E", "festival:valentine"),

        // 五彩斑斓：解锁方式改成反馈窗口里的 Konami 彩蛋
        new("rainbow", "五彩斑斓", "", "konami"),
    };

    public static ThemeItem Current =>
        Themes.FirstOrDefault(t => t.Id == AppSettings.Get("theme", "default")) ?? Themes[0];

    public static Color Accent
    {
        get
        {
            if (string.IsNullOrEmpty(Current.Hex)) return Color.FromRgb(0x8B, 0x5C, 0xF6);
            try { return (Color)ColorConverter.ConvertFromString(Current.Hex); }
            catch { return Color.FromRgb(0x3B, 0x82, 0xF6); }
        }
    }

    /// <summary>五彩斑斓用的渐变</summary>
    public static LinearGradientBrush RainbowBrush(double opacity = 1)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        var stops = new (Color Color, double At)[]
        {
            (Color.FromRgb(0xFF, 0x4D, 0x4F), 0.00),
            (Color.FromRgb(0xFF, 0x9F, 0x1F), 0.17),
            (Color.FromRgb(0xFF, 0xE6, 0x3D), 0.34),
            (Color.FromRgb(0x3D, 0xD6, 0x8C), 0.50),
            (Color.FromRgb(0x3D, 0xB5, 0xFF), 0.67),
            (Color.FromRgb(0x8B, 0x5C, 0xF6), 0.84),
            (Color.FromRgb(0xD9, 0x4D, 0xFF), 1.00),
        };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B), at));
        return brush;
    }

    public static bool IsRainbow => Current.Id == "rainbow";

    // ===== 统计 =====

    public static int LaunchCount => AppSettings.GetInt("launchCount", 0);
    public static bool IsPremium => AppSettings.GetBool("premiumUnlocked", false);

    public static int CountLaunch()
    {
        var n = LaunchCount + 1;
        AppSettings.Set("launchCount", n.ToString());

        // 凌晨 0–5 点启动过
        var now = DateTime.Now;
        if (now.Hour >= 0 && now.Hour < 5) AppSettings.Set("nightOwl", "1");

        // 节日当天启动过
        if (now.Month == 12 && now.Day >= 24 && now.Day <= 26) AppSettings.Set("festival_snow", "1");
        if (now.Month == 10 && now.Day == 31) AppSettings.Set("festival_halloween", "1");
        if (now.Month == 1 && now.Day == 1) AppSettings.Set("festival_newyear", "1");
        if (now.Month == 2 && now.Day == 14) AppSettings.Set("festival_valentine", "1");

        return n;
    }

    public static void MarkFeedbackSent() => AppSettings.Set("feedbackSent", "1");

    public static void CountImportedPack()
    {
        var n = AppSettings.GetInt("importedPacks", 0) + 1;
        AppSettings.Set("importedPacks", n.ToString());
    }

    /// <summary>当前游戏文件夹里装了几个版本</summary>
    public static int InstalledVersionCount()
    {
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions");
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetDirectories(dir).Count(d =>
                File.Exists(Path.Combine(d, Path.GetFileName(d) + ".json")));
        }
        catch { return 0; }
    }

    /// <summary>装过几种加载器（Fabric / Forge / NeoForge）</summary>
    public static int LoaderKinds()
    {
        try
        {
            var dir = Path.Combine(VersionService.McDir, "versions");
            if (!Directory.Exists(dir)) return 0;

            var kinds = new HashSet<string>();
            foreach (var d in Directory.GetDirectories(dir))
            {
                var id = Path.GetFileName(d).ToLowerInvariant();
                if (id.Contains("fabric-loader")) { kinds.Add("fabric"); continue; }
                if (!id.Contains("-forge-")) continue;

                var json = Path.Combine(d, Path.GetFileName(d) + ".json");
                var text = File.Exists(json) ? File.ReadAllText(json) : "";
                kinds.Add(text.Contains("neoforged", StringComparison.OrdinalIgnoreCase) ? "neoforge" : "forge");
            }
            return kinds.Count;
        }
        catch { return 0; }
    }

    // ===== 解锁 =====

    public static bool IsUnlocked(ThemeItem theme)
    {
        // 调试用：全部解锁（settings.json 里 forceUnlockAll=1）
        if (AppSettings.GetBool("forceUnlockAll", false)) return true;
        if (string.IsNullOrEmpty(theme.Unlock)) return true;

        if (theme.Unlock == "feedback") return AppSettings.GetBool("feedbackSent", false);
        if (theme.Unlock == "konami") return AppSettings.GetBool("konamiUnlocked", false);

        if (theme.Unlock.StartsWith("festival:", StringComparison.Ordinal))
            return AppSettings.GetBool("festival_" + theme.Unlock["festival:".Length..], false);

        return theme.Unlock switch
        {
            "premium" => IsPremium,
            "night" => AppSettings.GetBool("nightOwl", false),
            "loaders3" => LoaderKinds() >= 3,
            "versions10" => InstalledVersionCount() >= 10,
            "folders3" => GameFolderService.Folders.Count >= 3,
            "packs2" => AppSettings.GetInt("importedPacks", 0) >= 2,
            var n => int.TryParse(n, out var need) && LaunchCount >= need
        };
    }

    /// <summary>已解锁时显示：这个主题当时是怎么来的</summary>
    public static string UnlockText(ThemeItem theme)
    {
        if (string.IsNullOrEmpty(theme.Unlock)) return "默认主题，不用解锁。";

        return theme.Unlock switch
        {
            "premium" => "登录过一次微软正版账号。",
            "night" => "在凌晨 0~5 点之间启动过启动器。",
            "loaders3" => "本机装过 Fabric、Forge、NeoForge 三种加载器。",
            "versions10" => "本机装过 10 个游戏版本。",
            "folders3" => "在启动器里添加过 3 个游戏文件夹。",
            "packs2" => "导入过 2 个整合包。",
            "festival:snow" => "在 12 月 24~26 日启动过。",
            "festival:halloween" => "在 10 月 31 日启动过。",
            "festival:newyear" => "在 1 月 1 日启动过。",
            "festival:valentine" => "在 2 月 14 日启动过。",
            "konami" => "在「发个反馈」窗口输入了 ↑↑↓↓←→←→BA。",
            var n when int.TryParse(n, out var need) => "累计启动 " + need + " 次。",
            _ => ""
        };
    }

    /// <summary>未解锁时显示：怎么做才能解锁，带当前进度</summary>
    public static string Hint(ThemeItem theme)
    {
        if (string.IsNullOrEmpty(theme.Unlock)) return "默认主题，不用解锁。";

        return theme.Unlock switch
        {
            "premium" => "用微软账号登录一次正版，就能拿到。",
            "night" => "在凌晨 0 点到 5 点之间启动一次启动器。",
            "loaders3" => "装过 Fabric、Forge、NeoForge 三种加载器（当前 " + LoaderKinds() + " 种）。",
            "versions10" => "本机装过 10 个版本（当前 " + InstalledVersionCount() + " 个）。",
            "folders3" => "添加 3 个游戏文件夹（当前 " + GameFolderService.Folders.Count + " 个）。",
            "packs2" => "导入 2 个整合包（当前 " + AppSettings.GetInt("importedPacks", 0) + " 个）。",
            "festival:snow" => "在 12 月 24~26 日启动一次。",
            "festival:halloween" => "在 10 月 31 日启动一次。",
            "festival:newyear" => "在 1 月 1 日启动一次。",
            "festival:valentine" => "在 2 月 14 日启动一次。",
            "konami" => "在「发个反馈」窗口输入：上上下下、左右左右、B、A。",
            var n when int.TryParse(n, out var need) =>
                "累计启动 " + need + " 次（当前 " + LaunchCount + " 次，还差 " +
                Math.Max(0, need - LaunchCount) + " 次）。",
            _ => ""
        };
    }

    /// <summary>上上下下左右左右 BA：反馈窗口里的彩蛋</summary>
    public static bool UnlockKonami()
    {
        if (AppSettings.GetBool("konamiUnlocked", false)) return false;
        AppSettings.Set("konamiUnlocked", "1");
        return true;
    }

    public static void Use(ThemeItem theme)
    {
        if (!IsUnlocked(theme)) return;
        AppSettings.Set("theme", theme.Id);
    }
}
