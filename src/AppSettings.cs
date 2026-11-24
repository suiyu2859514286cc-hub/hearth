using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlockLauncher;

/// <summary>全局设置，存 %APPDATA%\BlockLauncher\settings.json</summary>
public static class AppSettings
{
    /// <summary>启动器版本号（改这里，全项目跟着变）</summary>
    public const string Version = "0.2.3-beta";

    /// <summary>启动器更新模式：auto（自动检查）/ off（关闭提示）</summary>
    public static string UpdateMode => Get("updateMode", "auto");

    /// <summary>是否显示公告</summary>
    public static string Announcements => Get("announcements", "on");

    /// <summary>匿名数据收集</summary>
    public static string Telemetry => Get("telemetry", "on");

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Hearth", "settings.json");

    private static Dictionary<string, string>? _data;

    private static Dictionary<string, string> Data
    {
        get
        {
            if (_data != null) return _data;
            try
            {
                _data = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch { _data = new(); }
            return _data;
        }
    }

    public static string Get(string key, string fallback = "")
        => Data.TryGetValue(key, out var v) ? v : fallback;

    public static void Set(string key, string value)
    {
        Data[key] = value;
        Save();
    }

    public static int GetInt(string key, int fallback)
        => int.TryParse(Get(key), out var v) ? v : fallback;

    public static bool GetBool(string key, bool fallback)
        => Get(key) switch { "1" or "true" => true, "0" or "false" => false, _ => fallback };

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Data,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // 几个常用项
    public static string JavaPath => Get("javaPath", "");                 // 空 = 自动
    public static int DefaultMemory => GetInt("defaultMemory", 4096);
    public static string DownloadSource => Get("downloadSource", "auto"); // auto / official
    public static int DownloadThreads => GetInt("downloadThreads", 8);
    public static bool Animations => GetBool("animations", true);
}

/// <summary>内存分配：跟随全局 / 自动 / 自定义</summary>
public static class MemoryUtil
{
    /// <summary>自动配置：本机总内存的一半，取整到 512 MB，夹在 1G–8G</summary>
    public static int Auto()
    {
        try
        {
            var totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024L);
            var half = (int)(totalMb / 2);
            return Math.Clamp(half / 512 * 512, 1024, 8192);
        }
        catch { return 4096; }
    }

    /// <summary>某个版本最终用多少内存</summary>
    public static int Resolve(string versionId)
    {
        var mode = VersionMeta.Get(versionId, "memoryMode", "");
        if (mode == "")
        {
            // 旧数据兼容：存过 memory 就当自定义
            mode = VersionMeta.Get(versionId, "memory", "") != "" ? "custom" : "global";
        }

        return mode switch
        {
            "auto" => Auto(),
            "custom" => int.TryParse(
                VersionMeta.Get(versionId, "memoryValue", VersionMeta.Get(versionId, "memory", "4096")),
                out var v) && v > 0 ? v : 4096,
            _ => AppSettings.DefaultMemory
        };
    }
}
