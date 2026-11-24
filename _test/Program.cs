using BlockLauncher;

var id = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "1.20.1";
var progress = new Progress<(string Msg, double Pct)>(p => Console.WriteLine($"[{p.Pct,5:F1}%] {p.Msg}"));

if (args.Contains("--fabric"))
{
    var loaders = await LoaderService.GetFabricVersionsAsync(id);
    var pick = loaders.FirstOrDefault(l => l.Stable)?.Version ?? loaders[0].Version;
    Console.WriteLine($"fabric loaders: {loaders.Count}, pick = {pick}");
    var newId = await LoaderService.InstallFabricAsync(id, pick, progress);
    Console.WriteLine("installed: " + newId);
    return 0;
}

if (args.Contains("--premium"))
{
    var acc = MicrosoftAuth.Load();
    if (acc == null) { Console.WriteLine("没有已保存账号"); return 1; }

    Console.WriteLine("账号: " + acc.Name + " / " + acc.Uuid);
    Console.WriteLine("令牌过期: " + acc.ExpiresAt + " (UTC)");
    Console.WriteLine("是否已过期: " + (acc.ExpiresAt <= DateTime.UtcNow));

    var ver = args.FirstOrDefault(a => a.StartsWith("--ver="))?.Substring(6) ?? "1.20.1";
    Console.WriteLine("启动: " + ver + "  (正版模式)");

    var r = await LaunchService.LaunchAsync(ver, acc.Name, 4096, null,
        CancellationToken.None, acc.AccessToken, acc.Uuid);
    Console.WriteLine("结果: " + r.Ok + " " + r.Message);
    return 0;
}

if (args.Contains("--settings-check"))
{
    Console.WriteLine("javaPath        = [" + AppSettings.JavaPath + "]");
    Console.WriteLine("defaultMemory   = " + AppSettings.DefaultMemory);
    Console.WriteLine("downloadSource  = " + AppSettings.DownloadSource);
    Console.WriteLine("downloadThreads = " + AppSettings.DownloadThreads);
    Console.WriteLine("animations      = " + AppSettings.Animations);

    // 模拟主窗口启动时取内存的逻辑
    var targetVersion = args.FirstOrDefault(a => a.StartsWith("--ver="))?.Substring(6) ?? "1.20.1";
    Console.WriteLine("auto memory     = " + MemoryUtil.Auto());
    Console.WriteLine("1.20.1  mem     = " + MemoryUtil.Resolve("1.20.1"));
    Console.WriteLine("1.12.2  mem     = " + MemoryUtil.Resolve("1.12.2"));
    Console.WriteLine("global  mem     = " + AppSettings.DefaultMemory);
    Console.WriteLine("java 17         = " + (LaunchService.FindJava(17) ?? "null"));
    return 0;
}

if (args.Contains("--meta"))
{
    Console.WriteLine("1.20.1 isolated = " + VersionMeta.IsIsolated("1.20.1"));
    Console.WriteLine("1.20.1 gameDir  = " + VersionMeta.GameDir("1.20.1"));
    VersionMeta.Set("1.20.1", "isolation", "0");
    Console.WriteLine("after off      = " + VersionMeta.GameDir("1.20.1"));
    VersionMeta.Set("1.20.1", "isolation", "1");
    Console.WriteLine("after on       = " + VersionMeta.GameDir("1.20.1"));
    return 0;
}

if (args.Contains("--pack"))
{
    var packPath = args.FirstOrDefault(a => a.StartsWith("--path="))?.Substring(7) ?? "";
    var pack = PackService.Inspect(packPath);
    if (pack == null) { Console.WriteLine("无法识别"); return 1; }
    Console.WriteLine($"type={pack.Type} name={pack.Name} mc={pack.McVersion} loader={pack.Loader} {pack.LoaderVersion} files={pack.FileCount}");
    var message = await PackService.InstallAsync(pack, progress);
    Console.WriteLine("RESULT: " + message);
    return 0;
}

if (args.Contains("--neoforge"))
{
    var ver = args.FirstOrDefault(a => a.StartsWith("--ver="))?.Substring(6) ?? "47.1.12";
    var list = await LoaderService.GetNeoForgeVersionsAsync(id);
    Console.WriteLine($"neoforge versions: {list.Count}, newest = {list.FirstOrDefault()?.Version}");
    var newId = await LoaderService.InstallNeoForgeAsync(id, ver, progress);
    Console.WriteLine("installed: " + newId);
    return 0;
}

if (args.Contains("--forge"))
{
    var ver = args.FirstOrDefault(a => a.StartsWith("--ver="))?.Substring(6) ?? "47.2.0";
    Console.WriteLine($"installing forge {ver} for {id}");
    var newId = await LoaderService.InstallForgeAsync(id, ver, progress);
    Console.WriteLine("installed: " + newId);
    return 0;
}

if (args.Contains("--launch"))
{
    var target = args.FirstOrDefault(a => a.StartsWith("--id="))?.Substring(5) ?? id;
    var (ok, msg) = await LaunchService.LaunchAsync(target, "Tester", 3072, progress);
    Console.WriteLine((ok ? "OK: " : "FAIL: ") + msg);
    await Task.Delay(4000);
    return ok ? 0 : 1;
}

var withAssets = !args.Contains("--no-assets");
Console.WriteLine($"install {id}, assets={withAssets}");
try
{
    await InstallService.InstallAsync(id, withAssets, progress);
}
catch (Exception ex)
{
    Console.WriteLine("FAIL: " + ex);
    return 1;
}
return 0;
