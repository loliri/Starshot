using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Starshot.Features.Update;

/// <summary>
/// apply hdiff 目录二进制 diff（CI: hdiffz -m -c-zstd-19 -D 生成）。
/// 用 native hpatchz.exe 子进程——跨进程读 old 目录，避开主进程加载的 dll 锁冲突。
/// hpatchz.exe 在 app-{tag}/ 程序目录（AppContext.BaseDirectory），CI 构建带，本地 debug 不带。
/// </summary>
internal static class HPatchInvoker
{
    public static async Task<bool> ApplyAsync(
        string oldDir,
        string patchFile,
        string newDir,
        CancellationToken ct
    )
    {
        string exe = FindHpatchz();
        if (exe is null)
        {
            Log.Warning("[HPatchInvoker] hpatchz.exe not found in {Dir}", AppContext.BaseDirectory);
            return false;
        }

        var psi = new ProcessStartInfo(exe, $"-f \"{oldDir}\" \"{patchFile}\" \"{newDir}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process? p = null;
        try
        {
            p = Process.Start(psi);
            if (p is null)
            {
                Log.Warning("[HPatchInvoker] Process.Start returned null");
                return false;
            }
            p.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log.Warning("[HPatchInvoker] {Data}", e.Data);
            };
            p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            Log.Information("[HPatchInvoker] hpatchz exited with code {Code}", p.ExitCode);
            return p.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (p is not null)
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch { }
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[HPatchInvoker] apply failed");
            return false;
        }
    }

    private static string? FindHpatchz()
    {
        // 程序目录（app-{tag}/，hpatchz.exe 跟主程序一起）
        string exe = Path.Combine(AppContext.BaseDirectory, "hpatchz.exe");
        return File.Exists(exe) ? exe : null;
    }
}
