using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>
/// 整合包导入。格式判定顺序照 PCL：Modrinth → CurseForge → 完整 .minecraft 目录。
/// </summary>
public static class PackService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    static PackService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/0.6.0");
    }

    public static string McDir => VersionService.McDir;

    public record PackInfo(
        string Type,          // minecraft / modrinth / curseforge
        string Name,
        string McVersion,
        string Loader,        // fabric / forge / neoforge / ""
        string LoaderVersion,
        string SourcePath,
        bool IsFolder,
        int FileCount);

    /// <summary>识别一个整合包（文件夹或 zip）</summary>
    public static PackInfo? Inspect(string path)
    {
        try
        {
            if (Directory.Exists(path)) return InspectFolder(path);
            if (File.Exists(path)) return InspectZip(path);
        }
        catch { }
        return null;
    }

    private static PackInfo? InspectFolder(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));

        // 完整 .minecraft 目录
        if (Directory.Exists(Path.Combine(folder, "versions")))
        {
            var versions = Directory.GetDirectories(Path.Combine(folder, "versions"))
                .Select(Path.GetFileName).Where(v => v != null).ToList();
            return new PackInfo("minecraft", name, "", "", "", folder, true, versions.Count);
        }

        // 文件夹里直接放了一个版本
        if (Directory.GetFiles(folder, "*.json").Any(f => File.Exists(Path.Combine(folder, Path.GetFileNameWithoutExtension(f) + ".jar"))))
            return new PackInfo("minecraft", name, "", "", "", folder, true, 1);

        return null;
    }

    private static PackInfo? InspectZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

        // 根目录或一层子目录里的关键文件
        string? Find(string file)
        {
            var direct = names.FirstOrDefault(n => n.Equals(file, StringComparison.OrdinalIgnoreCase));
            if (direct != null) return direct;
            return names.FirstOrDefault(n =>
                n.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase) &&
                n.Count(c => c == '/') == 1);
        }

        var mrIndex = Find("modrinth.index.json");
        if (mrIndex != null) return ReadModrinth(zip, mrIndex, zipPath);

        var cfManifest = Find("manifest.json");
        if (cfManifest != null) return ReadCurseForge(zip, cfManifest, zipPath);

        if (names.Any(n => n.StartsWith("versions/", StringComparison.OrdinalIgnoreCase)) ||
            names.Any(n => n.Contains("/versions/")))
            return new PackInfo("minecraft", Path.GetFileNameWithoutExtension(zipPath), "", "", "", zipPath, false, 0);

        return null;
    }

    private static PackInfo ReadModrinth(ZipArchive zip, string indexEntry, string path)
    {
        var entry = zip.GetEntry(indexEntry)!;
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "整合包" : "整合包";
        var mc = "";
        var loader = "";
        var loaderVersion = "";
        if (root.TryGetProperty("dependencies", out var deps))
        {
            foreach (var d in deps.EnumerateObject())
            {
                if (d.Name.Equals("minecraft", StringComparison.OrdinalIgnoreCase)) mc = d.Value.GetString() ?? "";
                else if (d.Name.Contains("fabric", StringComparison.OrdinalIgnoreCase)) { loader = "fabric"; loaderVersion = d.Value.GetString() ?? ""; }
                else if (d.Name.Contains("forge", StringComparison.OrdinalIgnoreCase))
                {
                    loader = d.Name.Contains("neo", StringComparison.OrdinalIgnoreCase) ? "neoforge" : "forge";
                    loaderVersion = d.Value.GetString() ?? "";
                }
            }
        }

        var fileCount = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
            ? files.GetArrayLength() : 0;

        return new PackInfo("modrinth", name, mc, loader, loaderVersion, path, false, fileCount);
    }

    private static PackInfo ReadCurseForge(ZipArchive zip, string manifestEntry, string path)
    {
        var entry = zip.GetEntry(manifestEntry)!;
        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "整合包" : "整合包";
        var mc = "";
        var loader = "";
        var loaderVersion = "";
        if (root.TryGetProperty("minecraft", out var m))
        {
            if (m.TryGetProperty("version", out var v)) mc = v.GetString() ?? "";
            if (m.TryGetProperty("modLoaders", out var mods) && mods.GetArrayLength() > 0)
            {
                var id = mods[0].TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                var lower = id.ToLowerInvariant();
                if (lower.StartsWith("forge-")) { loader = "forge"; loaderVersion = id[6..]; }
                else if (lower.StartsWith("fabric-")) { loader = "fabric"; loaderVersion = id[7..]; }
                else if (lower.StartsWith("neoforge-")) { loader = "neoforge"; loaderVersion = id[9..]; }
            }
        }

        var fileCount = root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array
            ? files.GetArrayLength() : 0;

        return new PackInfo("curseforge", name, mc, loader, loaderVersion, path, false, fileCount);
    }

    /// <summary>安装整合包：装依赖版本 → 铺文件 → 返回装好的版本 id 或说明</summary>
    public static async Task<string> InstallAsync(
        PackInfo info, IProgress<(string Msg, double Pct)>? progress,
        string? instanceName = null, CancellationToken ct = default)
    {
        var mc = McDir;
        Directory.CreateDirectory(mc);

        if (info.Type == "minecraft")
        {
            progress?.Report(("正在复制游戏文件…", 5));
            await Task.Run(() => CopyPackInto(info, mc, progress), ct);
            progress?.Report(("导入完成", 100));
            return "已导入到 .minecraft（版本列表里能看到新版本）";
        }

        // Modrinth / CurseForge：先装原版和加载器
        var installedVersion = "";
        if (!string.IsNullOrEmpty(info.McVersion))
        {
            progress?.Report(("正在安装 " + info.McVersion + "…", 4));
            var baseDir = Path.Combine(mc, "versions", info.McVersion);
            if (!File.Exists(Path.Combine(baseDir, info.McVersion + ".json")))
            {
                await InstallService.InstallAsync(info.McVersion, true, progress, ct);
            }
            installedVersion = info.McVersion;

            if (!string.IsNullOrEmpty(info.Loader))
            {
                // 已经装过就别重装（Forge 装一次要打几万个补丁）
                var candidates = info.Loader switch
                {
                    "fabric" => new[] { "fabric-loader-" + info.LoaderVersion + "-" + info.McVersion },
                    "neoforge" => new[] { info.McVersion + "-neoforge-" + info.LoaderVersion, info.McVersion + "-forge-" + info.LoaderVersion },
                    _ => new[] { info.McVersion + "-forge-" + info.LoaderVersion }
                };
                var existing = candidates.FirstOrDefault(id =>
                    File.Exists(Path.Combine(mc, "versions", id, id + ".json")));

                if (existing != null)
                {
                    installedVersion = existing;
                    progress?.Report(("加载器已经装过：" + existing, 50));
                }
                else
                {
                    try
                    {
                        progress?.Report(("正在安装 " + info.Loader + "…", 45));
                        installedVersion = info.Loader switch
                        {
                            "fabric" => await LoaderService.InstallFabricAsync(info.McVersion, info.LoaderVersion, progress, ct),
                            "neoforge" => await LoaderService.InstallNeoForgeAsync(info.McVersion, info.LoaderVersion, progress, ct),
                            _ => await LoaderService.InstallForgeAsync(info.McVersion, info.LoaderVersion, progress, ct)
                        };
                    }
                    catch (Exception ex)
                    {
                        progress?.Report(("加载器安装失败：" + ex.Message + "，继续铺文件", 50));
                    }
                }
            }
        }

        // 整合包做成一个独立实例：versions/<名字>/，里面放继承加载器的 json + 文件
        var name = SanitizeName(string.IsNullOrWhiteSpace(instanceName) ? info.Name : instanceName);
        var instanceDir = Path.Combine(mc, "versions", name);
        Directory.CreateDirectory(instanceDir);

        if (!string.IsNullOrEmpty(installedVersion))
        {
            var json = new
            {
                id = name,
                inheritsFrom = installedVersion,
                type = "release",
                releaseTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss+00:00"),
                libraries = Array.Empty<object>(),
                arguments = new { game = Array.Empty<object>(), jvm = Array.Empty<object>() }
            };
            await File.WriteAllTextAsync(
                Path.Combine(instanceDir, name + ".json"),
                JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }), ct);
        }

        // 铺 overrides / 下载 files
        progress?.Report(("正在铺整合包文件…", 70));
        await Task.Run(() => ExtractOverrides(info, instanceDir), ct);

        if (info.Type == "modrinth")
        {
            progress?.Report(("正在下载整合包模组…", 80));
            await DownloadModrinthFiles(info, instanceDir, progress, ct);
        }

        progress?.Report(("导入完成", 100));
        return info.Type == "curseforge"
            ? "已创建整合包「" + name + "」。CurseForge 的模组需要用它们的启动器补（我们没有它的接口）"
            : "已创建整合包「" + name + "」，模组已铺进 versions 下的这个文件夹";
    }

    /// <summary>版本文件夹名不能带这些字符</summary>
    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(name) ? "整合包" : name;
    }

    private static void CopyPackInto(PackInfo info, string mc, IProgress<(string Msg, double Pct)>? progress)
    {
        var source = info.SourcePath;
        var temp = "";

        if (!info.IsFolder)
        {
            temp = Path.Combine(Path.GetTempPath(), "blocklauncher", "pack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(source, temp, true);
            source = FindMinecraftRoot(temp) ?? temp;
        }

        var total = 0L;
        try { total = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { }

        var copied = 0L;
        var lastPct = -1;
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(mc, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                File.Copy(file, dest, true);
                copied += new FileInfo(file).Length;
                if (total > 0)
                {
                    var pct = (int)(5 + 90.0 * copied / total);
                    if (pct != lastPct) { lastPct = pct; progress?.Report(("正在复制… " + pct + "%", pct)); }
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(temp))
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    private static string? FindMinecraftRoot(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, "versions"))) return dir;
        foreach (var sub in Directory.GetDirectories(dir))
        {
            var found = FindMinecraftRoot(sub);
            if (found != null) return found;
        }
        return null;
    }

    private static void ExtractOverrides(PackInfo info, string mc)
    {
        using var zip = ZipFile.OpenRead(info.SourcePath);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();

        // overrides/ 在根目录，或者被一层文件夹套着
        const string marker = "/overrides/";
        var basePrefix = "overrides/";
        if (!names.Any(x => x.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)))
        {
            var nested = names.FirstOrDefault(x => x.Contains(marker, StringComparison.OrdinalIgnoreCase));
            if (nested == null) return;
            var idx = nested.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            basePrefix = nested[..(idx + marker.Length)];
        }

        foreach (var entry in zip.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            if (!full.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (full.EndsWith("/")) continue;

            var rel = full[basePrefix.Length..];
            if (string.IsNullOrWhiteSpace(rel)) continue;

            var dest = Path.Combine(mc, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    private static async Task DownloadModrinthFiles(
        PackInfo info, string mc, IProgress<(string Msg, double Pct)>? progress, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(info.SourcePath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').EndsWith("modrinth.index.json", StringComparison.OrdinalIgnoreCase));
        if (entry == null) return;

        using var stream = entry.Open();
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("files", out var files)) return;

        var list = files.EnumerateArray().ToList();
        var done = 0;
        foreach (var file in list)
        {
            ct.ThrowIfCancellationRequested();
            var path = file.TryGetProperty("path", out var p) ? p.GetString() : null;
            var url = "";
            if (file.TryGetProperty("downloads", out var downloads) && downloads.GetArrayLength() > 0)
                url = downloads[0].GetString() ?? "";
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(url)) continue;

            var dest = Path.Combine(mc, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(dest))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();
                    await using var fs = File.Create(dest);
                    await resp.Content.CopyToAsync(fs, ct);
                }
                catch { }
            }

            done++;
            progress?.Report(("正在下载模组（" + done + "/" + list.Count + "）…", 80 + 18.0 * done / Math.Max(1, list.Count)));
        }
    }
}
