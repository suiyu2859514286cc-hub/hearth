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

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Hearth", AppSettings.Version));
        c.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    public record ReleaseInfo(string Tag, string Name, string Notes, string DownloadUrl, long Size);

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

            // 优先挑名字里带 Setup 的安装包
            string url = "";
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                string? fallback = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var an = a.TryGetProperty("name", out var anEl) ? anEl.GetString() ?? "" : "";
                    var au = a.TryGetProperty("browser_download_url", out var auEl) ? auEl.GetString() ?? "" : "";
                    var asz = a.TryGetProperty("size", out var szEl) && szEl.TryGetInt64(out var sz) ? sz : 0;

                    if (string.IsNullOrEmpty(au)) continue;
                    if (an.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        url = au;
                        size = asz;
                        break;
                    }
                    fallback ??= au;
                }
                if (string.IsNullOrEmpty(url)) url = fallback ?? "";
            }

            return new ReleaseInfo(tag, name, notes, url, size);
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

    /// <summary>下载安装包到临时目录，返回路径</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo info,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
    {
        var name = "Hearth-" + info.Tag.TrimStart('v', 'V') + "-Setup.exe";
        var path = Path.Combine(Path.GetTempPath(), name);

        using var resp = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? info.Size;

        await using var fs = File.Create(path);
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

        return path;
    }
}
