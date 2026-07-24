using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    public bool Prerelease { get; init; }
    public DateTimeOffset PublishedAt { get; init; }
}

/// <summary>
/// 链式 delta 的一环：from 版本的 delta.zip 下载 URL + 目标 tag。
/// </summary>
public sealed class DeltaChainLink
{
    public string FromTag { get; init; } = "";
    public string ToTag { get; init; } = "";
    public string DeltaUrl { get; init; } = "";
}


public static class ReleaseClient
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/loliri/Starshot/releases/latest";
    private const string AllReleasesUrl = "https://api.github.com/repos/loliri/Starshot/releases";

    private static readonly HttpClient _http = CreateClient();


    private static HttpClient CreateClient()
    {
        // GitHub API 不走系统代理（开关开 = UseProxy=false 直连 api.github.com）。仅 API，zip 下载走 CDN 不受影响。
        var handler = new HttpClientHandler { UseProxy = !AppConfig.EnableGithubApiNoProxy };
        var c = new HttpClient(handler);
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

        // 找 Starshot-{tag_name}-win-{arch}.zip（arch 跟随当前进程架构：x64 / arm64）
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        string zipName = $"Starshot-{payload.TagName}-win-{arch}.zip";
        var asset = payload.Assets?.FirstOrDefault(a => string.Equals(a.Name, zipName, StringComparison.OrdinalIgnoreCase));
        string zipUrl = asset?.BrowserDownloadUrl ?? "";

        return new ReleaseInfo
        {
            Version = version,
            TagName = (payload.TagName ?? "").Trim(),
            ZipUrl = zipUrl,
            Notes = payload.Body ?? "",
            Prerelease = payload.Prerelease,
            PublishedAt = payload.PublishedAt,
        };
    }


    private sealed class GitHubReleasePayload
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }


    // delta asset 名格式：Starshot-{toTag}-from-{fromTag}-win-{arch}-delta.zip
    private static readonly Regex DeltaAssetPattern = new(
        @"^Starshot-(.+)-from-(.+)-win-(.+)-delta\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);


    /// <summary>
    /// 构建链式 delta：从 currentTag 到 targetTag，在 GitHub releases 的 assets 里逐层找 delta。
    /// 返回 null = 找不到完整链（或超过 maxLayers）→ 调用方应 fallback 整包。
    /// </summary>
    public static async Task<List<DeltaChainLink>?> GetDeltaChainAsync(
        string currentTag, string targetTag, int maxLayers, CancellationToken ct = default)
    {
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

        // 拉 /releases 列表（30 条，够覆盖最近版本）
        using var resp = await _http.GetAsync(AllReleasesUrl, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var releases = await JsonSerializer.DeserializeAsync<GitHubReleasePayload[]>(stream, cancellationToken: ct);
        if (releases is null || releases.Length == 0) return null;

        // 构建索引：fromTag → (toTag, deltaUrl, arch)
        // 每个 release 的 assets 里找 delta asset，解析 from/to/arch
        var deltaMap = new Dictionary<string, (string toTag, string deltaUrl)>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in releases)
        {
            if (rel.Assets is null) continue;
            foreach (var asset in rel.Assets)
            {
                if (string.IsNullOrEmpty(asset.Name) || string.IsNullOrEmpty(asset.BrowserDownloadUrl)) continue;
                var match = DeltaAssetPattern.Match(asset.Name);
                if (!match.Success) continue;
                string toTag = match.Groups[1].Value;
                string fromTag = match.Groups[2].Value;
                string assetArch = match.Groups[3].Value;
                if (!string.Equals(assetArch, arch, StringComparison.OrdinalIgnoreCase)) continue;
                // 同一 fromTag 取第一条（releases 按时间倒序，最新 release 的 delta 优先）
                if (!deltaMap.ContainsKey(fromTag))
                {
                    deltaMap[fromTag] = (toTag, asset.BrowserDownloadUrl);
                }
            }
        }

        // 从 currentTag 逐层走链
        var chain = new List<DeltaChainLink>();
        string cursor = currentTag;
        for (int i = 0; i < maxLayers; i++)
        {
            if (!deltaMap.TryGetValue(cursor, out var next))
            {
                // 断链
                return null;
            }
            chain.Add(new DeltaChainLink
            {
                FromTag = cursor,
                ToTag = next.toTag,
                DeltaUrl = next.deltaUrl,
            });
            cursor = next.toTag;
            // 到达目标（忽略大小写，tag 可能含 -Preview 后缀差异）
            if (string.Equals(cursor, targetTag, StringComparison.OrdinalIgnoreCase))
            {
                return chain;
            }
        }
        // 超过 maxLayers 还没到目标
        return null;
    }
}
