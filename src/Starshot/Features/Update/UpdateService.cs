using Microsoft.Extensions.Logging;
using SharpCompress.Readers;
using Starshot.Language;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Starshot.Features.Update;

public static class UpdateService
{
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.LoggerFactory.Create(b => { }).CreateLogger("UpdateService");

    public static async Task<(ReleaseInfo? update, string? latestTag)> CheckUpdateAsync(bool ignoreSkipped = true)
    {
#if DEBUG
        return (null, null);
#else
        // 不吞异常：网络失败向上抛（手动检查弹"更新失败"，启动检查由调用方 catch 静默）。
        // update=null 仅表示"确无新版本/被忽略/无 zip 资源"；latestTag 始终带 GitHub 最新版号（供"已是最新"提示显示）
        var release = await ReleaseClient.GetLatestReleaseAsync(AppConfig.EnablePreReleaseUpdateCheck);
        if (release is null) return (null, null);
        if (!TryParseVersion(AppConfig.AppVersion, out var current)) return (null, release.TagName);
        if (release.Version <= current) return (null, release.TagName);
        // 只有自动检查才跳过用户忽略的版本；手动检查无视忽略
        if (ignoreSkipped && Version.TryParse(AppConfig.IgnoreVersion, out var ignore) && release.Version <= ignore) return (null, release.TagName);
        if (string.IsNullOrWhiteSpace(release.ZipUrl)) return (null, release.TagName);
        return (release, release.TagName);
#endif
    }


