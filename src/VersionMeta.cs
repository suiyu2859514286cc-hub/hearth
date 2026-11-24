using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlockLauncher;

/// <summary>单个版本的设置：描述、隔离、内存、Java 等，存 version-meta.json</summary>
public static class VersionMeta
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Hearth", "version-meta.json");

    private static Dictionary<string, Dictionary<string, string>>? _data;

    private static Dictionary<string, Dictionary<string, string>> Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                _data = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                        File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch { _data = new(); }
            return _data;
        }
    }

    public static string Get(string versionId, string key, string fallback = "")
        => Data.TryGetValue(versionId, out var d) && d.TryGetValue(key, out var v) ? v : fallback;

    public static void Set(string versionId, string key, string value)
    {
        if (!Data.TryGetValue(versionId, out var d))
        {
            d = new Dictionary<string, string>();
            Data[versionId] = d;
        }
        d[key] = value;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>版本隔离：默认开，存档/mod 都放 versions/&lt;id&gt; 下（PCL 的做法）</summary>
    public static bool IsIsolated(string versionId)
        => Get(versionId, "isolation", "1") == "1";

    /// <summary>这个版本实际使用的游戏目录：隔离时是版本自己的文件夹</summary>
    public static string GameDir(string versionId)
    {
        var mc = VersionService.McDir;
        return IsIsolated(versionId) ? Path.Combine(mc, "versions", versionId) : mc;
    }

    public static void Remove(string versionId)
    {
        if (!Data.Remove(versionId)) return;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
