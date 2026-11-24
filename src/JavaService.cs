using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>找不到对应版本的 Java 时，从 Adoptium 自动下</summary>
public static class JavaService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    static JavaService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/0.5.0");
    }

    /// <summary>确保有指定大版本的 Java，返回 java.exe 路径</summary>
    public static async Task<string> EnsureJavaAsync(
        int major, IProgress<(string Msg, double Pct)>? progress, CancellationToken ct = default)
    {
        var local = LaunchService.FindJava(major);
        if (local != null) return local;

        var dir = Path.Combine(VersionService.McDir, "runtime", "jdk-" + major);
        var exe = Path.Combine(dir, "bin", "java.exe");
        if (File.Exists(exe)) return exe;

        var tempRoot = Path.Combine(Path.GetTempPath(), "blocklauncher");
        Directory.CreateDirectory(tempRoot);
        var zip = Path.Combine(tempRoot, "jdk-" + major + ".zip");

        if (!File.Exists(zip) || new FileInfo(zip).Length < 10 * 1024 * 1024 || !LooksLikeZip(zip))
        {
            progress?.Report(("正在获取 Java " + major + " 下载地址…", 2));
            var link = await GetPackageLinkAsync(major, ct);

            progress?.Report(("正在下载 Java " + major + "（约 100 MB）…", 5));
            await DownloadAsync(link, zip, ct);

            if (!LooksLikeZip(zip))
            {
                try { File.Delete(zip); } catch { }
                throw new Exception("下载到的不是 zip，可能被网络拦截了");
            }
        }

        progress?.Report(("正在解压 Java " + major + "…", 70));
        var extractDir = Path.Combine(tempRoot, "jdk-" + major + "-unzip");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(zip, extractDir, true);

        var root = FindJavaRoot(extractDir);
        if (root == null) throw new Exception("解压完了没找到 java.exe");

        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.GetDirectoryName(dir)!);
        CopyDirectory(root, dir);

        progress?.Report(("Java " + major + " 就绪", 100));
        return Path.Combine(dir, "bin", "java.exe");
    }

    /// <summary>
    /// 拿 Java 下载地址。清华镜像优先——Adoptium 的包在 GitHub 上，国内经常被拦成 HTML。
    /// </summary>
    private static async Task<string> GetPackageLinkAsync(int major, CancellationToken ct)
    {
        var mirror = "https://mirrors.tuna.tsinghua.edu.cn/Adoptium/" + major + "/jdk/x64/windows/";
        try
        {
            var html = await Http.GetStringAsync(mirror, ct);
            var matches = System.Text.RegularExpressions.Regex.Matches(html, "href=\"([^\"]+\\.zip)\"");
            if (matches.Count > 0)
                return mirror + matches[^1].Groups[1].Value;
        }
        catch { }

        var url = "https://api.adoptium.net/v3/assets/latest/" + major +
                  "/hotspot?os=windows&architecture=x64&image_type=jdk";
        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var link = doc.RootElement[0].GetProperty("binary").GetProperty("package").GetProperty("link").GetString();
        if (string.IsNullOrEmpty(link)) throw new Exception("没拿到 Java " + major + " 的下载地址");
        return link;
    }

    private static bool LooksLikeZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
        }
        catch { return false; }
    }

    private static string? FindJavaRoot(string dir)
    {
        try
        {
            var direct = Path.Combine(dir, "bin", "java.exe");
            if (File.Exists(direct)) return dir;

            foreach (var sub in Directory.GetDirectories(dir))
            {
                var found = FindJavaRoot(sub);
                if (found != null) return found;
            }
        }
        catch { }
        return null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);

        foreach (var sub in Directory.GetDirectories(source))
            CopyDirectory(sub, Path.Combine(target, Path.GetFileName(sub)));
    }

    private static async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await resp.Content.CopyToAsync(fs, ct);
    }
}
