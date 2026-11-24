using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace BlockLauncher;

/// <summary>皮肤、披风、头像</summary>
public static class SkinService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>头像（优先 crafatar，失败换 minotar）</summary>
    public static async Task<BitmapImage?> LoadAvatarAsync(string uuid, string name)
    {
        var id = uuid.Replace("-", "");
        var urls = new[]
        {
            "https://crafatar.com/avatars/" + id + "?size=96&overlay",
            "https://minotar.net/helm/" + name + "/96.png",
            "https://mc-heads.net/avatar/" + id + "/96"
        };

        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length < 200) continue;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { }
        }
        return null;
    }

    /// <summary>皮肤原图</summary>
    public static async Task<byte[]?> LoadSkinBytesAsync(string uuid)
    {
        var id = uuid.Replace("-", "");
        foreach (var url in new[]
                 {
                     "https://crafatar.com/skins/" + id,
                     "https://minotar.net/skin/" + id
                 })
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url);
                if (bytes.Length > 1000) return bytes;
            }
            catch { }
        }
        return null;
    }

    /// <summary>上传皮肤（variant: classic / slim）</summary>
    public static async Task<string> UploadSkinAsync(string accessToken, string filePath, string variant)
    {
        using var content = new MultipartFormDataContent();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", Path.GetFileName(filePath));
        content.Add(new StringContent(variant), "variant");

        using var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.minecraftservices.com/minecraft/profile/skins") { Content = content };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return "上传失败：" + Describe(text);

        return "";
    }

    public record CapeInfo(string Id, string Alias, bool Active, string Url);

    /// <summary>披风预览图：贴图 64x32，取左上 (0,0,10,16) 那块当背面</summary>
    public static async Task<System.Windows.Media.Imaging.BitmapSource?> LoadCapePreviewAsync(string url)
    {
        try
        {
            var https = url.Replace("http://", "https://", StringComparison.OrdinalIgnoreCase);
            var bytes = await Http.GetByteArrayAsync(https);

            var full = new System.Windows.Media.Imaging.BitmapImage();
            full.BeginInit();
            full.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            full.StreamSource = new MemoryStream(bytes);
            full.EndInit();
            full.Freeze();

            if (full.PixelWidth < 32 || full.PixelHeight < 16) return null;

            var cropped = new System.Windows.Media.Imaging.CroppedBitmap(
                full, new System.Windows.Int32Rect(0, 0, 10, 16));
            cropped.Freeze();
            return cropped;
        }
        catch { return null; }
    }

    /// <summary>披风列表：藏在 profile 接口的 capes 字段里（单独 /capes 是 404）</summary>
    public static async Task<(List<CapeInfo> Capes, string Error)> GetCapesAsync(string accessToken)
    {
        var list = new List<CapeInfo>();

        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return (list, "拿披风失败（HTTP " + (int)resp.StatusCode + "）：" + Describe(text));

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("capes", out var capes) &&
                capes.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in capes.EnumerateArray())
                {
                    var id = c.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
                    var alias = c.TryGetProperty("alias", out var a) ? a.GetString() ?? id : id;
                    var state = c.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";
                    var url = c.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(id))
                        list.Add(new CapeInfo(id, alias, state.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase), url));
                }
            }
        }
        catch (Exception ex)
        {
            return (list, ex.Message);
        }

        return (list, "");
    }

    /// <summary>换披风；capeId 传空表示脱下</summary>
    public static async Task<string> SetCapeAsync(string accessToken, string capeId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put,
            "https://api.minecraftservices.com/minecraft/profile/capes/active");

        if (string.IsNullOrEmpty(capeId))
            req.Method = HttpMethod.Delete;
        else
            req.Content = new StringContent(
                JsonSerializer.Serialize(new { capeId }),
                System.Text.Encoding.UTF8, "application/json");

        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return "切换失败：" + Describe(await resp.Content.ReadAsStringAsync());
        return "";
    }

    private static string Describe(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("errorMessage", out var m)) return m.GetString() ?? json;
            if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? json;
        }
        catch { }
        return json.Length > 200 ? json[..200] : json;
    }
}
