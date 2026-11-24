using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlockLauncher;

/// <summary>
/// 崩溃分析：读游戏日志和 crash-reports，按常见原因给结论。
/// 规则是先给"能一眼认出来的"，认不出来就只报现象，别瞎猜。
/// </summary>
public static class CrashAnalyzer
{
    public record Result(string Title, string Reason, string Suggestion, string LogPath, bool Confident);

    private static readonly (string Pattern, string Title, string Reason, string Suggestion)[] Rules =
    {
        (
            @"java.lang.OutOfMemoryError|Java heap space|GC overhead limit exceeded",
            "内存不够",
            "游戏在运行中把分配的内存用完了（OutOfMemoryError）。",
            "去「版本设置 → 设置」把内存调大一点（4G 起步，大整合包建议 6-8G）。\n" +
            "如果已经调得很大还崩，可能是某个 mod 内存泄漏。"
        ),
        (
            @"UnsupportedClassVersionError|has been compiled by a more recent version",
            "Java 版本太旧",
            "游戏或某个 mod 需要更新的 Java，但当前用的是一个旧版本。",
            "去「版本设置 → 设置」看看这个版本要求的 Java 版本，换一个对应的大版本（1.20+ 一般要 Java 17 或 21）。"
        ),
        (
            @"Duplicate mods|DuplicateModsFoundException|duplicate mod",
            "Mod 重复",
            "mods 文件夹里有同一个 mod 的两个版本，加载器不知道用哪个。",
            "打开 mods 文件夹，同一个 mod 只留一个版本（文件名里通常带版本号）。"
        ),
        (
            @"Missing or unsupported mandatory dependencies|requires .* which is missing|缺失.*前置",
            "缺少前置 Mod",
            "某个 mod 依赖的前置 mod 没装，或者版本对不上。",
            "按日志里提到的名字去资源中心搜那个前置 mod 装上。常见前置：Fabric API、Architectury、Cloth Config。"
        ),
        (
            @"NoClassDefFoundError|ClassNotFoundException",
            "缺少库文件",
            "加载时找不到某个类，通常是依赖没下全，或者 mod 跟游戏版本不匹配。",
            "把日志里那个类名对应的 mod 找出来，确认它的游戏版本跟当前版本一致；也可以删掉该 mod 试试能不能起来。"
        ),
        (
            @"GLFW error|Failed to create window|Pixel format not accelerated|OpenGL",
            "显卡 / 驱动问题",
            "创建游戏窗口失败，通常是显卡驱动太旧或不支持。",
            "更新显卡驱动；笔记本的话确认游戏是用独显跑的。\n" +
            "如果装了光影，先关掉光影试试。"
        ),
        (
            @"java.io.FileNotFoundException.*options|Failed to read|AccessDeniedException",
            "文件读写失败",
            "游戏读不到某个配置文件，或者没有写权限。",
            "检查游戏目录是不是被占用了（比如另一个启动器开着），或者把游戏目录从只读位置挪出来。"
        ),
        (
            @"Exception in thread ""main""|A fatal error has been detected",
            "游戏启动时崩了",
            "游戏在初始化阶段就崩了，日志开头那段能看出具体原因。",
            "把下面这份日志发给作者，或者自己看最上面几行异常。"
        ),
    };

    /// <summary>分析一次崩溃</summary>
    public static Result Analyze(string versionId, int? exitCode)
    {
        var gameDir = VersionMeta.GameDir(versionId);

        // 1) 优先看 crash-reports 里最新的那份
        var crashText = "";
        var crashPath = "";
        try
        {
            var dir = Path.Combine(gameDir, "crash-reports");
            if (Directory.Exists(dir))
            {
                var latest = Directory.GetFiles(dir, "crash-*.txt")
                    .OrderByDescending(File.GetLastWriteTime)
                    .FirstOrDefault();

                if (latest != null)
                {
                    crashPath = latest;
                    crashText = File.ReadAllText(latest);
                }
            }
        }
        catch { }

        // 2) 再看启动日志
        var launchLog = Path.Combine(Path.GetTempPath(), "hearth-launch.log");
        var logText = "";
        try
        {
            if (File.Exists(launchLog)) logText = File.ReadAllText(launchLog);
        }
        catch { }

        var combined = crashText + "\n" + logText;

        foreach (var (pattern, title, reason, suggestion) in Rules)
        {
            if (Regex.IsMatch(combined, pattern, RegexOptions.IgnoreCase))
            {
                return new Result(title, reason, suggestion,
                    string.IsNullOrEmpty(crashPath) ? launchLog : crashPath, true);
            }
        }

        // 3) 认不出来：如实说，不给假结论
        var who = exitCode is > 0
            ? "游戏进程退出了（退出码 " + exitCode + "）"
            : "游戏进程异常退出了";

        return new Result("游戏崩溃了", who + "，但日志里没有能一眼认出来的原因。",
            "把下面这份日志发给作者，或者自己翻翻最上面几行异常。\n" +
            "如果刚装了新 mod，先把它删掉试试。",
            string.IsNullOrEmpty(crashPath) ? launchLog : crashPath, false);
    }

    /// <summary>取日志末尾若干行，给弹窗显示</summary>
    public static string ReadTail(string path, int maxChars = 1800)
    {
        try
        {
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path);
            if (text.Length <= maxChars) return text.Trim();
            return "…" + text[^maxChars..].Trim();
        }
        catch
        {
            return "";
        }
    }
}
