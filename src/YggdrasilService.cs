using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlockLauncher;

/// <summary>
/// 第三方皮肤站的 Yggdrasil 认证（红石、LittleSkin 这类）。
/// 登录后拿到 accessToken 和角色，启动时配合 authlib-injector 就能用站内皮肤、进支持皮肤站的服务器。
/// </summary>
public static class YggdrasilService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };

    static YggdrasilService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "Hearth/" + AppSettings.Version);
        Http.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public record Profile(string Id, string Name);
    public record Result(string? AccessToken, Profile? Profile, string? Error);

    /// <summary>常用皮肤站，省得手打</summary>
    public static readonly (string Name, string Url)[] KnownSites =
    {
        ("LittleSkin", "https://littleskin.cn/api/yggdrasil"),
        ("红石皮肤站", "https://skin.prinzeugen.net/api/yggdrasil"),
        ("Blessing Skin", "https://skin.mcim.fun/api/yggdrasil"),
    };

    public static async Task<Result> AuthenticateAsync(string serverUrl, string email, string password,
        CancellationToken ct = default)
    {
        try
        {
            var baseUrl = serverUrl.TrimEnd('/');

            var payload = JsonSerializer.Serialize(new
            {
                agent = new { name = "Minecraft", version = 1 },
                username = email,
                password,
                clientToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(baseUrl + "/authserver/authenticate", content, ct);

            var text = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return new Result(null, null, FriendlyError(text));

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            var token = root.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                return new Result(null, null, "皮肤站没有返回登录令牌");

            string id = "", name = "";
            if (root.TryGetProperty("selectedProfile", out var sp) && sp.ValueKind == JsonValueKind.Object)
            {
                id = sp.TryGetProperty("id", out var i) ? (i.GetString() ?? "") : "";
                name = sp.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
            }

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                return new Result(null, null, "这个账号在皮肤站里还没有角色，先去网站上建一个");

            return new Result(token, new Profile(id, name), null);
        }
        catch (Exception ex)
        {
            return new Result(null, null, "连不上皮肤站：" + ex.Message);
        }
    }

    private static string FriendlyError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out var m))
                return m.GetString() ?? body;
            if (doc.RootElement.TryGetProperty("message", out var m2))
                return m2.GetString() ?? body;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(body)) return "皮肤站拒绝了登录（账号或密码不对？）";
        return body.Length > 200 ? body[..200] : body;
    }
}
