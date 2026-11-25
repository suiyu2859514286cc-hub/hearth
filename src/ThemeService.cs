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

        // 原来的「五彩斑斓」（发反馈解锁）换成这个：金色「秘技」，靠彩蛋解锁
        new("konami", "秘技", "#FACC15", "konami"),
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

    /// <summary>解锁小提示：只给暗示，不给答案</summary>
    public static string Hint(ThemeItem theme) => theme.Unlock switch
    {
        "premium" => "这世界的门票，只发给有名字的人。",
        "night" => "当钟走过零点，别急着睡。",
        "loaders3" => "三种手艺都碰过的人，才懂什么叫兼容。",
        "versions10" => "十个房间，才装得下你的收藏。",
        "folders3" => "换过三次风景，你才算走过远路。",
        "packs2" => "接过两份别人的图纸，你也能当匠人。",
        "festival:snow" => "一年里总有那么几天，窗外是白的。",
        "festival:halloween" => "十月的最后一晚，别忘了点灯。",
        "festival:newyear" => "钟声敲响的那一刻，你还在。",
        "festival:valentine" => "有些日子，天是粉的。",
        "feedback" => "说一句话给做这个东西的人听。",
        "konami" => "有人在某个写反馈的地方，敲过一串很老的密码。",
        var n when int.TryParse(n, out var need) => need switch
        {
            5 => "炉火刚点起来的时候。",
            10 => "天快黑的时候，光是紫的。",
            20 => "烧到发红，还差一会儿。",
            50 => "抬头时，天边有光在动。",
            100 => "整片天空都是你的。",
            _ => "再多走几步。"
        },
        _ => ""
    };

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
