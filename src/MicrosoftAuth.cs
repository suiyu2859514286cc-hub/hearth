using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>微软正版登录：设备码流程 → Xbox Live → XSTS → Minecraft 档案</summary>
public static class MicrosoftAuth
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.Add("User-Agent", "Hearth/" + AppSettings.Version);
        c.DefaultRequestHeaders.Add("Accept", "application/json");
        return c;
    }

    /// <summary>Azure 应用（公共客户端）的 client_id，存在设置里</summary>
    public static string ClientId => AppSettings.Get("msClientId", "").Trim();

    public record DeviceCodeInfo(string UserCode, string VerificationUri, string Code, int Interval, int ExpiresIn);

    public record Account(
        string Uuid, string Name, string RefreshToken,
        string AccessToken, DateTime ExpiresAt);

    // ===== 第一步：拿设备码 =====

    public static async Task<DeviceCodeInfo> RequestDeviceCodeAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ClientId))
            throw new InvalidOperationException("还没填微软应用的 client_id");

        var body = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["scope"] = "XboxLive.signin offline_access"
        };

        using var resp = await Http.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode",
            new FormUrlEncodedContent(body), ct);

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("拿设备码失败：" + ErrorOf(text));

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return new DeviceCodeInfo(
            root.GetProperty("user_code").GetString() ?? "",
            root.GetProperty("verification_uri").GetString() ?? "https://microsoft.com/link",
            root.GetProperty("device_code").GetString() ?? "",
            root.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5,
            root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 900);
    }

    // ===== 第二步：等用户授权，轮询拿 token =====

    public static async Task<Account> WaitForLoginAsync(DeviceCodeInfo dc, IProgress<string>? progress, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(dc.ExpiresIn);
        var interval = Math.Max(1, dc.Interval);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);

            var body = new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = dc.Code
            };

            using var resp = await Http.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
                new FormUrlEncodedContent(body), ct);

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var msAccess = root.GetProperty("access_token").GetString() ?? "";
                var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? "" : "";
                progress?.Report("微软账号已验证，正在登录 Xbox…");
                return await CompleteAsync(msAccess, refresh, ct);
            }

            // 用户还没授权就继续等
            if (text.Contains("authorization_pending", StringComparison.Ordinal)) continue;
            if (text.Contains("slow_down", StringComparison.Ordinal)) { interval += 2; continue; }
            if (text.Contains("expired_token", StringComparison.Ordinal))
                throw new InvalidOperationException("设备码过期了，重新来一次");
            if (text.Contains("authorization_declined", StringComparison.Ordinal))
                throw new InvalidOperationException("授权被拒绝了");

            throw new InvalidOperationException("登录失败：" + ErrorOf(text));
        }

        throw new InvalidOperationException("等太久了，重新来一次");
    }

    /// <summary>用 refresh_token 续登（启动时用）</summary>
    public static async Task<Account> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var body = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
            ["scope"] = "XboxLive.signin offline_access"
        };

        using var resp = await Http.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token",
            new FormUrlEncodedContent(body), ct);

        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("登录过期了，重新登录一次");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        return await CompleteAsync(
            root.GetProperty("access_token").GetString() ?? "",
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? refreshToken : refreshToken,
            ct);
    }

    // ===== 第三步：Xbox Live → XSTS → Minecraft =====

    private static async Task<Account> CompleteAsync(string msAccess, string refreshToken, CancellationToken ct)
    {
        // Xbox Live
        var xblBody = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + msAccess
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        });

        var (xblToken, _) = await PostXboxAsync("https://user.auth.xboxlive.com/user/authenticate", xblBody, ct);

        // XSTS
        var xstsBody = JsonSerializer.Serialize(new
        {
            Properties = new
            {
                SandboxId = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT"
        });

        var (xstsToken, uhs) = await PostXboxAsync("https://xsts.auth.xboxlive.com/xsts/authorize", xstsBody, ct);

        // Minecraft
        var mcBody = JsonSerializer.Serialize(new
        {
            identityToken = "XBL3.0 x=" + uhs + ";" + xstsToken
        });

        using var mcResp = await Http.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new StringContent(mcBody, Encoding.UTF8, "application/json"), ct);

        var mcText = await mcResp.Content.ReadAsStringAsync(ct);
        if (!mcResp.IsSuccessStatusCode)
        {
            // 这个错误是 Mojang 没批应用白名单，不是配置问题
            if (mcText.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Hearth 还没通过 Mojang 的应用审核，暂时只能用离线登录（审核通过后无需更新，直接可用）");

            throw new InvalidOperationException("Minecraft 登录失败：" + ErrorOf(mcText));
        }

        using var mcDoc = JsonDocument.Parse(mcText);
        var mcToken = mcDoc.RootElement.GetProperty("access_token").GetString() ?? "";
        var expiresIn = mcDoc.RootElement.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 86400;

        // 先查有没有买过游戏（这个接口也要带令牌）
        var entReq = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/entitlements/mcstore");
        entReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        using var entResp = await Http.SendAsync(entReq, ct);
        var entText = await entResp.Content.ReadAsStringAsync(ct);

        var hasGame = entText.Contains("product_minecraft", StringComparison.OrdinalIgnoreCase) ||
                      entText.Contains("game_minecraft", StringComparison.OrdinalIgnoreCase);

        if (!hasGame)
            throw new InvalidOperationException("这个微软账号下没有查到 Minecraft 正版授权");

        // 档案（必须带令牌，否则一律 404）
        var profReq = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        profReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcToken);
        using var profResp = await Http.SendAsync(profReq, ct);
        var profText = await profResp.Content.ReadAsStringAsync(ct);

        if (!profResp.IsSuccessStatusCode)
            throw new InvalidOperationException("拉取游戏档案失败（HTTP " + (int)profResp.StatusCode + "）：" + ErrorOf(profText));

        using var profDoc = JsonDocument.Parse(profText);
        var uuid = profDoc.RootElement.GetProperty("id").GetString() ?? "";
        var name = profDoc.RootElement.GetProperty("name").GetString() ?? "";

        return new Account(uuid, name, refreshToken, mcToken,
            DateTime.UtcNow.AddSeconds(expiresIn));
    }

    private static async Task<(string Token, string Uhs)> PostXboxAsync(string url, string json, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // 常见：没开 Xbox 账号 / 未成年人限制
            if (text.Contains("2148916233", StringComparison.Ordinal))
                throw new InvalidOperationException("这个微软账号还没注册 Xbox 账号");
            if (text.Contains("2148916238", StringComparison.Ordinal))
                throw new InvalidOperationException("账号未成年，需要家长同意");
            throw new InvalidOperationException("Xbox 认证失败：" + ErrorOf(text));
        }

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var token = root.GetProperty("Token").GetString() ?? "";
        var uhs = "";
        if (root.TryGetProperty("DisplayClaims", out var dc) &&
            dc.TryGetProperty("xui", out var xui) &&
            xui.GetArrayLength() > 0)
            uhs = xui[0].GetProperty("uhs").GetString() ?? "";

        return (token, uhs);
    }

    private static string ErrorOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error_description", out var d))
                return d.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("errorMessage", out var m))
                return m.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("error", out var e))
                return e.GetString() ?? json;
        }
        catch { }
        return json;
    }

    // ===== 账号存档 =====

    private static string AccountFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "accounts.json");

    // 旧版的单账号文件，读到就搬进来
    private static string LegacyAccountFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearth", "account.json");

    private class AccountBook
    {
        public string Current { get; set; } = "";
        public List<Account> Accounts { get; set; } = new();
    }

    private static AccountBook ReadBook()
    {
        var book = new AccountBook();

        try
        {
            if (File.Exists(AccountFile))
            {
                book = JsonSerializer.Deserialize<AccountBook>(File.ReadAllText(AccountFile)) ?? new AccountBook();
                return book;
            }
        }
        catch { }

        // 迁移旧版单账号
        try
        {
            if (File.Exists(LegacyAccountFile))
            {
                var old = JsonSerializer.Deserialize<Account>(File.ReadAllText(LegacyAccountFile));
                if (old != null && !string.IsNullOrEmpty(old.Uuid))
                {
                    book.Accounts.Add(old);
                    book.Current = old.Uuid;
                    WriteBook(book);
                    File.Delete(LegacyAccountFile);
                }
            }
        }
        catch { }

        return book;
    }

    private static void WriteBook(AccountBook book)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AccountFile)!);
            File.WriteAllText(AccountFile, JsonSerializer.Serialize(book,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>所有登录过的账号</summary>
    public static List<Account> LoadAll()
    {
        var book = ReadBook();
        return book.Accounts
            .Where(a => !string.IsNullOrEmpty(a.Uuid))
            .GroupBy(a => a.Uuid)
            .Select(g => g.Last())
            .ToList();
    }

    /// <summary>当前账号</summary>
    public static Account? Load()
    {
        var book = ReadBook();
        if (book.Accounts.Count == 0) return null;

        var current = book.Accounts.FirstOrDefault(a => a.Uuid == book.Current);
        return current ?? book.Accounts[0];
    }

    /// <summary>存账号并设为当前（同 uuid 覆盖旧的令牌）</summary>
    public static void Save(Account account)
    {
        var book = ReadBook();
        book.Accounts.RemoveAll(a => a.Uuid == account.Uuid);
        book.Accounts.Insert(0, account);
        book.Current = account.Uuid;
        WriteBook(book);
    }

    /// <summary>切换当前账号</summary>
    public static void SetCurrent(string uuid)
    {
        var book = ReadBook();
        if (book.Accounts.All(a => a.Uuid != uuid)) return;
        book.Current = uuid;
        WriteBook(book);
    }

    /// <summary>删掉一个账号</summary>
    public static void Remove(string uuid)
    {
        var book = ReadBook();
        book.Accounts.RemoveAll(a => a.Uuid == uuid);

        if (book.Current == uuid)
            book.Current = book.Accounts.FirstOrDefault()?.Uuid ?? "";

        WriteBook(book);
    }

    /// <summary>清空全部账号</summary>
    public static void Clear()
    {
        try { if (File.Exists(AccountFile)) File.Delete(AccountFile); } catch { }
        try { if (File.Exists(LegacyAccountFile)) File.Delete(LegacyAccountFile); } catch { }
    }
}
