using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlockLauncher;

/// <summary>游玩记录：哪个版本什么时候启动过</summary>
public static class PlayHistory
{
    public record Entry(string VersionId, DateTime When);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "history.json");

    private static List<Entry>? _cache;

    public static List<Entry> All()
    {
        if (_cache != null) return _cache;

        try
        {
            if (File.Exists(FilePath))
            {
                _cache = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? new();
                return _cache;
            }
        }
        catch { }

        _cache = new List<Entry>();
        return _cache;
    }

    /// <summary>最近玩过的几个版本（去重，按时间倒序）</summary>
    public static List<Entry> Recent(int count)
    {
        return All()
            .GroupBy(e => e.VersionId)
            .Select(g => g.OrderByDescending(e => e.When).First())
            .OrderByDescending(e => e.When)
            .Take(count)
            .ToList();
    }

    public static Entry? Last()
    {
        return Recent(1).FirstOrDefault();
    }

    /// <summary>记一次启动</summary>
    public static void Add(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId)) return;

        var list = All();
        list.Add(new Entry(versionId, DateTime.Now));

        // 只留最近 200 条，别无限长
        if (list.Count > 200)
            list.RemoveRange(0, list.Count - 200);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>把「1.21.4」这种 id 换成版本设置里自定义的名字</summary>
    public static string DisplayName(string versionId)
    {
        try
        {
            var path = Path.Combine(VersionService.McDir, "versions", versionId, versionId + ".json");
            if (!File.Exists(path)) return versionId;

            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            var name = node?["name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name) && name != versionId) return name!;
        }
        catch { }

        return versionId;
    }
}
