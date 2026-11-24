using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BlockLauncher;

/// <summary>
/// 启动前处理游戏设置文件（options.txt）。
/// 照 PCL：只在「首次启动」时改，避免覆盖玩家或整合包自带的设置。
/// </summary>
public static class GameOptions
{
    /// <summary>版本是不是老格式（1.0 ~ 1.12 用 zh_CN，1.13+ 用 zh_cn，写错会变英文）</summary>
    private static bool IsLegacyLanguageFormat(string versionId)
    {
        if (!versionId.StartsWith("1.", StringComparison.Ordinal)) return false;   // 26.x 这类新版本按新规则

        var rest = versionId[2..];
        var dot = rest.IndexOf('.');
        var minorText = dot > 0 ? rest[..dot] : rest;
        return int.TryParse(minorText, out var minor) && minor <= 12;
    }

    public static void EnsureFirstLaunch(string gameDir, string versionId)
    {
        try
        {
            var optionsPath = Path.Combine(gameDir, "options.txt");
            var savesDir = Path.Combine(gameDir, "saves");

            // 已经有存档 = 不是第一次，别动（PCL 的 #3844：整合包可能自带 options.txt）
            if (File.Exists(optionsPath) && Directory.Exists(savesDir) &&
                Directory.GetDirectories(savesDir).Length > 0)
                return;

            var lines = File.Exists(optionsPath)
                ? File.ReadAllLines(optionsPath).ToList()
                : new List<string>();

            void Set(string key, string value)
            {
                var prefix = key + ":";
                for (var i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        lines[i] = prefix + value;
                        return;
                    }
                }
                lines.Add(prefix + value);
            }

            Set("lang", IsLegacyLanguageFormat(versionId) ? "zh_CN" : "zh_cn");

            // 关掉首次进游戏的提示，PCL 也会顺手处理这些
            Set("skipMultiplayerWarning", "true");
            Set("onboardAccessibility", "false");
            Set("tutorialStep", "none");
            Set("telemetryOptInExtra", "false");

            Directory.CreateDirectory(gameDir);
            File.WriteAllLines(optionsPath, lines);
        }
        catch
        {
            // 写失败不影响启动，大不了还是英文
        }
    }
}
