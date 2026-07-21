using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
namespace Starshot.Features.Update;

public sealed class ReleaseInfo
{
    public Version Version { get; init; } = new();
    /// <summary>原始 tag_name（如 0.3.1-Preview）。zip 里 app-{TagName}/ 目录名用它，不能用去后缀的 Version。</summary>
    public string TagName { get; init; } = "";
    public string ZipUrl { get; init; } = "";
    public string Notes { get; init; } = "";
}


public static class ReleaseClient
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/loliri/Starshot/releases/latest";
    private const string AllReleasesUrl = "https://api.github.com/repos/loliri/Starshot/releases";

    private static readonly HttpClient _http = CreateClient();


    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Starshot/{AppConfig.AppVersion}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }


    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(bool includePrerelease, CancellationToken ct = default)
    {
        // 不吞网络异常：让 HttpRequestException 向上抛，调用方据此区分"无新版本"与"检查失败"
        // pre-release 用 /releases 取列表第一个（最新，含 pre-release）；正式版用 /releases/latest（跳过 pre-release）
        string url = includePrerelease ? AllReleasesUrl : LatestReleaseUrl;
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        GitHubReleasePayload? payload;
        if (includePrerelease)
        {
            var arr = await JsonSerializer.DeserializeAsync<GitHubReleasePayload[]>(stream, cancellationToken: ct);
            payload = arr?.FirstOrDefault();
        }
        else
        {
            payload = await JsonSerializer.DeserializeAsync<GitHubReleasePayload>(stream, cancellationToken: ct);
        }
        if (payload is null) return null;
        return BuildReleaseInfo(payload);
    }


    private static ReleaseInfo? BuildReleaseInfo(GitHubReleasePayload payload)
    {
        // tag_name 去前缀 v；再去 pre-release 后缀（-Preview / -beta / -rc1 等）再 Version.TryParse
        string tag = (payload.TagName ?? "").Trim();
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag[1..];
        int dash = tag.IndexOf('-');
        if (dash > 0) tag = tag[..dash];
        if (!Version.TryParse(tag, out var version)) return null;

        // 找 Starshot-{tag_name}-win-x64.zip（zip 名用原始 tag_name，跟 workflow 一致）
        string zipName = $"Starshot-{payload.TagName}-win-x64.zip";
        var asset = payload.Assets?.FirstOrDefault(a => string.Equals(a.Name, zipName, StringComparison.OrdinalIgnoreCase));
        string zipUrl = asset?.BrowserDownloadUrl ?? "";

        return new ReleaseInfo
        {
            Version = version,
            TagName = (payload.TagName ?? "").Trim(),
            ZipUrl = zipUrl,
            Notes = payload.Body ?? "",
        };
    }


    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
