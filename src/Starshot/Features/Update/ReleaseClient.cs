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
    public string ZipUrl { get; init; } = "";
    public string Notes { get; init; } = "";
}


public static class ReleaseClient
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/loliri/Starshot/releases/latest";

    private static readonly HttpClient _http = CreateClient();


    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Starshot/{AppConfig.AppVersion}");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }


    public static async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(LatestReleaseUrl, ct);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<GitHubReleasePayload>(stream, cancellationToken: ct);
            if (payload is null) return null;

            // tag_name 去前缀 v 再 Version.TryParse
            string tag = (payload.TagName ?? "").Trim();
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag[1..];
            if (!Version.TryParse(tag, out var version)) return null;

            // 找 Starshot-{tag_name}-win-x64.zip（zip 名用原始 tag_name，跟 workflow 一致）
            string zipName = $"Starshot-{payload.TagName}-win-x64.zip";
            var asset = payload.Assets?.FirstOrDefault(a => string.Equals(a.Name, zipName, StringComparison.OrdinalIgnoreCase));
            string zipUrl = asset?.BrowserDownloadUrl ?? "";

            return new ReleaseInfo
            {
                Version = version,
                ZipUrl = zipUrl,
                Notes = payload.Body ?? "",
            };
        }
        catch
        {
            return null;
        }
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
