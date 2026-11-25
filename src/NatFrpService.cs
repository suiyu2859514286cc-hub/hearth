using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>
/// 樱花内网穿透：只负责把用户自己的樱花隧道拉起来。
/// 不代注册、不共享账号，访问密钥只存本地。
/// </summary>
public static class NatFrpService
{
    // SakuraFrp 分发的 frpc（Windows x64）。版本升级时改这里。
    private const string FrpcUrl =
        "https://nya.globalslb.net/natfrp/client/frpc/0.51.0-sakura-15.1/frpc_windows_amd64.exe";

    public static string ToolDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hearth", "tools");

    public static string FrpcPath => Path.Combine(ToolDir, "frpc.exe");

    private static Process? _proc;
    private static readonly StringBuilder LogBuf = new();

    public static string Log
    {
        get { lock (LogBuf) return LogBuf.ToString(); }
    }

    public static bool IsRunning
    {
        get
        {
            try { return _proc is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>日志或状态变化时触发，UI 用</summary>
    public static event Action? Changed;

    public static async Task EnsureFrpcAsync(IProgress<string>? progress = null)
    {
        if (File.Exists(FrpcPath)) return;

        Directory.CreateDirectory(ToolDir);
        progress?.Report("正在下载 frpc…");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var bytes = await http.GetByteArrayAsync(FrpcUrl);
        var tmp = FrpcPath + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes);
        File.Move(tmp, FrpcPath, true);

        progress?.Report("frpc 下载完成");
    }

    public static void Start(string token, string tunnelId)
    {
        Stop();
        if (!File.Exists(FrpcPath))
            throw new FileNotFoundException("还没下载 frpc");

        lock (LogBuf) LogBuf.Clear();

        var psi = new ProcessStartInfo(FrpcPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ToolDir
        };
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(token + ":" + tunnelId);
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add("--log_level");
        psi.ArgumentList.Add("info");
        psi.ArgumentList.Add("--disable_log_color");

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => Append(e.Data);
        proc.ErrorDataReceived += (_, e) => Append(e.Data);
        proc.Exited += (_, _) => Changed?.Invoke();

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        _proc = proc;
        Changed?.Invoke();
    }

    private static void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (LogBuf)
        {
            LogBuf.AppendLine(line);
            if (LogBuf.Length > 12000)
                LogBuf.Remove(0, LogBuf.Length - 8000);
        }
        Changed?.Invoke();
    }

    public static void Stop()
    {
        var proc = _proc;
        _proc = null;
        if (proc != null)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch { }
            try { proc.Dispose(); } catch { }
        }
        Changed?.Invoke();
    }
}

/// <summary>
/// 从 Minecraft 的 latest.log 里找「对局域网开放」的端口。
/// 日志是英文的 Local game hosted on port xxxxx。
/// </summary>
public static class MinecraftLanLog
{
    private static readonly Regex PortPattern = new(
        @"Local game hosted on port (\d+)|本地游戏已在端口 (\d+) 上开启",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int? FindRecentPort(DateTime since)
    {
        foreach (var path in CandidateLogs())
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.LastWriteTime < since.AddMinutes(-3)) continue;

                var port = ReadTailPort(path);
                if (port != null) return port;
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> CandidateLogs()
    {
        var list = new List<(string Path, DateTime When)>();

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Add(Path.Combine(appData, ".minecraft", "logs", "latest.log"));
        }
        catch { }

        try
        {
            var versions = Path.Combine(VersionService.McDir, "versions");
            if (Directory.Exists(versions))
                foreach (var dir in Directory.GetDirectories(versions))
                    Add(Path.Combine(dir, "logs", "latest.log"));
        }
        catch { }

        return list.OrderByDescending(x => x.When).Select(x => x.Path);

        void Add(string p)
        {
            try
            {
                var fi = new FileInfo(p);
                if (fi.Exists) list.Add((p, fi.LastWriteTime));
            }
            catch { }
        }
    }

    private static int? ReadTailPort(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var take = (int)Math.Min(fs.Length, 64 * 1024);
        fs.Seek(-take, SeekOrigin.End);
        var buf = new byte[take];
        var read = fs.Read(buf, 0, take);
        var text = Encoding.UTF8.GetString(buf, 0, read);

        var matches = PortPattern.Matches(text);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var g = matches[i].Groups[1].Success ? matches[i].Groups[1] : matches[i].Groups[2];
            if (int.TryParse(g.Value, out var port) && port > 0 && port <= 65535)
                return port;
        }
        return null;
    }
}

/// <summary>
/// 本地端口转发：把固定端口（默认 25565）转到 MC 真正的局域网端口。
/// 这样樱花隧道里的本地端口不用每次改。
/// </summary>
public sealed class PortForwarder : IDisposable
{
    private readonly int _listenPort;
    private readonly int _targetPort;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    public PortForwarder(int listenPort, int targetPort)
    {
        _listenPort = listenPort;
        _targetPort = targetPort;
    }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, _listenPort);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = BridgeAsync(client, ct);
        }
    }

    private async Task BridgeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var server = new TcpClient();
                await server.ConnectAsync(IPAddress.Loopback, _targetPort, ct);
                using var c1 = client.GetStream();
                using var c2 = server.GetStream();
                var a = c1.CopyToAsync(c2, ct);
                var b = c2.CopyToAsync(c1, ct);
                await Task.WhenAny(a, b);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _listener = null;
        _cts = null;
    }
}
