using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>加载器安装：Fabric / Forge / NeoForge</summary>
public static class LoaderService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    public const string MirrorBase = "https://bmclapi2.bangbang93.com";

    static LoaderService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/0.4.0");
    }

    public static string McDir => VersionService.McDir;

    /// <summary>OptiFine 版本（BMCLAPI 提供列表和镜像）</summary>
    public record OptiFineVersion(string McVersion, string Type, string Patch, string FileName, string ForgeHint)
    {
        public string Display => Type.Replace("HD_U", "") + " " + Patch;
    }

    public record LoaderVersion(string Version, bool Stable, string Label, string? Raw = null)
    {
        /// <summary>接口里用的原始名（NeoForge 的 1.20.1-47.1.84 这种）</summary>
        public string ApiName => string.IsNullOrEmpty(Raw) ? Version : Raw!;
    }

    /// <summary>Maven 坐标 → 文件相对路径：group:artifact:version[:classifier]</summary>
    public static string MavenPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return "";
        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? parts[3] : null;
        var file = artifact + "-" + version + (classifier != null ? "-" + classifier : "") + ".jar";
        return group + "/" + artifact + "/" + version + "/" + file;
    }

    // ===== Fabric =====

    /// <summary>OptiFine 版本列表（按 MC 版本）</summary>
    public static async Task<List<OptiFineVersion>> GetOptiFineVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        var list = new List<OptiFineVersion>();
        try
        {
            var json = await Http.GetStringAsync("https://bmclapi2.bangbang93.com/optifine/" + mcVersion, ct);
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var patch = item.TryGetProperty("patch", out var p) ? p.GetString() ?? "" : "";
                var file = item.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "";
                var forge = item.TryGetProperty("forge", out var fo) ? fo.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(patch))
                    list.Add(new OptiFineVersion(mcVersion, type, patch, file, forge));
            }
        }
        catch { }

        return list;
    }

    /// <summary>下载 OptiFine 的 jar（BMCLAPI 镜像，官方源要过验证页）</summary>
    public static async Task<string> DownloadOptiFineAsync(OptiFineVersion version, string directory,
        IProgress<(string Msg, double Pct)>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, version.FileName);

        var url = "https://bmclapi2.bangbang93.com/optifine/" +
                  version.McVersion + "/" + version.Type + "/" + version.Patch;

        progress?.Report(("正在下载 OptiFine " + version.Display + "…", 10));

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var fs = File.Create(dest);
        await using var input = await resp.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long done = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read <= 0) break;
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                progress?.Report(("正在下载 OptiFine（" + (done / 1024 / 1024) + " / " + (total / 1024 / 1024) + " MB）", 10 + 85.0 * done / total));
        }

        progress?.Report(("OptiFine 下载完成", 100));
        return dest;
    }

    public static async Task<List<LoaderVersion>> GetFabricVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync(
            "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion, ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<LoaderVersion>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var loader = item.GetProperty("loader");
            var ver = loader.GetProperty("version").GetString() ?? "";
            var stable = loader.TryGetProperty("stable", out var s) && s.GetBoolean();
            list.Add(new LoaderVersion(ver, stable, stable ? ver + "（稳定）" : ver));
        }
        return list;
    }

    /// <summary>装 Fabric：拿 profile json，存成版本，再把它的库下下来</summary>
    public static async Task<string> InstallFabricAsync(
        string mcVersion, string loaderVersion,
        IProgress<(string Msg, double Pct)>? progress, CancellationToken ct = default)
    {
        progress?.Report(("正在获取 Fabric 配置…", 2));
        var profile = await Http.GetStringAsync(
            "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion + "/" + loaderVersion + "/profile/json", ct);

        using var doc = JsonDocument.Parse(profile);
        var id = doc.RootElement.GetProperty("id").GetString()!;
        var vDir = Path.Combine(McDir, "versions", id);
        Directory.CreateDirectory(vDir);
        await File.WriteAllTextAsync(Path.Combine(vDir, id + ".json"), profile, ct);

        var libs = doc.RootElement.GetProperty("libraries").EnumerateArray().ToList();
        var done = 0;
        foreach (var lib in libs)
        {
            ct.ThrowIfCancellationRequested();
            var name = lib.GetProperty("name").GetString()!;
            var baseUrl = lib.TryGetProperty("url", out var u) ? u.GetString() : "https://maven.fabricmc.net/";
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = "https://maven.fabricmc.net/";
            var path = MavenPath(name);
            if (string.IsNullOrEmpty(path)) continue;

            var dest = Path.Combine(McDir, "libraries", path);
            await DownloadAsync(baseUrl!.TrimEnd('/') + "/" + path, dest, ct);

            done++;
            progress?.Report(($"正在下载 Fabric 库（{done}/{libs.Count}）…", 5 + 90.0 * done / Math.Max(1, libs.Count)));
        }

        progress?.Report(("Fabric 安装完成", 100));
        return id;
    }

    // ===== Forge / NeoForge 的版本列表 =====

    public static async Task<List<LoaderVersion>> GetForgeVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync(MirrorBase + "/forge/minecraft/" + mcVersion, ct);

        // Forge 官方会给每个 MC 版本一个 recommended（PCL 也是这么标的）
        var recommended = "";
        try
        {
            var promos = await Http.GetStringAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct);
            using var pd = JsonDocument.Parse(promos);
            if (pd.RootElement.TryGetProperty("promos", out var promosEl) &&
                promosEl.TryGetProperty(mcVersion + "-recommended", out var rec))
                recommended = rec.GetString() ?? "";
        }
        catch { }

        using var doc = JsonDocument.Parse(json);
        var list = new List<LoaderVersion>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var ver = item.GetProperty("version").GetString() ?? "";
            list.Add(new LoaderVersion(ver, ver == recommended, ver));
        }
        return SortDesc(list);
    }

    /// <summary>装 Forge：下载 installer，用 --installClient 跑（1.13+ 都支持）</summary>
    public static async Task<string> InstallForgeAsync(
        string mcVersion, string forgeVersion,
        IProgress<(string Msg, double Pct)>? progress, CancellationToken ct = default)
    {
        var id = mcVersion + "-forge-" + forgeVersion;
        var fileVersion = mcVersion + "-" + forgeVersion;
        var pathPart = "/maven/net/minecraftforge/forge/" + fileVersion + "/forge-" + fileVersion + "-installer.jar";
        var mirror = MirrorBase + pathPart;
        var official = "https://maven.minecraftforge.net/net/minecraftforge/forge/" + fileVersion +
                       "/forge-" + fileVersion + "-installer.jar";

        var cacheDir = Path.Combine(Path.GetTempPath(), "blocklauncher");
        Directory.CreateDirectory(cacheDir);
        var installer = Path.Combine(cacheDir, "forge-" + fileVersion + "-installer.jar");

        progress?.Report(("正在下载 Forge 安装器…", 3));
        await DownloadWithFallbackAsync(mirror, official, installer, ct);

        var java = LaunchService.FindJava(17) ?? LaunchService.FindJava();
        if (java == null) throw new Exception("没找到 Java 17，Forge 安装器需要它");

        progress?.Report(("正在运行 Forge 安装器…", 8));
        var psi = new ProcessStartInfo(java)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(McDir);

        using var proc = Process.Start(psi) ?? throw new Exception("安装器没起来");
        var lines = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lines++;
            if (lines % 25 == 0)
                progress?.Report(("正在打补丁…（" + lines + " 步）", 8 + Math.Min(87, lines / 4.0)));
        };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0) throw new Exception("Forge 安装器退出码 " + proc.ExitCode);
        if (!File.Exists(Path.Combine(McDir, "versions", id, id + ".json")))
            throw new Exception("安装器跑完了，但没生成 " + id);

        progress?.Report(("Forge 安装完成", 100));
        return id;
    }

    /// <summary>按版本号从新到旧排（BMCLAPI 返回的顺序是乱的）</summary>
    public static List<LoaderVersion> SortDesc(List<LoaderVersion> list)
        => list.OrderByDescending(v => ParseVersion(v.Version)).ToList();

    private static Version ParseVersion(string text)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        return Version.TryParse(cleaned, out var v) ? v : new Version(0, 0);
    }

    // ===== NeoForge =====

    public static async Task<List<LoaderVersion>> GetNeoForgeVersionsAsync(string mcVersion, CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync(MirrorBase + "/neoforge/list/" + mcVersion, ct);
        using var doc = JsonDocument.Parse(json);
        var list = new List<LoaderVersion>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var ver = item.GetProperty("version").GetString() ?? "";
            var raw = item.TryGetProperty("rawVersion", out var rv) ? rv.GetString() ?? ver : ver;
            list.Add(new LoaderVersion(ver, false, ver, raw));
        }
        return SortDesc(list);
    }

    /// <summary>
    /// 装 NeoForge。1.20.1 的包名是 forge（PCL 里的 legacy 分支），其余是 neoforge。
    /// </summary>
    public static async Task<string> InstallNeoForgeAsync(
        string mcVersion, string apiName,
        IProgress<(string Msg, double Pct)>? progress, CancellationToken ct = default)
    {
        var package = mcVersion == "1.20.1" ? "forge" : "neoforge";
        var display = apiName.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase)
            ? apiName[(mcVersion.Length + 1)..]
            : apiName;
        var id = mcVersion + "-neoforge-" + display;
        var file = package + "-" + apiName + "-installer.jar";
        var mirror = MirrorBase + "/maven/net/neoforged/" + package + "/" + apiName + "/" + file;
        var official = "https://maven.neoforged.net/releases/net/neoforged/" + package + "/" + apiName + "/" + file;

        var cacheDir = Path.Combine(Path.GetTempPath(), "blocklauncher");
        Directory.CreateDirectory(cacheDir);
        var installer = Path.Combine(cacheDir, "neoforge-" + apiName + "-installer.jar");

        progress?.Report(("正在下载 NeoForge 安装器…", 3));
        await DownloadWithFallbackAsync(mirror, official, installer, ct);

        var java = LaunchService.FindJava(17) ?? LaunchService.FindJava();
        if (java == null) throw new Exception("没找到 Java 17，NeoForge 安装器需要它");

        progress?.Report(("正在运行 NeoForge 安装器…", 8));
        var psi = new ProcessStartInfo(java)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(McDir);

        using var proc = Process.Start(psi) ?? throw new Exception("安装器没起来");
        var lines = 0;
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lines++;
            if (lines % 25 == 0)
                progress?.Report(("正在打补丁…（" + lines + " 步）", 8 + Math.Min(87, lines / 4.0)));
        };
        proc.ErrorDataReceived += (_, _) => { };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0) throw new Exception("NeoForge 安装器退出码 " + proc.ExitCode);

        // 安装器生成的目录名可能不带 MC 前缀，找一下
        // 1.20.1 的 NeoForge 会生成 1.20.1-forge-47.x.x 这种目录名，新版才是 -neoforge-
        var versionRoot = Path.Combine(McDir, "versions");
        var found = Directory.Exists(versionRoot)
            ? Directory.GetDirectories(versionRoot)
                .Select(Path.GetFileName)
                .FirstOrDefault(n => n != null &&
                    (n.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ||
                     n.Contains("-forge-", StringComparison.OrdinalIgnoreCase)) &&
                    n.Contains(display, StringComparison.OrdinalIgnoreCase))
            : null;

        if (found == null) throw new Exception("安装器跑完了，但没找到 NeoForge 版本目录");

        progress?.Report(("NeoForge 安装完成", 100));
        return found;
    }

    private static async Task DownloadWithFallbackAsync(string mirror, string official, string dest, CancellationToken ct)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 1_000_000) return;

        // 设置里选了官方源就不走镜像
        var sources = AppSettings.DownloadSource == "official" ? new[] { official } : new[] { mirror, official };

        foreach (var url in sources)
        {
            try
            {
                await DownloadAsync(url, dest, ct);
                if (File.Exists(dest) && new FileInfo(dest).Length > 1_000_000) return;
            }
            catch
            {
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }
        }
        throw new Exception("安装器下载失败：" + official);
    }

    private static async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await resp.Content.CopyToAsync(fs, ct);
    }
}
