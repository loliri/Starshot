using Microsoft.Extensions.Logging;
using Serilog;
using SharpCompress.Readers;
using Starshot.Language;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SemVersion = SemanticVersioning.Version;

namespace Starshot.Features.Update;

public static class UpdateService
{
    private static readonly Microsoft.Extensions.Logging.ILogger _logger = LoggerFactory.Create(b => b.AddSerilog()).CreateLogger("UpdateService");

    public static async Task<(ReleaseInfo? update, string? latestTag)> CheckUpdateAsync(bool ignoreSkipped = true)
    {
#if DEBUG
        return (null, null);
#else
        // 不吞异常：网络失败向上抛（手动检查弹"更新失败"，启动检查由调用方 catch 静默）。
        // update=null 仅表示"确无新版本/被忽略/无 zip 资源"；latestTag 始终带 GitHub 最新版号（供"已是最新"提示显示）
        var release = AppConfig.UpdateSource == 0
            ? await ReleaseClient.GetLatestReleaseCDNAsync(AppConfig.EnablePreReleaseUpdateCheck)
            : await ReleaseClient.GetLatestReleaseGitHubAsync(AppConfig.EnablePreReleaseUpdateCheck);
        if (release is null) return (null, null);
        if (!TryParseVersion(AppConfig.AppVersion, out var current)) return (null, release.TagName);
        if (release.Version <= current) return (null, release.TagName);
        // 只有自动检查才跳过用户忽略的版本；手动检查无视忽略
        if (ignoreSkipped && SemVersion.TryParse(AppConfig.IgnoreVersion, out var ignore) && release.Version <= ignore) return (null, release.TagName);
        // ZipUrl 空 = GitHub 源 asset 没找到（没下载资源）。CDN「已最新」不走到这（上面 Version<=current 已兜）
        if (string.IsNullOrWhiteSpace(release.ZipUrl)) return (null, release.TagName);
        return (release, release.TagName);
#endif
    }


