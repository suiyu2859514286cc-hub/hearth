using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>
/// authlib-injector：让游戏连到第三方皮肤站，离线账号也能显示皮肤、披风。
/// 皮肤站地址在设置里填，jar 自动下载到缓存目录。
/// </summary>
public static class AuthlibService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static AuthlibService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/" + AppSettings.Version);
    }

    /// <summary>皮肤站地址（留空表示不用）</summary>
    public static string ServerUrl => AppSettings.Get("authlibUrl", "").Trim();

    public static bool Enabled => !string.IsNullOrEmpty(ServerUrl);

    private static string JarPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Hearth", "authlib-injector.jar");

    /// <summary>确保 jar 存在，返回路径（失败返回 null）</summary>
    public static async Task<string?> EnsureJarAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(JarPath) && new FileInfo(JarPath).Length > 100_000)
                return JarPath;

            progress?.Report("正在下载皮肤组件…");

            // 从 BMCLAPI 镜像拿最新版下载地址
            var meta = await Http.GetStringAsync(
                "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", ct);

            string? url = null;
            using (var doc = JsonDocument.Parse(meta))
            {
                if (doc.RootElement.TryGetProperty("download_url", out var u))
                    url = u.GetString();
            }

            if (string.IsNullOrEmpty(url))
                url = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest";

            Directory.CreateDirectory(Path.GetDirectoryName(JarPath)!);

            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            await using var fs = File.Create(JarPath);
            await resp.Content.CopyToAsync(fs, ct);

            return File.Exists(JarPath) && new FileInfo(JarPath).Length > 100_000 ? JarPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>游戏启动时要加的 -javaagent 参数（没配皮肤站时返回 null）</summary>
    public static async Task<string?> GetAgentArgAsync(IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!Enabled) return null;

        var jar = await EnsureJarAsync(progress, ct);
        if (jar == null) return null;

        var url = ServerUrl;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return null;

        return "-javaagent:" + jar + "=" + url;
    }
}
