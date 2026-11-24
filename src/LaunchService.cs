using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>启动游戏：合并继承链、拼 classpath、替换参数、找 Java、拉起来</summary>
public static class LaunchService
{
    public static string McDir => VersionService.McDir;

    /// <summary>离线玩家的 UUID（Mojang 的算法：md5("OfflinePlayer:"+名字) 加短横线）</summary>
    public static string OfflineUuid(string name)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..];
    }

    public static string? FindJava(int wantedMajor = 0)
    {
        // 设置里手动指定的优先，但大版本对不上就退回自动找（免得 1.20.1 用 Java 8 跑崩）
        var custom = AppSettings.JavaPath;
        if (!string.IsNullOrEmpty(custom) && File.Exists(custom) &&
            (wantedMajor <= 0 || GetJavaMajor(custom) == wantedMajor))
            return custom;

        var candidates = new List<string>();

        var roots = new[]
        {
            @"D:\软件制作\工具",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime"),
        };

        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (!Path.GetFileName(dir).Equals("bin", StringComparison.OrdinalIgnoreCase)) continue;
                    var exe = Path.Combine(dir, "java.exe");
                    if (File.Exists(exe)) candidates.Add(exe);
                    if (candidates.Count > 80) break;
                }
            }
            catch { }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var exe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }

        if (wantedMajor > 0)
        {
            // 指定了版本就必须精确匹配，找不到返回 null 让上层去下载。
            // 这里不能退而求其次——1.12.2 用 Java 17 跑会直接崩。
            foreach (var candidate in candidates)
                if (File.Exists(candidate) && GetJavaMajor(candidate) == wantedMajor) return candidate;
            return null;
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public static int GetJavaMajor(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(4000);
            var line = err.Split('\n').FirstOrDefault(l => l.Contains("version")) ?? err;
            var start = line.IndexOf('"');
            var end = line.LastIndexOf('"');
            if (start >= 0 && end > start)
            {
                var ver = line[(start + 1)..end];
                if (ver.StartsWith("1."))
                {
                    var parts = ver.Split('.');
                    return parts.Length > 1 && int.TryParse(parts[1], out var m) ? m : 8;
                }
                return int.TryParse(ver.Split('.')[0], out var n) ? n : 0;
            }
        }
        catch { }
        return 0;
    }

    /// <summary>读版本 JSON，遇到 inheritsFrom 就把父版本合并进来（父在下，子覆盖）</summary>
    public static JsonObject? LoadMergedVersion(string versionId, int depth = 0)
    {
        var path = Path.Combine(McDir, "versions", versionId, versionId + ".json");
        if (!File.Exists(path)) return null;

        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (node == null) return null;

        var parentId = node["inheritsFrom"]?.GetValue<string>();
        if (parentId == null || depth >= 5) return node;

        var parent = LoadMergedVersion(parentId, depth + 1);
        return parent == null ? node : MergeVersion(parent, node);
    }

    private static JsonObject MergeVersion(JsonObject parent, JsonObject child)
    {
        var result = (JsonObject)parent.DeepClone();
        foreach (var kv in child)
            result[kv.Key] = kv.Value?.DeepClone();

        // 库：子在前、父在后，同名的保留子版本
        var libs = new JsonArray();
        var seen = new HashSet<string>();
        foreach (var source in new[] { child, parent })
        {
            if (source["libraries"] is not JsonArray arr) continue;
            foreach (var lib in arr)
            {
                var name = lib?["name"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                if (!seen.Add(name)) continue;
                libs.Add(lib!.DeepClone());
            }
        }
        result["libraries"] = libs;

        // 参数：父在前、子在后
        var args = new JsonObject();
        foreach (var key in new[] { "game", "jvm" })
        {
            var merged = new JsonArray();
            foreach (var source in new[] { parent, child })
            {
                if (source["arguments"]?[key] is JsonArray arr)
                    foreach (var a in arr)
                        merged.Add(a!.DeepClone());
            }
            args[key] = merged;
        }
        result["arguments"] = args;

        return result;
    }

    public static async Task<(bool Ok, string Message)> LaunchAsync(
        string versionId, string playerName, int memoryMb,
        IProgress<(string Msg, double Pct)>? progress = null,
        CancellationToken ct = default,
        string? accessToken = null, string? uuid = null,
        Func<int, bool>? confirmJavaDownload = null,
        Action<int>? onJavaDownloadStart = null)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var mc = McDir;
            var vDir = Path.Combine(mc, "versions", versionId);
            var gameDir = VersionMeta.GameDir(versionId);   // 版本隔离时是版本自己的文件夹
            Directory.CreateDirectory(gameDir);
            var root = LoadMergedVersion(versionId);
            if (root == null) return (false, "找不到版本 JSON，先安装这个版本");

            // 第一次进这个版本：把语言设成中文，关掉新手提示
            GameOptions.EnsureFirstLaunch(gameDir, versionId);

            var wantedMajor = 8;
            if (root["javaVersion"]?["majorVersion"]?.GetValue<int>() is { } jmn) wantedMajor = jmn;

            var java = FindJava(wantedMajor);
            if (java == null)
            {
                // 先问一句要不要下，用户拒绝就整个取消启动
                if (confirmJavaDownload != null && !confirmJavaDownload(wantedMajor))
                    return (false, "已取消启动（本机缺少 Java " + wantedMajor + "）");

                onJavaDownloadStart?.Invoke(wantedMajor);
                progress?.Report(("本机没有 Java " + wantedMajor + "，正在自动下载…", 0));
                try
                {
                    java = await JavaService.EnsureJavaAsync(wantedMajor, progress, ct);
                }
                catch (Exception ex)
                {
                    return (false, "Java " + wantedMajor + " 自动下载失败：" + ex.Message);
                }
            }
            if (java == null) return (false, "没找到 Java " + wantedMajor + "，先装一个 JDK");

            ct.ThrowIfCancellationRequested();

            var cp = new List<string>();
            if (root["libraries"] is JsonArray libs)
            {
                foreach (var lib in libs.OfType<JsonObject>())
                {
                    if (!IsAllowedForThisOs(lib)) continue;

                    string? path = lib["downloads"]?["artifact"]?["path"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(path) && lib["name"]?.GetValue<string>() is { } mavenName)
                        path = LoaderService.MavenPath(mavenName);
                    if (string.IsNullOrEmpty(path)) continue;

                    var full = Path.Combine(mc, "libraries", path);
                    var fileName = Path.GetFileName(full);
                    if (fileName.Contains("arm64", StringComparison.OrdinalIgnoreCase)) continue;
                    if (fileName.Contains("-x86", StringComparison.OrdinalIgnoreCase) &&
                        !fileName.Contains("x86_64", StringComparison.OrdinalIgnoreCase)) continue;
                    if (File.Exists(full)) cp.Add(full);
                }
            }

            // 继承版本没有自己的 jar 时，把父版本的 jar 复制成「当前版本名.jar」。
            // Forge 的 ignoreList 按版本名匹配（如 1.20.1-forge-47.2.0.jar），名字不对会模块冲突。
            var clientJar = Path.Combine(vDir, versionId + ".jar");
            if (!File.Exists(clientJar))
            {
                var parentJarId = FindParentWithJar(versionId, 0);
                if (parentJarId != null)
                {
                    var parentJar = Path.Combine(mc, "versions", parentJarId, parentJarId + ".jar");
                    try { File.Copy(parentJar, clientJar, true); }
                    catch { clientJar = parentJar; }
                }
            }
            if (File.Exists(clientJar)) cp.Add(clientJar);

            var natives = Path.Combine(vDir, "natives");
            if (!Directory.Exists(natives) || Directory.GetFiles(natives).Length == 0)
            {
                var inherited = FindParentNatives(versionId, 0);
                if (inherited != null) natives = inherited;
            }

            var assetIndexId = root["assetIndex"]?["id"]?.GetValue<string>() ?? "legacy";

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["auth_player_name"] = playerName,
                ["version_name"] = versionId,
                ["game_directory"] = gameDir,
                ["assets_root"] = Path.Combine(mc, "assets"),
                ["assets_index_name"] = assetIndexId,
                ["auth_uuid"] = string.IsNullOrEmpty(uuid)
                    ? OfflineUuid(playerName)
                    : uuid.Replace("-", ""),
                ["auth_access_token"] = string.IsNullOrEmpty(accessToken) ? "0" : accessToken,
                ["clientid"] = "",
                ["auth_xuid"] = "",
                ["user_type"] = string.IsNullOrEmpty(accessToken) ? "legacy" : "msa",
                ["version_type"] = root["type"]?.GetValue<string>() ?? "release",
                ["natives_directory"] = natives,
                ["launcher_name"] = "Hearth",
                ["launcher_version"] = AppSettings.Version,
                ["classpath"] = string.Join(';', cp),
                ["user_properties"] = "{}",
                ["game_assets"] = Path.Combine(mc, "assets", "virtual", "legacy"),
                // Forge 自己的占位符
                ["library_directory"] = Path.Combine(mc, "libraries").Replace('\\', '/'),
                ["classpath_separator"] = ";",
            };

            var args = new List<string>
            {
                "-Xmx" + memoryMb + "M",
                "-Djava.library.path=" + natives,
            };

            // 配了皮肤站就挂上 authlib-injector，离线账号也能显示皮肤
            try
            {
                var agent = await AuthlibService.GetAgentArgAsync(
                    new Progress<string>(m => progress?.Report((m, 3))), ct);

                if (!string.IsNullOrEmpty(agent))
                {
                    args.Add(agent);
                    // 告诉游戏皮肤站是"正版验证服务器"，不然某些版本会拒绝
                    args.Add("-Dauthlibinjector.side=client");
                }
            }
            catch { }

            args.Add("-cp");
            args.Add(string.Join(';', cp));

            if (root["arguments"]?["jvm"] is JsonArray jvmArgs)
            {
                var jvmList = ExpandArgs(jvmArgs).ToList();
                for (var i = 0; i < jvmList.Count; i++)
                {
                    var item = jvmList[i];
                    if (item is "-cp" or "-classpath") { i++; continue; }
                    if (item.StartsWith("-Djava.class.path=", StringComparison.OrdinalIgnoreCase)) continue;
                    args.Add(Substitute(item, vars));
                }
            }

            Directory.CreateDirectory(natives);

            var mainClass = root["mainClass"]?.GetValue<string>();
            if (string.IsNullOrEmpty(mainClass)) return (false, "版本 JSON 里没有 mainClass");
            args.Add(mainClass);

            if (root["arguments"]?["game"] is JsonArray gameArgs)
            {
                foreach (var item in ExpandArgs(gameArgs))
                    args.Add(Substitute(item, vars));
            }
            else if (root["minecraftArguments"]?.GetValue<string>() is { } legacyArgs)
            {
                foreach (var part in SplitArgs(legacyArgs))
                    args.Add(Substitute(part, vars));
            }

            var psi = new ProcessStartInfo(java)
            {
                WorkingDirectory = gameDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            ct.ThrowIfCancellationRequested();

            var logPath = Path.Combine(Path.GetTempPath(), "hearth-launch.log");
            try
            {
                File.WriteAllText(logPath,
                    java + Environment.NewLine +
                    string.Join(Environment.NewLine, args.Select(a => "  " + a)) +
                    Environment.NewLine + "-----" + Environment.NewLine);
            }
            catch { }

            var proc = Process.Start(psi);
            if (proc == null) return (false, "进程没起来");

            proc.OutputDataReceived += (_, e) => AppendLog(logPath, e.Data);
            proc.ErrorDataReceived += (_, e) => AppendLog(logPath, e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await Task.Delay(2500);
            if (proc.HasExited) return (false, "游戏启动后立刻退出了，看 " + logPath);

            return (true, "已用 Java " + wantedMajor + " 启动 " + versionId);
        }
        catch (OperationCanceledException)
        {
            return (false, "已取消启动");
        }
        catch (Exception ex)
        {
            return (false, "启动失败：" + ex.Message);
        }
    }

    /// <summary>顺着 inheritsFrom 找到第一个自带 jar 的父版本</summary>
    private static string? FindParentWithJar(string versionId, int depth)
    {
        if (depth > 5) return null;
        var path = Path.Combine(McDir, "versions", versionId, versionId + ".json");
        if (!File.Exists(path)) return null;
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        var parentId = node?["inheritsFrom"]?.GetValue<string>();
        if (parentId == null) return null;

        var jar = Path.Combine(McDir, "versions", parentId, parentId + ".jar");
        if (File.Exists(jar)) return parentId;
        return FindParentWithJar(parentId, depth + 1);
    }

    /// <summary>顺着 inheritsFrom 找到第一个有 natives 文件的版本目录</summary>
    private static string? FindParentNatives(string versionId, int depth)
    {
        if (depth > 5) return null;
        var path = Path.Combine(McDir, "versions", versionId, versionId + ".json");
        if (!File.Exists(path)) return null;
        var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        var parentId = node?["inheritsFrom"]?.GetValue<string>();
        if (parentId == null) return null;

        var dir = Path.Combine(McDir, "versions", parentId, "natives");
        if (Directory.Exists(dir) && Directory.GetFiles(dir).Length > 0) return dir;
        return FindParentNatives(parentId, depth + 1);
    }

    private static void AppendLog(string path, string? line)
    {
        if (line == null) return;
        try { File.AppendAllText(path, line + Environment.NewLine); } catch { }
    }

    private static IEnumerable<string> ExpandArgs(JsonArray array)
    {
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var s))
            {
                if (!string.IsNullOrEmpty(s)) yield return s;
            }
            else if (item is JsonObject obj && obj["rules"] is JsonArray rules)
            {
                if (!RulesAllow(rules)) continue;
                if (obj["value"] is JsonValue v)
                {
                    if (v.TryGetValue<string>(out var single)) yield return single ?? "";
                }
                else if (obj["value"] is JsonArray values)
                {
                    foreach (var value2 in values)
                        if (value2 is JsonValue sv && sv.TryGetValue<string>(out var str))
                            yield return str ?? "";
                }
            }
        }
    }

    private static string Substitute(string input, Dictionary<string, string> vars)
    {
        var prefix = "$" + "{";
        foreach (var (key, value) in vars)
            input = input.Replace(prefix + key + "}", value, StringComparison.OrdinalIgnoreCase);
        return input;
    }

    private static IEnumerable<string> SplitArgs(string raw)
    {
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in raw)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                continue;
            }
            current.Append(ch);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static bool IsAllowedForThisOs(JsonObject lib)
        => lib["rules"] is not JsonArray rules || RulesAllow(rules);

    private static bool RulesAllow(JsonArray rules)
    {
        var allowed = false;
        foreach (var rule in rules.OfType<JsonObject>())
        {
            var action = rule["action"]?.GetValue<string>();
            var match = true;

            if (rule["os"] is JsonObject os)
            {
                if (os["name"]?.GetValue<string>() is { } name && name != "windows") match = false;
                if (os["arch"]?.GetValue<string>() is { } arch && arch is not ("x86_64" or "amd64")) match = false;
            }

            if (rule["features"] != null) match = false;
            if (match) allowed = action == "allow";
        }
        return allowed;
    }
}