    /// <summary>
    /// 真流式解压：网络流直连 SharpCompress Reader，逐 entry 写到 destDir。不落 zip、不依赖中央目录。
    /// 进度按网络已读字节 / Content-Length 计算（流式下载解压一体）。
    /// </summary>
    public static async Task ExtractToDirectoryAsync(string zipUrl, string destDir, IProgress<(int percent, string bytesText)>? progress, CancellationToken ct = default, bool disableCert = false)
    {
        string destFull = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        progress?.Report((0, ""));

        var handler = new HttpClientHandler();
        if (disableCert)
        {
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        }
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Starshot");
        // ResponseHeadersRead：只读响应头，拿 Content-Length 后直接拿流（不缓冲整个响应体）
        using var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var httpStream = await resp.Content.ReadAsStreamAsync(ct);
        // CountingStream 包装统计已读字节，SharpCompress 透过它读网络流
        using var counting = new CountingStream(httpStream);
        using var reader = ReaderFactory.OpenReader(counting);

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


    /// <summary>
    /// 流式下载文件到磁盘，进度按网络字节 / Content-Length（无解压，patch 直接落盘）。
    /// </summary>
    public static async Task DownloadFileAsync(string url, string destFile, IProgress<(int percent, string bytesText)>? progress, CancellationToken ct = default)
    {
        progress?.Report((0, ""));
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Starshot");
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var httpStream = await resp.Content.ReadAsStreamAsync(ct);
        using var counting = new CountingStream(httpStream);
        using var fs = File.Create(destFile);
        var buf = new byte[81920];
        int n;
        while ((n = await counting.ReadAsync(buf, 0, buf.Length, ct)) > 0)
        {
            await fs.WriteAsync(buf, 0, n, ct);
            if (total > 0)
            {
                int pct = (int)(counting.BytesRead * 99 / total.Value);
                progress?.Report((pct, $"{FormatSize(counting.BytesRead)} / {FormatSize(total.Value)}"));
            }
        }
    }


    public static async Task StartUpdateAsync(ReleaseInfo info, IProgress<(int percent, string bytesText)> progress, CancellationToken ct = default, bool forceFull = false)
    {
        string root = AppConfig.UserDataFolder;
        string versionIni = Path.Combine(root, "version.ini");
        string launcherExe = Path.Combine(root, "Starshot.exe");
        // app-{new}/ 用原始 tag（含 -Preview 后缀），跟 zip 实际目录名对齐；Version 是去后缀的，不能拿来拼目录
        string appNewDir = Path.Combine(root, "app-" + info.TagName);

        try
        {
            bool deltaOK = false;
            if (!forceFull)
            {
                try
                {
                    deltaOK = AppConfig.UpdateSource == 0
                        ? await TryDeltaUpdateCDNAsync(info, root, appNewDir, progress, ct)
                        : await TryDeltaUpdateGitHubAsync(info, root, appNewDir, progress, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Delta update failed, falling back to full package");
                }
            }
            if (!deltaOK)
            {
                try { if (Directory.Exists(appNewDir)) Directory.Delete(appNewDir, recursive: true); } catch { }
                _logger?.LogInformation("Falling back to full package update");
                await ExtractToDirectoryAsync(info.ZipUrl, root, progress, ct);
                if (!File.Exists(launcherExe) || !File.Exists(versionIni) || !Directory.Exists(appNewDir))
                    throw new InvalidDataException("Update package structure invalid");
            }

            try { File.WriteAllText(versionIni, $"version={info.TagName}"); } catch { }

            Process.Start(new ProcessStartInfo(launcherExe) { UseShellExecute = true, Arguments = $"--clean={Environment.ProcessId}" });
            // launcher 已取消强杀旧进程，app 必须自己保证终止以释放 app-{old}/ 目录锁：
            // 先软退走 UI 关闭流程，2s 后硬退兜底（SystemTrayWindow 的 Closing 无条件 Cancel 会卡住软退）
            _ = Task.Run(async () => { await Task.Delay(2000); Environment.Exit(0); });
            App.Current.Exit();
        }
        catch (Exception)
        {
            // 失败/取消：清残缺 appNewDir + 回滚 version.ini 到当前运行版本
            try { if (Directory.Exists(appNewDir)) Directory.Delete(appNewDir, recursive: true); } catch { }
            try { File.WriteAllText(versionIni, $"version={AppConfig.AppVersion}"); } catch { }
            throw;
        }
    }


    /// <summary>
    /// 差分更新（链式 delta）：hpatchz 子进程逐层 apply，每层 old + patch → new；
    /// 中间结果作下一层 old，最后一步落 appNewDir。返回 true = 成功；false/异常 = 调用方 fallback 整包。
    /// </summary>
    private static async Task<bool> TryDeltaUpdateGitHubAsync(
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
        if (string.IsNullOrEmpty(currentTag))
        {
            _logger?.LogInformation("Delta skipped: no version.ini, falling back to full package");
            return false;
        }

        // 本地构建（Local）没有 GitHub release 对应的 delta，直接走整包
        if (currentTag == "local")
        {
            _logger?.LogInformation("Delta skipped: Local build, falling back to full package");
            return false;
        }

        // 查 delta 链
        int maxLayers = AppConfig.DeltaUpdateMaxLayers;
        var chain = await ReleaseClient.GetDeltaChainGitHubAsync(currentTag, info.TagName, maxLayers, ct);
        if (chain is null || chain.Count == 0)
        {
            _logger?.LogInformation("Delta skipped: no chain found from {From} to {To} (max {Max} layers), falling back to full package", currentTag, info.TagName, maxLayers);
            return false;
        }

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
            if (found is null)
            {
                _logger?.LogInformation("Delta skipped: current app dir not found for tag {Tag}, falling back to full package", currentTag);
                return false;
            }
            currentAppDir = found;
        }

        _logger?.LogInformation("Delta update: {Chain} layers from {From} to {To}", chain.Count, currentTag, info.TagName);

        // 链式 apply：每层 old + patch → new；中间结果作下一层 old，最后一步落到 appNewDir
        string currentApp = currentAppDir;
        for (int i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            _logger?.LogInformation("Delta layer {Index}: {From} -> {To}", i + 1, link.FromTag, link.ToTag);

            // 进度：单层直接透传；多层按层均分，右边显示层号
            IProgress<(int percent, string bytesText)> layerProgress;
            if (chain.Count == 1)
            {
                layerProgress = progress;
            }
            else
            {
                int basePercent = (int)((double)i / chain.Count * 100);
                int nextPercent = (int)((double)(i + 1) / chain.Count * 100);
                layerProgress = new Progress<(int percent, string bytesText)>(p =>
                {
                    int pct = basePercent + (int)((double)p.percent / 100 * (nextPercent - basePercent));
                    progress.Report((pct, $"{i + 1}/{chain.Count}  {p.bytesText}"));
                });
            }

            bool isLast = i == chain.Count - 1;
            string nextApp = isLast ? appNewDir : Path.Combine(root, ".delta-step-" + i);

            string patchFile = Path.Combine(root, ".diff-" + Guid.NewGuid().ToString("N") + ".patch");
            try
            {
                await DownloadFileAsync(link.DeltaUrl, patchFile, layerProgress, ct);
                if (!await HPatchInvoker.ApplyAsync(currentApp, patchFile, nextApp, ct))
                {
                    _logger?.LogWarning("Delta layer {Index}: hpatchz apply failed", i + 1);
                    return false;
                }
            }
            finally
            {
                try { if (File.Exists(patchFile)) File.Delete(patchFile); } catch { }
            }

            // 清理中间步骤目录（保留起始 currentAppDir，用户回滚兜底；最终 appNewDir 留给 launcher 接管）
            if (i > 0) try { Directory.Delete(currentApp, recursive: true); } catch { }
            currentApp = nextApp;
        }

        progress.Report((100, ""));
        _logger?.LogInformation("Delta update completed successfully");
        return true;
    }


    /// <summary>
    /// CDN 差分更新：版本 manifest 的 diffs 找 from==当前 → 单 diff（CDN 每 version 对最近 5 个 target 各打一个 diff，命中即一步到位，无链）。
    /// 返回 true=成功；false/异常=调用方走整包。
    /// </summary>
    private static async Task<bool> TryDeltaUpdateCDNAsync(
        ReleaseInfo info, string root, string appNewDir,
        IProgress<(int percent, string bytesText)> progress, CancellationToken ct)
    {
        // 当前版本 tag
        string versionIni = Path.Combine(root, "version.ini");
        string? currentTag = null;
        if (File.Exists(versionIni))
        {
            string line = File.ReadAllText(versionIni).TrimStart('\xEF', '\xBB', '\xBF');
            var eq = line.IndexOf('=');
            if (eq >= 0) currentTag = line[(eq + 1)..].Trim().ToLowerInvariant();
        }
        if (string.IsNullOrEmpty(currentTag))
        {
            _logger?.LogInformation("CDN delta skipped: no version.ini");
            return false;
        }
        if (currentTag == "local")
        {
            _logger?.LogInformation("CDN delta skipped: Local build");
            return false;
        }

        // 拿目标版本 manifest
        var vm = await ReleaseClient.GetVersionManifestCDNAsync(info.TagName, ct);
        if (vm is null)
        {
            _logger?.LogInformation("CDN delta skipped: version manifest not found for {Tag}", info.TagName);
            return false;
        }

        // 当前架构
        string arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var archManifest = arch == "x64" ? vm.X64 : vm.Arm64;
        if (archManifest is null)
        {
            _logger?.LogInformation("CDN delta skipped: no {Arch} manifest", arch);
            return false;
        }

        // diffs 找 from == 当前版本
        var diff = archManifest.Diffs?.FirstOrDefault(d => string.Equals(d.From, currentTag, StringComparison.OrdinalIgnoreCase));
        if (diff is null)
        {
            _logger?.LogInformation("CDN delta skipped: no diff from {From} to {To} (not in 5 targets)", currentTag, info.TagName);
            return false;
        }

        // 当前 app 目录
        string currentAppDir = Path.Combine(root, "app-" + currentTag);
        if (!Directory.Exists(currentAppDir))
        {
            var found = Directory.GetDirectories(root, "app-*")
                .FirstOrDefault(d => string.Equals(
                    Path.GetFileName(d)["app-".Length..],
                    currentTag,
                    StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                _logger?.LogInformation("CDN delta skipped: current app dir not found for tag {Tag}", currentTag);
                return false;
            }
            currentAppDir = found;
        }

        _logger?.LogInformation("CDN delta update: {From} -> {To}", currentTag, info.TagName);

        // 下载 diff.patch（hdiffz 内置 zstd 压缩，直接文件，无 zip 容器）
        string diffUrl = $"{AppConfig.CdnBase}/release/{info.TagName}/{diff.File}";
        string patchFile = Path.Combine(root, ".diff-" + Guid.NewGuid().ToString("N") + ".patch");
        try
        {
            await DownloadFileAsync(diffUrl, patchFile, progress, ct);
            if (!await HPatchInvoker.ApplyAsync(currentAppDir, patchFile, appNewDir, ct))
            {
                _logger?.LogWarning("CDN delta: hpatchz apply failed");
                return false;
            }
        }
        finally
        {
            try { if (File.Exists(patchFile)) File.Delete(patchFile); } catch { }
        }

        // launcher 有实质更新（manifest.launcher 非空）：单独拉取替换 root/Starshot.exe。
        // 失败不致命——记 warning 用旧 launcher（delta 主体已成功），下次更新再试
        if (!string.IsNullOrEmpty(archManifest.Launcher))
        {
            string tmpLauncher = Path.Combine(root, ".launcher-new.exe");
            try
            {
                string launcherUrl = $"{AppConfig.CdnBase}/release/{info.TagName}/{archManifest.Launcher}";
                await DownloadFileAsync(launcherUrl, tmpLauncher, null, ct);
                File.Move(tmpLauncher, Path.Combine(root, "Starshot.exe"), overwrite: true);
            }
            catch (OperationCanceledException) { throw; }  // 用户取消要中止更新，不能当「launcher 失败保留旧的」吞掉
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to update launcher, keeping old one");
                try { if (File.Exists(tmpLauncher)) File.Delete(tmpLauncher); } catch { }
            }
        }

        progress.Report((100, ""));
        _logger?.LogInformation("CDN delta update completed successfully");
        return true;
    }


    /// <summary>
    /// 解析版本字符串（version.ini 的 AppVersion 或 tag）：去 v 前缀，保留 prerelease 用 SemVersion 比。
    /// 本地构建（无 version.ini 或 "Local"）按 0.0.0 最低版本处理，可更新到任意 CI/CD release（方便测试更新流程）。
    /// </summary>
    private static bool TryParseVersion(string? raw, out SemVersion version)
    {
        version = new SemVersion(0, 0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return true;
        string s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        if (SemVersion.TryParse(s, out var v)) version = v;
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
