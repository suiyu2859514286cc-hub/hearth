using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>检查 GitHub Releases 上有没有新版本</summary>
public static class UpdateService
{
    private const string Owner = "suiyu2859514286cc-hub";
    private const string Repo = "hearth";

    /// <summary>查 API 用：20 秒够了</summary>
    private static readonly HttpClient Http = CreateClient(TimeSpan.FromSeconds(20));

    /// <summary>下安装包用：不能设短超时，56 MB 会被掐断（之前就是这个 bug）</summary>
    private static readonly HttpClient DownloadClient = CreateClient(Timeout.InfiniteTimeSpan);

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var c = new HttpClient { Timeout = timeout };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hearth", AppSettings.Version));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public record ReleaseInfo(string Tag, string Name, string Notes, string DownloadUrl, long Size, string FileName);

    /// <summary>拿最新 release；没有 release 或出错时返回 null</summary>
    public static async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(
                "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest", ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(tag)) return null;

            // 优先挑「更新包」（名字带 Update），其次才是完整安装包
            string updateUrl = "", updateName = "", setupUrl = "", setupName = "";
            long updateSize = 0, setupSize = 0;

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() ?? "" : "";
                    var au = a.TryGetProperty("browser_download_url", out var auEl) ? auEl.GetString() ?? "" : "";
                    var asz = a.TryGetProperty("size", out var szEl) && szEl.TryGetInt64(out var sz) ? sz : 0;

                    if (string.IsNullOrEmpty(au)) continue;

                    if (an.Contains("Update", StringComparison.OrdinalIgnoreCase))
                    {
                        updateUrl = au;
                        updateName = an;
                        updateSize = asz;
                    }
                    else if (an.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        setupUrl = au;
                        setupName = an;
                        setupSize = asz;
                    }
                }
            }

            // 有更新包就下更新包（不用重装），没有才退回安装包
            var url = !string.IsNullOrEmpty(updateUrl) ? updateUrl : setupUrl;
            var fileName = !string.IsNullOrEmpty(updateUrl) ? updateName : setupName;
            var size = !string.IsNullOrEmpty(updateUrl) ? updateSize : setupSize;

            return new ReleaseInfo(tag, name, notes, url, size, fileName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>比版本号：返回 true 表示远程比本地新</summary>
    public static bool IsNewer(string remoteTag, string localVersion)
    {
        var r = Parse(remoteTag);
        var l = Parse(localVersion);
        if (r == null || l == null) return false;
        return r > l;
    }

    private static Version? Parse(string text)
    {
        var s = text.Trim().TrimStart('v', 'V');

        // 去掉 -beta / -rc 之类后缀，只留数字部分
        var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];

        var parts = s.Split('.');
        var nums = new List<int>();
        foreach (var p in parts)
        {
            nums.Add(int.TryParse(p, out var v) ? v : 0);
            if (nums.Count == 4) break;
        }

        while (nums.Count < 4) nums.Add(0);

        try { return new Version(nums[0], nums[1], nums[2], nums[3]); }
        catch { return null; }
    }

    /// <summary>下载一个文件到指定路径</summary>
    public static async Task<string> DownloadAsync(string url, long expectedSize, string destPath,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
    {
        using var resp = await DownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? expectedSize;

        await using var fs = File.Create(destPath);
        await using var input = await resp.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long done = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read <= 0) break;
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            progress?.Report((done, total));
        }

        return destPath;
    }

    /// <summary>
    /// 下载更新包并生成「替换自己并重启」的脚本。
    /// 更新包是 zip（里面一个 Hearth.exe）；下载完解压出来，再让 bat 在启动器退出后覆盖。
    /// </summary>
    public static async Task<string> PrepareUpdateAsync(ReleaseInfo info,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var appExe = Path.Combine(appDir, "Hearth.exe");

        var tempDir = Path.Combine(Path.GetTempPath(),
            "hearth-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        var pkg = Path.Combine(tempDir, "package" + Path.GetExtension(info.FileName));
        await DownloadAsync(info.DownloadUrl, info.Size, pkg, progress, ct);

        // 解压出新的 exe
        string newExe;
        if (pkg.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(pkg, tempDir, overwriteFiles: true);
            newExe = Directory.GetFiles(tempDir, "Hearth.exe", SearchOption.AllDirectories).FirstOrDefault()
                     ?? throw new InvalidOperationException("更新包里没有 Hearth.exe");
        }
        else if (pkg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            newExe = pkg;
        }
        else
        {
            throw new InvalidOperationException("不认识的更新包格式：" + info.FileName);
        }

        // 替换脚本：等启动器退出 → 覆盖 exe → 重新打开
        var bat = Path.Combine(tempDir, "apply-update.bat");
        var script =
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            "copy /y \"" + newExe + "\" \"" + appExe + "\" >nul\r\n" +
            "start \"\" \"" + appExe + "\"\r\n" +
            "del \"%~f0\"\r\n";

        File.WriteAllText(bat, script, System.Text.Encoding.Default);
        return bat;
    }
}
