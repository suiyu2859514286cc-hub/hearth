using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlockLauncher;

/// <summary>多个游戏文件夹（.minecraft）的列表与当前选择，照 PCL 的文件夹管理</summary>
public static class GameFolderService
{
    public record Folder(string Name, string Path);

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth");
    private static readonly string ConfigFile = Path.Combine(ConfigDir, "folders.json");

    /// <summary>
    /// 默认游戏目录：exe 旁边有 .minecraft 就用它（便携模式，整个文件夹能拷走），
    /// 否则用 %APPDATA%\.minecraft
    /// </summary>
    private static string DefaultPath
    {
        get
        {
            var portable = Path.Combine(AppContext.BaseDirectory, ".minecraft");
            if (Directory.Exists(portable)) return portable;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        }
    }

    public static List<Folder> Folders { get; private set; } = new();
    public static string CurrentPath { get; private set; } = DefaultPath;

    /// <summary>切换或改动文件夹后触发</summary>
    public static event Action? Changed;

    private static bool _loaded;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(ConfigFile))
            {
                var data = JsonSerializer.Deserialize<Store>(File.ReadAllText(ConfigFile));
                if (data != null)
                {
                    Folders = data.Folders ?? new List<Folder>();
                    CurrentPath = string.IsNullOrWhiteSpace(data.Current) ? DefaultPath : data.Current;
                }
            }
        }
        catch { }

        // exe 旁边出现 .minecraft：当成便携目录，自动加进来并切过去
        var portable = Path.Combine(AppContext.BaseDirectory, ".minecraft");
        if (Directory.Exists(portable) &&
            !Folders.Any(f => string.Equals(f.Path, portable, StringComparison.OrdinalIgnoreCase)))
        {
            Folders.Insert(0, new Folder("便携", portable));
            CurrentPath = portable;
        }

        if (Folders.Count == 0) Folders.Add(new Folder("默认", DefaultPath));
        if (!Folders.Any(f => string.Equals(f.Path, CurrentPath, StringComparison.OrdinalIgnoreCase)))
            CurrentPath = Folders[0].Path;
    }

    private class Store
    {
        public List<Folder>? Folders { get; set; }
        public string? Current { get; set; }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(new Store
            {
                Folders = Folders,
                Current = CurrentPath
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static Folder? Current =>
        Folders.FirstOrDefault(f => string.Equals(f.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>切换当前文件夹，不存在的目录会先建出来</summary>
    public static void Switch(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) Directory.CreateDirectory(full);

        if (!Folders.Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase)))
            Folders.Add(new Folder(Path.GetFileName(full.TrimEnd('\\', '/')), full));

        CurrentPath = full;
        Save();
        Changed?.Invoke();
    }

    /// <summary>添加一个已有文件夹（比如别的启动器的 .minecraft）</summary>
    public static void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full)) return;

        if (!Folders.Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase)))
        {
            Folders.Add(new Folder(Path.GetFileName(full.TrimEnd('\\', '/')), full));
            Save();
            Changed?.Invoke();
        }
        Switch(full);
    }

    /// <summary>从列表里移除（不删磁盘上的文件）</summary>
    public static void Remove(string path)
    {
        if (Folders.Count <= 1) return;
        var item = Folders.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item == null) return;

        Folders.Remove(item);
        if (string.Equals(CurrentPath, path, StringComparison.OrdinalIgnoreCase))
            CurrentPath = Folders[0].Path;

        Save();
        Changed?.Invoke();
    }

    /// <summary>这个文件夹里装了几个版本</summary>
    public static int CountVersions(string path)
    {
        try
        {
            var dir = Path.Combine(path, "versions");
            if (!Directory.Exists(dir)) return 0;
            return Directory.GetDirectories(dir).Count(d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".json")));
        }
        catch { return 0; }
    }
}
