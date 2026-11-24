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
/// 版本安装：版本 JSON → 客户端 → 库文件 → natives → 资源文件。
/// 下载顺序和镜像规则照 PCL（BMCLAPI 优先，失败退回官方源）。
/// </summary>
public static class InstallService
{
    public const string MirrorBase = "https://bmclapi2.bangbang93.com";

    // 下载单个文件最多等 5 分钟；真正的「卡住」由每次读取的 60 秒超时兜住
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // 下载字节计数：详情窗口用它算实时速度
    private static long _downloadedBytes;
    public static long DownloadedBytes => Interlocked.Read(ref _downloadedBytes);
    public static void ResetByteCounter() => Interlocked.Exchange(ref _downloadedBytes, 0);

    static InstallService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/0.3.0");
    }

    public static string McDir => VersionService.McDir;

    /// <summary>装一个版本。includeAssets 关掉可以只装客户端和库（快很多，但游戏没贴图）。</summary>
    public static async Task InstallAsync(
        string versionId,
        bool includeAssets,
        IProgress<(string Msg, double Pct)>? progress,
        CancellationToken ct = default,
        IProgress<(string Stage, int Done, int Total)>? stages = null)
    {
        var mc = McDir;
        var vDir = Path.Combine(mc, "versions", versionId);
        Directory.CreateDirectory(vDir);

        // 1) 版本 JSON
        ResetByteCounter();
        progress?.Report(("正在获取版本信息…", 0));
        stages?.Report(("json", 0, 1));
        var jsonText = await GetStringAsync(MirrorBase + "/version/" + versionId + "/json", ct);
        var jsonPath = Path.Combine(vDir, versionId + ".json");
        await File.WriteAllTextAsync(jsonPath, jsonText, ct);
        stages?.Report(("json", 1, 1));

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        // 2) 客户端 jar
        progress?.Report(("正在下载客户端…", 2));
        stages?.Report(("client", 0, 1));
        var clientUrl = root.GetProperty("downloads").GetProperty("client").GetProperty("url").GetString()!;
        await DownloadAsync(clientUrl, Path.Combine(vDir, versionId + ".jar"), ct);
        stages?.Report(("client", 1, 1));
        progress?.Report(("客户端完成", 6));

        // 3) 库文件 + natives
        var libs = root.GetProperty("libraries").EnumerateArray().Where(IsAllowedForThisOs).ToList();
        var downloads = new List<(string Url, string Dest, long Size, string? Sha1)>();
        var nativeJars = new List<string>();

        foreach (var lib in libs)
        {
            if (!lib.TryGetProperty("downloads", out var dl)) continue;

            if (dl.TryGetProperty("artifact", out var artifact))
            {
                var url = artifact.GetProperty("url").GetString();
                var path = artifact.GetProperty("path").GetString();
                var size = artifact.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                {
                    var sha1 = artifact.TryGetProperty("sha1", out var h) ? h.GetString() : null;
                    downloads.Add((url, Path.Combine(mc, "libraries", path), size, sha1));
                }
            }

            // 老版本用 natives 字段 + classifiers
            if (dl.TryGetProperty("classifiers", out var classifiers) &&
                lib.TryGetProperty("natives", out var natives) &&
                natives.TryGetProperty("windows", out var winKey))
            {
                var key = winKey.GetString() ?? "natives-windows";
                if (classifiers.TryGetProperty(key, out var classifier))
                {
                    var url = classifier.GetProperty("url").GetString();
                    var path = classifier.GetProperty("path").GetString();
                    var size = classifier.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(path))
                    {
                        var dest = Path.Combine(mc, "libraries", path);
                        var sha1 = classifier.TryGetProperty("sha1", out var h1) ? h1.GetString() : null;
                        downloads.Add((url, dest, size, sha1));
                        nativeJars.Add(dest);
                    }
                }
            }
        }

        // 新版本（1.19+）的 natives 是普通库条目，名字里带 natives，靠文件名认
        foreach (var (_, dest, _, _) in downloads)
        {
            var file = Path.GetFileNameWithoutExtension(dest);
            if (!file.Contains("natives", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains("arm64", StringComparison.OrdinalIgnoreCase)) continue;
            if (file.Contains("-x86", StringComparison.OrdinalIgnoreCase) &&
                !file.Contains("x86_64", StringComparison.OrdinalIgnoreCase)) continue;
            if (!nativeJars.Contains(dest)) nativeJars.Add(dest);
        }

        var totalLibBytes = Math.Max(1, downloads.Sum(d => d.Size));
        long doneLibBytes = 0;

        stages?.Report(("libraries", 0, downloads.Count));
        progress?.Report(($"正在下载库文件（0/{downloads.Count}）…", 8));
        var done = 0;
        var total = downloads.Count;
        var sem = new SemaphoreSlim(Math.Clamp(AppSettings.DownloadThreads, 1, 32));
        var tasks = downloads.Select(async d =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var got = await DownloadAsync(d.Url, d.Dest, ct, d.Size > 0 ? d.Size : null, d.Sha1);
                Interlocked.Add(ref doneLibBytes, got > 0 ? got : d.Size);
            }
            finally
            {
                sem.Release();
                var now = Interlocked.Increment(ref done);

                // 进度按字节走，和真正下了多少一致
                if (now % 3 == 0 || now == total)
                {
                    var bytes = Interlocked.Read(ref doneLibBytes);
                    var pct = 8 + 34.0 * bytes / totalLibBytes;
                    progress?.Report(($"正在下载库文件（{now}/{total}）…", pct));
                    stages?.Report(("libraries", now, total));
                }
            }
        }).ToList();
        await Task.WhenAll(tasks);

        // 4) natives 解压
        stages?.Report(("natives", 0, 1));
        progress?.Report(("正在解压 natives…", 41));
        var nativesDir = Path.Combine(vDir, "natives");
        Directory.CreateDirectory(nativesDir);
        foreach (var jar in nativeJars)
        {
            if (!File.Exists(jar)) continue;
            try
            {
                using var zip = ZipFile.OpenRead(jar);
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("META-INF", StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.FullName.EndsWith('/')) continue;
                    var target = Path.Combine(nativesDir, Path.GetFileName(entry.FullName));
                    entry.ExtractToFile(target, true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("natives 解压失败 " + jar + ": " + ex.Message);
            }
        }

        // 5) 资源文件
        if (includeAssets)
        {
            stages?.Report(("natives", 1, 1));
        stages?.Report(("assets", 0, 1));
        progress?.Report(("正在获取资源索引…", 43));
            var assetIndex = root.GetProperty("assetIndex");
            var indexUrl = assetIndex.GetProperty("url").GetString()!;
            var indexId = assetIndex.GetProperty("id").GetString()!;
            var indexPath = Path.Combine(mc, "assets", "indexes", indexId + ".json");
            await DownloadAsync(indexUrl, indexPath, ct);

            using var idx = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct));
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in idx.RootElement.GetProperty("objects").EnumerateObject())
            {
                if (obj.Value.TryGetProperty("hash", out var h) && h.GetString() is { Length: > 0 } hash)
                    hashes.Add(hash);
            }

            var objectsDir = Path.Combine(mc, "assets", "objects");
            var list = hashes.ToList();
            var objDone = 0;

            // 每个资源的大小（索引里有 size），用来算真实进度
            var assetSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var obj in idx.RootElement.GetProperty("objects").EnumerateObject())
            {
                var h = obj.Value.TryGetProperty("hash", out var hh) ? hh.GetString() : null;
                var sz = obj.Value.TryGetProperty("size", out var ss) ? ss.GetInt64() : 0;
                if (!string.IsNullOrEmpty(h) && sz > 0) assetSizes[h] = sz;
            }
            var totalAssetBytes = Math.Max(1, (long)hashes.Sum(h => assetSizes.TryGetValue(h, out var s) ? s : 0));
            long doneAssetBytes = 0;
            var objFailed = new System.Collections.Concurrent.ConcurrentBag<string>();
            var objSem = new SemaphoreSlim(Math.Clamp(AppSettings.DownloadThreads, 1, 32));
            stages?.Report(("assets", 0, list.Count));
        progress?.Report(($"正在下载资源（0/{list.Count}）…", 45));
            var objTasks = list.Select(async hash =>
            {
                await objSem.WaitAsync(ct);
                try
                {
                    var sub = hash[..2];
                    var dest = Path.Combine(objectsDir, sub, hash);

                    // 资源文件名就是 sha1，大小也能从索引里查到
                    var knownSize = assetSizes.TryGetValue(hash, out var s0) && s0 > 0 ? s0 : (long?)null;

                    var got = await DownloadAsync(
                        "https://resources.download.minecraft.net/" + sub + "/" + hash,
                        dest, ct, knownSize, hash);
                    Interlocked.Add(ref doneAssetBytes, got > 0 ? got : (assetSizes.TryGetValue(hash, out var s2) ? s2 : 0));
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    // 单个资源坏了不影响整体，记下来最后统计
                    objFailed.Add(hash + "  " + ex.Message);
                }
                finally
                {
                    objSem.Release();
                    var now = Interlocked.Increment(ref objDone);
                    if (now % 10 == 0 || now == list.Count)
                    {
                        var bytes = Interlocked.Read(ref doneAssetBytes);
                        progress?.Report(($"正在下载资源（{now}/{list.Count}）…", 45 + 54.0 * bytes / totalAssetBytes));
                    }
                    stages?.Report(("assets", now, list.Count));
                }
            }).ToList();
            await Task.WhenAll(objTasks);

            if (!objFailed.IsEmpty)
            {
                var sample = string.Join(Environment.NewLine, objFailed.Take(5));
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "hearth-asset-failed.txt"),
                        "失败 " + objFailed.Count + " 个：" + Environment.NewLine + sample);
                }
                catch { }

                progress?.Report(($"有 {objFailed.Count} 个资源文件没下下来，游戏可能缺贴图（详情见临时目录的 hearth-asset-failed.txt）", 99));
            }
        }

        stages?.Report(("done", 1, 1));
        progress?.Report(("安装完成", 100));
    }

    /// <summary>rules 判断：这个库在这台机器上要不要装</summary>
    private static bool IsAllowedForThisOs(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules)) return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) ? a.GetString() : null;
            var match = true;

            if (rule.TryGetProperty("os", out var os))
            {
                if (os.TryGetProperty("name", out var name) && name.GetString() != "windows") match = false;
                if (os.TryGetProperty("arch", out var arch))
                {
                    var value = arch.GetString();
                    if (value is not ("x86_64" or "amd64")) match = false;
                }
            }

            // 带 features 的一般是 demo / 自定义分辨率之类，一律不装
            if (rule.TryGetProperty("features", out _)) match = false;

            if (match) allowed = action == "allow";
        }
        return allowed;
    }

    private static string Mirrorize(string url)
    {
        if (AppSettings.DownloadSource == "official") return url;

        if (url.Contains("libraries.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://libraries.minecraft.net/", MirrorBase + "/maven/", StringComparison.OrdinalIgnoreCase);
        if (url.Contains("resources.download.minecraft.net/", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://resources.download.minecraft.net/", MirrorBase + "/assets/", StringComparison.OrdinalIgnoreCase);
        if (url.Contains("piston-data.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://piston-data.mojang.com/", MirrorBase + "/", StringComparison.OrdinalIgnoreCase);
        if (url.Contains("piston-meta.mojang.com/", StringComparison.OrdinalIgnoreCase))
            return url.Replace("https://piston-meta.mojang.com/", MirrorBase + "/", StringComparison.OrdinalIgnoreCase);
        return url;
    }

    private static async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            return await Http.GetStringAsync(url, ct);
        }
        catch
        {
            // 镜像挂了就用官方
            var official = url.Replace(MirrorBase + "/version/", "https://launchermeta.mojang.com/mc/game/version/");
            return await Http.GetStringAsync(official, ct);
        }
    }

    /// <summary>下载单个文件：镜像优先，失败换官方；已存在且大小对得上才跳过</summary>
    /// <summary>校验文件 sha1（流式算，不占内存）</summary>
    private static async Task<bool> Sha1MatchesAsync(string path, string expectedSha1, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA1.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            return hex.Equals(expectedSha1.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<long> DownloadAsync(string url, string dest, CancellationToken ct,
        long? expectedSize = null, string? expectedSha1 = null)
    {
        long resumeFrom = 0;

        if (File.Exists(dest))
        {
            var len = new FileInfo(dest).Length;
            if (expectedSize is > 0)
            {
                if (len == expectedSize.Value)
                {
                    // 大小对得上，再验一下 sha1（有的话）
                    if (string.IsNullOrEmpty(expectedSha1) || await Sha1MatchesAsync(dest, expectedSha1, ct))
                        return 0;

                    try { File.Delete(dest); } catch { }
                }
                else if (len < expectedSize.Value)
                {
                    resumeFrom = len;   // 上次下到一半，接着下
                }
                else
                {
                    try { File.Delete(dest); } catch { }   // 比预期还大，肯定坏了
                }
            }
            else if (len > 0) return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        // 实测官方源比镜像快（镜像 0.15 MB/s，官方 0.49 MB/s），auto 走官方优先、镜像兜底
        var mirror = Mirrorize(url);
        var candidates = new List<string>();
        if (AppSettings.DownloadSource == "mirror")
        {
            candidates.Add(mirror);
            if (mirror != url) candidates.Add(url);
        }
        else
        {
            candidates.Add(url);
            if (mirror != url) candidates.Add(mirror);
        }

        Exception? last = null;
        foreach (var candidate in candidates)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // 每次读取单独计时：25 秒没数据就判断这条连接挂死了
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    var req = new HttpRequestMessage(HttpMethod.Get, candidate);
                    if (resumeFrom > 0)
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);

                    using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    resp.EnsureSuccessStatusCode();

                    // 206 = 服务器支持接着下；200 = 不支持，只能从头
                    var append = resp.StatusCode == System.Net.HttpStatusCode.PartialContent && resumeFrom > 0;
                    var mode = append ? FileMode.Append : FileMode.Create;
                    if (!append) resumeFrom = 0;

                    await using var fs = new FileStream(dest, mode, FileAccess.Write, FileShare.None, 81920, true);
                    await using var input = await resp.Content.ReadAsStreamAsync(cts.Token);

                    var buffer = new byte[81920];
                    while (true)
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(25));
                        var read = await input.ReadAsync(buffer, cts.Token);
                        if (read <= 0) break;
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                        Interlocked.Add(ref _downloadedBytes, read);
                    }

                    await fs.FlushAsync(ct);

                    if (expectedSize is > 0 && fs.Length != expectedSize.Value)
                    {
                        // 差一点点的话留着下次续传，差太多就当坏了
                        if (fs.Length > expectedSize.Value * 2 || fs.Length < expectedSize.Value / 2)
                        {
                            try { File.Delete(dest); } catch { }
                        }
                        throw new IOException("大小不对：" + fs.Length + " / " + expectedSize.Value);
                    }

                    // 有 sha1 就验一遍，坏了删掉让下一轮重下
                    if (!string.IsNullOrEmpty(expectedSha1) &&
                        !await Sha1MatchesAsync(dest, expectedSha1, ct))
                    {
                        try { File.Delete(dest); } catch { }
                        throw new IOException("文件校验不通过（sha1 不符）");
                    }

                    return fs.Length;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(400, ct);
                }
            }
        }
        throw new IOException("下载失败: " + url + " — " + last?.Message, last);
    }
}
