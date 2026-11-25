using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BlockLauncher;

/// <summary>
/// 读版本 JSON，得出「Java 版本 + 加载器」。
/// 沿 inheritsFrom 一路往上找：整合包往往只是一层薄壳，
/// mainClass 和 libraries 都在父版本里，只看自己会把它当成原版。
/// </summary>
public static class VersionDescribe
{
    public static (string Java, string Loader) Describe(string mcDir, string id)
    {
        var ids = new List<string>();
        var mains = new List<string>();
        var libsText = new StringBuilder();
        var java = "";

        var current = id;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!string.IsNullOrWhiteSpace(current) && ids.Count < 12 && seen.Add(current))
        {
            ids.Add(current);
            var jsonPath = Path.Combine(mcDir, "versions", current, current + ".json");
            if (!File.Exists(jsonPath)) break;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;

                if (string.IsNullOrEmpty(java) &&
                    root.TryGetProperty("javaVersion", out var jv) &&
                    jv.TryGetProperty("majorVersion", out var jm) &&
                    jm.TryGetInt32(out var major))
                {
                    java = major.ToString();
                }

                if (root.TryGetProperty("mainClass", out var mcEl) &&
                    mcEl.GetString() is { Length: > 0 } mainClass)
                {
                    mains.Add(mainClass);
                }

                if (root.TryGetProperty("libraries", out var libs) &&
                    libs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var lib in libs.EnumerateArray())
                    {
                        if (lib.TryGetProperty("name", out var n) && n.GetString() is { } libName)
                            libsText.AppendLine(libName);
                    }
                }

                var parent = root.TryGetProperty("inheritsFrom", out var inh) ? inh.GetString() : null;
                current = string.IsNullOrWhiteSpace(parent) ? "" : parent;
            }
            catch { break; }
        }

        var idText = string.Join(" ", ids).ToLowerInvariant();
        var mainLower = string.Join("\n", mains).ToLowerInvariant();
        var libLower = libsText.ToString().ToLowerInvariant();

        var loader = DetectLoader(idText, mainLower, libLower);

        if (string.IsNullOrEmpty(java))
        {
            // 1.16 及以前的版本 JSON 大多没写 javaVersion，按游戏版本推断一个能用的
            java = GuessJava(FindGameVersion(idText)).ToString();
        }

        return (java, loader);
    }

    private static string DetectLoader(string idText, string mainLower, string libLower)
    {
        // 只看加载器包名：整合包的 libraries 里常有 net.fabricmc:sponge-mixin 这类依赖，
        // 用宽泛的 "net.fabricmc" 会把 NeoForge 整合包误判成 Fabric
        if (idText.Contains("fabric-loader") || mainLower.Contains("fabricmc") ||
            libLower.Contains("net.fabricmc:fabric-loader"))
            return "Fabric";

        if (idText.Contains("quilt-loader") || mainLower.Contains("quiltmc") ||
            libLower.Contains("org.quiltmc:quilt-loader"))
            return "Quilt";

        if (mainLower.Contains("neoforged") || libLower.Contains("net.neoforged"))
            return "NeoForge";

        if (idText.Contains("-forge-") || mainLower.Contains("minecraftforge") || libLower.Contains("net.minecraftforge"))
            return "Forge";

        if (idText.Contains("optifine") || mainLower.Contains("optifine") || libLower.Contains("optifine"))
            return "OptiFine";

        // 1.12 及以下的 Forge / OptiFine 整合包经常只剩 launchwrapper，libs 被裁过
        if (mainLower.Contains("launchwrapper") || libLower.Contains("launchwrapper"))
            return "旧版加载器";

        // 现代加载器都走这个入口，libs 被裁掉时就分不清 Forge 还是 NeoForge
        if (mainLower.Contains("bootstraplauncher"))
            return "Forge / NeoForge";

        return "原版";
    }

    /// <summary>从版本 id / 父版本 id 里挑出像 1.12.2 的那一段</summary>
    private static string? FindGameVersion(string idText)
    {
        foreach (var token in idText.Split(new[] { ' ', '/', '\\', '(', ')', '[', ']', '，', '。' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = token.Trim();
            var parts = t.Split('.');
            if (parts.Length < 2) continue;
            if (int.TryParse(parts[0], out var major) && major >= 1 && major <= 99)
                return t;
        }
        return null;
    }

    private static int GuessJava(string? gameVersion)
    {
        if (string.IsNullOrEmpty(gameVersion)) return 17;
        var parts = gameVersion.Split('.');
        if (!int.TryParse(parts[0], out var major)) return 17;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;

        if (major == 1 && minor <= 16) return 8;
        if (major == 1 && minor == 17) return 17;
        if (major == 1 && minor <= 20 && patch <= 4) return 17;
        return 21;
    }
}
