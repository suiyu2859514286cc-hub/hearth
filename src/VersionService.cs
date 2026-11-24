using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>Mojang 版本清单（分类规则照 PCL：正式版 / 预览版 / 远古版 / 愚人节版）</summary>
public static class VersionService
{
    public record VersionInfo(string Id, string Sub, string TypeName, string Java, bool Installed, DateTime Date);

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth");
    private static readonly string CacheFile = Path.Combine(Dir, "versions_cache.json");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static VersionService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/0.2.0");
    }

    /// <summary>当前游戏文件夹（.minecraft），由 GameFolderService 管理</summary>
    public static string McDir
    {
        get
        {
            GameFolderService.Load();
            return GameFolderService.CurrentPath;
        }
    }

    /// <summary>本机是否装了某个版本</summary>
    public static bool IsInstalled(string id)
    {
        try
        {
            var json = Path.Combine(McDir, "versions", id, id + ".json");
            return File.Exists(json);
        }
        catch { return false; }
    }

    private record Raw(string Id, string Type, string ReleaseTime);

    // PCL 里写死的愚人节版本
    private static readonly HashSet<string> FoolIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "20w14infinite", "20w14∞", "3d shareware v1.34", "1.rv-pre1", "15w14a", "2.0",
        "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine", "26w14a"
    };

    /// <summary>拿版本清单：当天缓存优先，失败退过期缓存，再失败返回空</summary>
    public static async Task<List<VersionInfo>> GetVersionsAsync()
    {
        var all = new List<Raw>();

        // 1) 缓存 6 小时内直接用
        try
        {
            if (File.Exists(CacheFile) &&
                DateTime.Now - File.GetLastWriteTime(CacheFile) < TimeSpan.FromHours(6))
            {
                all = JsonSerializer.Deserialize<List<Raw>>(File.ReadAllText(CacheFile)) ?? new List<Raw>();
            }
        }
        catch { }

        // 2) 请求 Mojang
        if (all.Count == 0)
        {
            try
            {
                var json = await Http.GetStringAsync("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
                using var doc = JsonDocument.Parse(json);
                foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
                {
                    all.Add(new Raw(
                        v.GetProperty("id").GetString() ?? "",
                        v.TryGetProperty("type", out var t) ? (t.GetString() ?? "release") : "release",
                        v.TryGetProperty("releaseTime", out var rt) ? (rt.GetString() ?? "") : ""
                    ));
                }
                if (all.Count > 0)
                {
                    Directory.CreateDirectory(Dir);
                    File.WriteAllText(CacheFile, JsonSerializer.Serialize(all));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("版本清单请求失败: " + ex.Message);
            }
        }

        // 3) 还是空：磁盘上的过期缓存也能用
        if (all.Count == 0)
        {
            try { all = JsonSerializer.Deserialize<List<Raw>>(File.ReadAllText(CacheFile)) ?? new List<Raw>(); }
            catch { }
        }

        var list = all.Select(r =>
        {
            var date = DateTime.TryParse(r.ReleaseTime, out var d) ? d : DateTime.MinValue;
            var typeName = Classify(r, date);
            var sub = date == DateTime.MinValue
                ? typeName
                : $"{typeName} · {date.ToLocalTime():yyyy-MM-dd}";
            return new VersionInfo(r.Id, sub, typeName, JavaFor(r.Id), IsInstalled(r.Id), date);
        }).ToList();

        return list.OrderByDescending(v => v.Date).ToList();
    }

    /// <summary>分类：照抄 PCL 的判定，包括纠正 Mojang 的误分类</summary>
    private static string Classify(Raw r, DateTime date)
    {
        var id = r.Id;
        var lower = id.ToLowerInvariant();
        switch (r.Type)
        {
            case "release":
                return "正式版";

            case "snapshot":
                // Mojang 把某些普通版本误标成了快照
                if (id.StartsWith("1.") && !lower.Contains("combat") &&
                    !lower.Contains("rc") && !lower.Contains("experimental") && !lower.Contains("pre"))
                    return "正式版";

                if (FoolIds.Contains(id)) return "愚人节版";
                // 4 月 1 日发布的自动算愚人节版（PCL 用的是 UTC+2）
                var sh = date.ToUniversalTime().AddHours(2);
                if (sh.Month == 4 && sh.Day == 1) return "愚人节版";
                return "预览版";

            case "special":
                return "愚人节版";

            default:
                return "远古版";
        }
    }

    /// <summary>按版本号推一个合适的 Java 大版本</summary>
    public static string JavaFor(string id)
    {
        try
        {
            var s = id.Split('-')[0];
            if (!s.StartsWith("1.")) return "21";           // 新版命名（26.x 之类）
            var parts = s.Split('.');
            if (parts.Length < 2) return "8";
            if (!int.TryParse(parts[1], out var minor)) return "21";
            if (minor >= 20) return "21";
            if (minor >= 18) return "17";
            if (minor == 17) return "16";
            return "8";
        }
        catch { return "21"; }
    }
}
