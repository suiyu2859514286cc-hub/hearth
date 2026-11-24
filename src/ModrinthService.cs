using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>Modrinth 热门模组（带本地缓存，每天更新一次）</summary>
public static class ModrinthService
{
    public record ModInfo(string Title, string Description, long Downloads,
        string IconUrl, string[] Categories, string Slug);

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth");
    private static readonly string CacheFile = Path.Combine(CacheDir, "mods_cache.json");

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static ModrinthService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/" + AppSettings.Version);
    }

    public record SearchHit(
        string Id, string Slug, string Title, string Description, string IconUrl,
        long Downloads, string Updated, string[] Categories, string ProjectType, string[] Versions);

    /// <summary>搜索资源（Mod / 资源包 / 光影 / 数据包）</summary>
    public static async Task<List<SearchHit>> SearchAsync(
        string query, string? mcVersion, string projectType, int limit = 20)
    {
        var facets = new List<string> { "[\"project_type:" + projectType + "\"]" };
        if (!string.IsNullOrWhiteSpace(mcVersion))
            facets.Add("[\"versions:" + mcVersion + "\"]");

        var url = "https://api.modrinth.com/v2/search?limit=" + limit +
                  "&index=relevance" +
                  "&query=" + Uri.EscapeDataString(query ?? "") +
                  "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var list = new List<SearchHit>();

        foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
        {
            var cats = new List<string>();
            if (hit.TryGetProperty("categories", out var c))
                foreach (var x in c.EnumerateArray()) cats.Add(x.GetString() ?? "");

            var vers = new List<string>();
            if (hit.TryGetProperty("versions", out var v))
                foreach (var x in v.EnumerateArray()) vers.Add(x.GetString() ?? "");

            list.Add(new SearchHit(
                hit.GetProperty("project_id").GetString() ?? "",
                hit.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "",
                hit.GetProperty("title").GetString() ?? "?",
                hit.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                hit.TryGetProperty("icon_url", out var i) ? i.GetString() ?? "" : "",
                hit.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : 0,
                hit.TryGetProperty("date_modified", out var dm) ? dm.GetString() ?? "" : "",
                cats.ToArray(),
                hit.TryGetProperty("project_type", out var pt) ? pt.GetString() ?? "" : projectType,
                vers.ToArray()));
        }

        return list;
    }

    public record FileInfo(
        string Name, string Url, string FileName,
        string[] GameVersions, string[] Loaders, string Date, long Size, long Downloads);

    /// <summary>拿某个资源的全部可用文件（按发布时间从新到旧）</summary>
    public static async Task<List<FileInfo>> GetFilesAsync(string projectId)
    {
        var url = "https://api.modrinth.com/v2/project/" + projectId + "/version";
        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var result = new List<FileInfo>();
        foreach (var ver in doc.RootElement.EnumerateArray())
        {
            var gameVersions = new List<string>();
            if (ver.TryGetProperty("game_versions", out var gv))
                foreach (var x in gv.EnumerateArray()) gameVersions.Add(x.GetString() ?? "");

            var loaders = new List<string>();
            if (ver.TryGetProperty("loaders", out var ld))
                foreach (var x in ld.EnumerateArray()) loaders.Add(x.GetString() ?? "");

            if (!ver.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;
            var file = files[0];

            var name = ver.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var urlFile = file.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var fname = file.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "";
            var date = ver.TryGetProperty("date_published", out var dp) ? dp.GetString() ?? "" : "";
            var size = file.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
            var downloads = ver.TryGetProperty("downloads", out var dls) && dls.TryGetInt64(out var d) ? d : 0;

            if (!string.IsNullOrEmpty(urlFile))
                result.Add(new FileInfo(name, urlFile, fname,
                    gameVersions.ToArray(), loaders.ToArray(), date, size, downloads));
        }

        return result
            .OrderByDescending(x => DateTime.TryParse(x.Date, out var d) ? d : DateTime.MinValue)
            .ToList();
    }

    /// <summary>下载一个文件到指定目录</summary>
    public static async Task DownloadAsync(string url, string directory, string fileName,
        IProgress<(long Done, long Total)>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, fileName);

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
            progress?.Report((done, total));
        }
    }

    /// <summary>拿热门模组：优先读当天缓存，没有就请求 Modrinth</summary>
    public static async Task<List<ModInfo>> GetPopularModsAsync(int limit = 24)
    {
        // 1) 缓存：当天有效
        try
        {
            if (File.Exists(CacheFile))
            {
                var age = DateTime.Now - File.GetLastWriteTime(CacheFile);
                if (age.TotalHours < 24)
                {
                    var cached = JsonSerializer.Deserialize<List<ModInfo>>(File.ReadAllText(CacheFile));
                    if (cached is { Count: > 0 }) return cached;
                }
            }
        }
        catch { }

        // 2) 请求 Modrinth
        try
        {
            var url = "https://api.modrinth.com/v2/search" +
                      "?limit=" + limit +
                      "&index=downloads" +
                      "&facets=" + Uri.EscapeDataString("[[\"project_type:mod\"]]");
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var list = new List<ModInfo>();
            foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
            {
                var cats = new List<string>();
                if (hit.TryGetProperty("categories", out var c))
                    foreach (var x in c.EnumerateArray()) cats.Add(x.GetString() ?? "");

                list.Add(new ModInfo(
                    hit.GetProperty("title").GetString() ?? "?",
                    hit.GetProperty("description").GetString() ?? "",
                    hit.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0,
                    hit.TryGetProperty("icon_url", out var ic) ? (ic.GetString() ?? "") : "",
                    cats.ToArray(),
                    hit.GetProperty("slug").GetString() ?? ""
                ));
            }

            if (list.Count > 0)
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(CacheFile, JsonSerializer.Serialize(list));
                return list;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Modrinth 请求失败: " + ex.Message);
        }

        return new List<ModInfo>();
    }
}