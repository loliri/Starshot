using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Starshot.Helpers;

/// <summary>
/// 只读：枚举文件夹算总大小 / 文件数，格式化人类可读。不触碰任何捕获/保存逻辑。
/// </summary>
public static class StorageStatsHelper
{

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".apng", ".jpg", ".jpeg", ".jpe", ".jfif",
        ".avif", ".heic", ".heif", ".jxl", ".webp", ".bmp", ".tif", ".tiff",
    };


    /// <summary>
    /// 递归求目录总大小（字节）。目录不存在返回 0。
    /// </summary>
    public static long GetDirectorySize(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string dir = stack.Pop();
            IEnumerator<string>? files = null;
            try
            {
                files = Directory.EnumerateFiles(dir).GetEnumerator();
                while (files.MoveNext())
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(files.Current);
                    if (fi.Exists)
                    {
                        total += fi.Length;
                    }
                }
            }
            catch { }
            finally
            {
                files?.Dispose();
            }
            IEnumerator<string>? dirs = null;
            try
            {
                dirs = Directory.EnumerateDirectories(dir).GetEnumerator();
                while (dirs.MoveNext())
                {
                    stack.Push(dirs.Current);
                }
            }
            catch { }
            finally
            {
                dirs?.Dispose();
            }
        }
        return total;
    }


    /// <summary>
    /// 递归统计图片文件数量（按扩展名）。
    /// </summary>
    public static int GetImageFileCount(string? path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }
        int count = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                if (ImageExtensions.Contains(Path.GetExtension(f)))
                {
                    count++;
                }
            }
        }
        catch { }
        return count;
    }


    /// <summary>
    /// 字节 → 人类可读（如 "1.2 GB"）。1024 进制，保留一位小数（KB 以下取整）。
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1)
        {
            size /= 1024;
            u++;
        }
        return u == 0 ? $"{(long)size} {units[u]}" : $"{size:F1} {units[u]}";
    }

}