    /// <summary>
    /// 真流式解压：网络流直连 SharpCompress Reader，逐 entry 写到 destDir。不落 zip、不依赖中央目录。
    /// 进度按网络已读字节 / Content-Length 计算（流式下载解压一体）。
    /// </summary>
    public static async Task ExtractToDirectoryAsync(string zipUrl, string destDir, IProgress<(int percent, string bytesText)>? progress, CancellationToken ct = default)
    {
        string destFull = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        progress?.Report((0, ""));

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Starshot");
        // ResponseHeadersRead：只读响应头，拿 Content-Length 后直接拿流（不缓冲整个响应体）
        using var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var httpStream = await resp.Content.ReadAsStreamAsync(ct);
        // CountingStream 包装统计已读字节，SharpCompress 透过它读网络流
        using var counting = new CountingStream(httpStream);
        using var reader = ReaderFactory.Open(counting);

        var buf = new byte[81920];
        while (reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory) continue;
            string? key = reader.Entry.Key;
            if (string.IsNullOrEmpty(key)) continue;

            // zip slip 防护：目标必须在 destDir 下
            string dest = Path.GetFullPath(Path.Combine(destDir, key.Replace('/', Path.DirectorySeparatorChar)));
            if (!dest.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var entryStream = reader.OpenEntryStream();
            using var fs = File.Create(dest);
            int n;
            while ((n = await entryStream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
            {
                await fs.WriteAsync(buf, 0, n, ct);
                if (total > 0)
                {
                    // 留 100% 给调用方 await 返回（API return 作完成标志），中间只到 99
                    int pct = (int)(counting.BytesRead * 99 / total.Value);
                    progress?.Report((pct, $"{FormatSize(counting.BytesRead)} / {FormatSize(total.Value)}"));
                }
            }
        }
        // 不在这里报 100%：完成标志是本方法 return（调用方 await 返回），progress 末尾到 99
    }


    public static async Task StartUpdateAsync(ReleaseInfo info, IProgress<(int percent, string bytesText)> progress, CancellationToken ct = default)
    {
        string root = AppConfig.UserDataFolder;
        string versionIni = Path.Combine(root, "version.ini");
        string versionIniBak = versionIni + ".bak";
        string launcherExe = Path.Combine(root, "Starshot.exe");
        string launcherBak = launcherExe + ".bak";
        // app-{new}/ 用原始 tag（含 -Preview 后缀），跟 zip 实际目录名对齐；Version 是去后缀的，不能拿来拼目录
        string appNewDir = Path.Combine(root, "app-" + info.TagName);

        // 备份 version.ini + 启动器（Copy 留原件，解压覆盖原件；失败还原 .bak）
        try { if (File.Exists(versionIni)) File.Copy(versionIni, versionIniBak, overwrite: true); } catch { }
        try { if (File.Exists(launcherExe)) File.Copy(launcherExe, launcherBak, overwrite: true); } catch { }

        // 尝试差分更新（链式 delta）；失败自动 fallback 整包
        bool deltaOK = false;
        try
        {
            deltaOK = await TryDeltaUpdateAsync(info, root, appNewDir, progress, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Delta update failed, falling back to full package");
            deltaOK = false;
        }
        if (!deltaOK)
        {
            // fallback：删残缺 app-{new}/（差分可能创建了半成品）
            try { if (Directory.Exists(appNewDir)) Directory.Delete(appNewDir, recursive: true); } catch { }

            await ExtractToDirectoryAsync(info.ZipUrl, root, progress, ct);
        }

        // 校验包结构：root/Starshot.exe + version.ini + app-{new}/
        if (!File.Exists(launcherExe)
            || !File.Exists(versionIni)
            || !Directory.Exists(appNewDir))
        {
            throw new InvalidDataException("Update package structure invalid");
        }

        // 成功：删 .bak
        try { if (File.Exists(versionIniBak)) File.Delete(versionIniBak); } catch { }
        try { if (File.Exists(launcherBak)) File.Delete(launcherBak); } catch { }

        // 启动器接管（--clean=<pid> 清旧 app-*，旧主进程锁着时按 pid 强杀）+ 退出本进程
        Process.Start(new ProcessStartInfo(launcherExe) { UseShellExecute = true, Arguments = $"--clean={Environment.ProcessId}" });
        App.Current.Exit();
    }


    /// <summary>
    /// 差分更新（链式 delta）：复制当前 app 目录 → 依次解压 delta 链覆盖 + 删 manifest deletedFiles。
    /// 返回 true = 成功；false/异常 = 调用方 fallback 整包。
    /// </summary>
    private static async Task<bool> TryDeltaUpdateAsync(
        ReleaseInfo info, string root, string appNewDir,
        IProgress<(int percent, string bytesText)> progress, CancellationToken ct)
    {
        // 当前版本的 tag（从 version.ini 读 AppConfig.AppVersion 取得；这里用 UserDataFolder 下 version.ini）
        string versionIni = Path.Combine(root, "version.ini");
        string? currentTag = null;
        if (File.Exists(versionIni))
        {
            string line = File.ReadAllText(versionIni).TrimStart('\xEF', '\xBB', '\xBF');
            var eq = line.IndexOf('=');
            if (eq >= 0) currentTag = line[(eq + 1)..].Trim().ToLowerInvariant();
        }
        if (string.IsNullOrEmpty(currentTag)) return false;

        // 本地构建（Local）没有 GitHub release 对应的 delta，直接走整包
        if (currentTag == "local") return false;

        // 查 delta 链
        int maxLayers = AppConfig.DeltaUpdateMaxLayers;
        var chain = await ReleaseClient.GetDeltaChainAsync(currentTag, info.TagName, maxLayers, ct);
        if (chain is null || chain.Count == 0) return false;

        // 当前 app 目录
        string currentAppDir = Path.Combine(root, "app-" + currentTag);
        if (!Directory.Exists(currentAppDir))
        {
            // version.ini 里的 tag 可能含大小写差异，试一下
            var found = Directory.GetDirectories(root, "app-*")
                .FirstOrDefault(d => string.Equals(
                    Path.GetFileName(d)["app-".Length..],
                    currentTag,
                    StringComparison.OrdinalIgnoreCase));
            if (found is null) return false;
            currentAppDir = found;
        }

        _logger?.LogInformation("Delta update: {Chain} layers from {From} to {To}", chain.Count, currentTag, info.TagName);

        // 1. 复制当前 app 目录 → 新 app 目录（本地磁盘，SSD 快）
        // 用 CopyEachFile 而非 Directory.Copy（后者在 .NET 不存在；用递归）
        CopyDirectory(currentAppDir, appNewDir);

        // 2. 依次应用 delta 链
        for (int i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            _logger?.LogInformation("Delta layer {Index}: {From} -> {To}", i + 1, link.FromTag, link.ToTag);

            // 进度：按链层数均分（每层占等比例）
            int basePercent = (int)((double)i / chain.Count * 100);
            int nextPercent = (int)((double)(i + 1) / chain.Count * 100);
            var layerProgress = new Progress<(int percent, string bytesText)>(p =>
            {
                int pct = basePercent + (int)((double)p.percent / 100 * (nextPercent - basePercent));
                progress.Report((pct, p.bytesText));
            });

            // 解压 delta.zip 到 root（覆盖变化文件 + manifest.json + version.ini）
            await ExtractToDirectoryAsync(link.DeltaUrl, root, layerProgress, ct);

            // 读 manifest.json → 删除 deletedFiles
            string manifestPath = Path.Combine(root, "manifest.json");
            if (File.Exists(manifestPath))
            {
                string json = await File.ReadAllTextAsync(manifestPath, ct);
                var manifest = JsonSerializer.Deserialize<DeltaManifest>(json);
                if (manifest?.DeletedFiles is not null)
                {
                    foreach (var rel in manifest.DeletedFiles)
                    {
                        string abs = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                        try
                        {
                            if (File.Exists(abs)) File.Delete(abs);
                        }
                        catch { /* 删除失败不致命 */ }
                    }
                }
                // 清理 manifest.json
                try { File.Delete(manifestPath); } catch { }
            }
        }

        // 校验 delta 结果：version.ini 版本应是 target
        if (File.Exists(versionIni))
        {
            string line = File.ReadAllText(versionIni).TrimStart('\xEF', '\xBB', '\xBF');
            var eq = line.IndexOf('=');
            if (eq >= 0)
            {
                string ver = line[(eq + 1)..].Trim().ToLowerInvariant();
                if (!string.Equals(ver, info.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning("Delta result version mismatch: expected {Expected}, got {Actual}", info.TagName, ver);
                    return false;
                }
            }
        }

        progress.Report((100, ""));
        _logger?.LogInformation("Delta update completed successfully");
        return true;
    }


    /// <summary>递归复制目录（Directory.Copy 在 .NET 不存在）</summary>
    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }


    /// <summary>delta.zip 里的 manifest.json 反序列化模型</summary>
    private sealed class DeltaManifest
    {
        [JsonPropertyName("deletedFiles")]
        public List<string>? DeletedFiles { get; set; }
    }


    /// <summary>
    /// 解析版本字符串（version.ini 的 AppVersion 或 tag）：去 v 前缀 + pre-release 后缀（-Preview/-beta/-rc）再 Version.TryParse。
    /// 本地构建（无 version.ini 或 "Local"）按 0.0.0 最低版本处理，可更新到任意 CI/CD release（方便测试更新流程）。
    /// </summary>
    private static bool TryParseVersion(string? raw, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return true;
        string s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        int dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];
        if (Version.TryParse(s, out var v)) version = v;
        return true;
    }


    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes}B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
    }


    /// <summary>
    /// 只读包装流，统计已读字节总数（用于流式解压进度）。
    /// </summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        public long BytesRead { get; private set; }
        public CountingStream(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            BytesRead += n;
            return n;
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int n = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            BytesRead += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
