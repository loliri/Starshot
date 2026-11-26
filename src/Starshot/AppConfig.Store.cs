using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
#if !DEBUG
using Microsoft.Win32;
#endif

namespace Starshot;

/// <summary>
/// 设置的存储后端。Debug 构建读写 config.sjson（JSON），Release 构建读写注册表 <c>HKCU\Software\Starshot</c>。
/// 内存表示统一为 string / int / bool / List&lt;string&gt;，落盘形态由后端各自决定：
/// 文件里是 JSON 字符串 / 数字 / 布尔 / 原生数组，注册表里是 REG_SZ / DWORD / 单字节 REG_BINARY / REG_MULTI_SZ。
/// </summary>
public static partial class AppConfig
{
#if DEBUG
    #region Debug：配置文件


    /// <summary>
    /// 当前配置文件路径
    /// </summary>
    private static string ConfigFilePath => Path.Combine(UserDataFolder, "config.sjson");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 设置备份文件的扩展名
    /// </summary>
    public static string SettingsBackupExtension => ".sjson";

    /// <summary>
    /// 设置导出文件的扩展名
    /// </summary>
    public static string SettingsExportExtension => ".json";

    private static bool StoreIsInitialized => File.Exists(ConfigFilePath);

    private static void StoreMarkInitialized()
    {
        if (!File.Exists(ConfigFilePath))
        {
            SaveConfigFile();
        }
    }

    // Debug 构建仍用配置文件，不存在新旧两套存储，无需迁移
    private static bool MigrateLegacyConfigFile() => false;

    private static Dictionary<string, object?> StoreLoadAll() =>
        ReadJsonSettings(ConfigFilePath) ?? [];

    /// <summary>
    /// 全量写回 config.sjson。temp + Move 原子替换，防写一半崩溃损坏整档。
    /// </summary>
    private static void SaveConfigFile()
    {
        string tmp = ConfigFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_settingCache, JsonOptions));
        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    private static void StoreWrite(string key, object? value) => SaveConfigFile();

    private static void StoreDeleteAll() => SaveConfigFile();

    private static object? StoreReadBack(string key) =>
        ReadJsonSettings(ConfigFilePath)?.GetValueOrDefault(key);

    private static string StoreExportText() => JsonSerializer.Serialize(_settingCache, JsonOptions);

    private static void StoreWriteFile(string path, string content) =>
        File.WriteAllText(path, content);

    private static Dictionary<string, object?>? StoreReadFile(string path) =>
        ReadJsonSettings(path);

    /// <summary>
    /// 读 JSON 设置字典：值 → string / int / bool / List&lt;string&gt;。文件不存在或不是 JSON 对象返回 null。
    /// </summary>
    private static Dictionary<string, object?>? ReadJsonSettings(string path)
    {
        if (!File.Exists(path))
            return null;
        var values = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                continue;
            object? value = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => (object?)(
                    property.Value.TryGetInt32(out int number) ? number : null
                ),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => property
                    .Value.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .ToList(),
                _ => null,
            };
            if (value is not null)
                values[property.Name] = value;
        }
        return values;
    }

    #endregion
#else
    #region Release：注册表


    /// <summary>
    /// 旧版配置文件（config.sjson）路径：只用于一次性迁移，Release 构建不再读写它
    /// </summary>
    private static string LegacyConfigFilePath => Path.Combine(UserDataFolder, "config.sjson");

    private const string SettingsRegistryPath = @"Software\Starshot";

    /// <summary>
    /// 导出 .reg 时写进文件头节的完整键路径
    /// </summary>
    private const string SettingsRegistryFullPath = @"HKEY_CURRENT_USER\Software\Starshot";

    /// <summary>
    /// 初始化标记的值名。值不存在 = 未初始化（首次启动，或旧版升级后尚未迁移）。
    /// 以后设置的键名或值类型再变，把 <see cref="CurrentConfigVersion"/> 加一，旧版本走同一套迁移入口。
    /// </summary>
    private const string ConfigVersionName = "ConfigVersion";

    private const int CurrentConfigVersion = 1;

    /// <summary>
    /// 设置备份文件的扩展名
    /// </summary>
    public static string SettingsBackupExtension => ".reg";

    /// <summary>
    /// 设置导出文件的扩展名
    /// </summary>
    public static string SettingsExportExtension => ".reg";

    private static RegistryKey? _settingsKey;

    /// <summary>
    /// 可写的设置键句柄（进程内复用，不关闭）。打不开时为 null：此后写入静默失败、读取一律回默认值。
    /// </summary>
    private static RegistryKey? SettingsKey
    {
        get
        {
            if (_settingsKey is null)
            {
                try
                {
                    _settingsKey = Registry.CurrentUser.CreateSubKey(
                        SettingsRegistryPath,
                        writable: true
                    );
                }
                catch { }
            }
            return _settingsKey;
        }
    }

    private static bool StoreIsInitialized
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsRegistryPath);
                return key?.GetValue(ConfigVersionName) is int version
                    && version >= CurrentConfigVersion;
            }
            catch
            {
                return false;
            }
        }
    }

    private static void StoreMarkInitialized()
    {
        try
        {
            SettingsKey?.SetValue(ConfigVersionName, CurrentConfigVersion, RegistryValueKind.DWord);
        }
        catch { }
    }

    /// <summary>
    /// 一次性迁移：旧版 config.sjson → 注册表。返回 true = 已迁移，调用方不必进欢迎页。
    /// 顺序是「先写全部值，再写标记，最后删旧文件」：中途崩溃时标记未写、旧文件还在，下次启动重做一遍（重复写是幂等的）。
    /// 旧文件解析失败按不存在处理（返回 false，文件不动，随后照常进欢迎页；标记落下后不再读它）。
    /// 连同旧形态列表的解析在内，下个版本整段删除。
    /// </summary>
    private static bool MigrateLegacyConfigFile()
    {
        string path = LegacyConfigFilePath;
        if (!File.Exists(path))
            return false;
        Dictionary<string, object?>? values;
        try
        {
            values = ReadLegacyConfigFile(path);
        }
        catch
        {
            values = null;
        }
        if (values is null)
            return false;
        try
        {
            RegistryKey? key = SettingsKey;
            if (key is null)
                return false;
            foreach (var (name, value) in values)
            {
                StoreWrite(name, value);
            }
            key.SetValue(ConfigVersionName, CurrentConfigVersion, RegistryValueKind.DWord);
        }
        catch
        {
            // 迁到一半失败：清掉已写入的值，按全新安装走欢迎页
            try
            {
                StoreDeleteAll();
            }
            catch { }
            return false;
        }
        // 缓存可能在迁移前已初始化：作废，下次访问从注册表重读
        _settingCache = null;
        // 标记已落下，此后不再读旧文件：删除失败（如被占用）也不影响
        try
        {
            File.Delete(path);
        }
        catch { }
        return true;
    }

    /// <summary>
    /// 解析旧版 config.sjson：值 → string / int / bool / List&lt;string&gt;。不是 JSON 对象抛异常（调用方按坏文件处理）。
    /// 三个列表键兼容旧形态（双重序列化的文本 / 分号串），其余键的字符串原样保留。
    /// </summary>
    private static Dictionary<string, object?> ReadLegacyConfigFile(string path)
    {
        var values = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Legacy config file is not a JSON object.");
        }
        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                continue;
            JsonElement element = property.Value;
            object? value = element.ValueKind switch
            {
                JsonValueKind.String => IsListSetting(property.Name)
                    ? (object?)ParseLegacyList(element.GetString())
                    : element.GetString(),
                JsonValueKind.Number => (object?)(
                    element.TryGetInt32(out int number) ? number : null
                ),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => element
                    .EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .ToList(),
                _ => null,
            };
            if (value is not null)
                values[property.Name] = value;
        }
        return values;
    }

    private static bool IsListSetting(string name) =>
        name
            is nameof(LogFolderHistory)
                or nameof(ScreenshotFolderHistory)
                or nameof(ExtraScreenshotFolders);

    /// <summary>
    /// 旧形态的列表值：先是 JSON 数组文本，不是就按分号串拆
    /// </summary>
    private static List<string> ParseLegacyList(string? raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw!);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc
                    .RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .ToList()!;
            }
        }
        catch (JsonException) { }
        return raw?.Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .ToList()
            ?? [];
    }

    private static Dictionary<string, object?> StoreLoadAll()
    {
        var values = new Dictionary<string, object?>();
        RegistryKey? key = SettingsKey;
        if (key is null)
            return values;
        foreach (string name in key.GetValueNames())
        {
            if (IsConfigVersion(name))
                continue;
            object? value = FromRegistryValue(key.GetValue(name));
            if (value is not null)
                values[name] = value;
        }
        return values;
    }

    private static bool IsConfigVersion(string name) =>
        string.Equals(name, ConfigVersionName, StringComparison.OrdinalIgnoreCase);

    private static void StoreWrite(string key, object? value)
    {
        RegistryKey? registry = SettingsKey;
        if (registry is null)
            return;
        switch (value)
        {
            case null:
                registry.DeleteValue(key, throwOnMissingValue: false);
                break;
            case int number:
                registry.SetValue(key, number, RegistryValueKind.DWord);
                break;
            case bool flag:
                registry.SetValue(
                    key,
                    flag ? new byte[] { 1 } : new byte[] { 0 },
                    RegistryValueKind.Binary
                );
                break;
            case List<string> list:
                registry.SetValue(key, ToMultiString(list), RegistryValueKind.MultiString);
                break;
            default:
                registry.SetValue(key, value?.ToString() ?? "", RegistryValueKind.String);
                break;
        }
    }

    /// <summary>
    /// REG_MULTI_SZ 不允许空元素：写入时滤掉 null 与空串
    /// </summary>
    private static string[] ToMultiString(List<string> list) =>
        list.Where(x => !string.IsNullOrEmpty(x)).ToArray();

    /// <summary>
    /// 注册表原生值 → 内存表示：DWORD → int，单字节 REG_BINARY → bool，REG_SZ → string，REG_MULTI_SZ → List&lt;string&gt;。
    /// </summary>
    private static object? FromRegistryValue(object? value)
    {
        switch (value)
        {
            case int:
            case string:
                return value;
            case byte[] bytes:
                return bytes.Length > 0 && bytes[0] != 0;
            case long number when number >= int.MinValue && number <= int.MaxValue:
                return (int)number;
            case string[] items:
                return items.Where(x => !string.IsNullOrEmpty(x)).ToList();
            default:
                return value?.ToString();
        }
    }

    private static void StoreDeleteAll()
    {
        RegistryKey? registry = SettingsKey;
        if (registry is null)
            return;
        foreach (string name in registry.GetValueNames())
        {
            if (!IsConfigVersion(name))
                registry.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private static object? StoreReadBack(string key) =>
        FromRegistryValue(SettingsKey?.GetValue(key));

    private static string StoreExportText()
    {
        var builder = new StringBuilder(4096);
        builder.AppendLine("Windows Registry Editor Version 5.00");
        builder.AppendLine();
        builder.Append('[').Append(SettingsRegistryFullPath).AppendLine("]");
        foreach (
            var (name, value) in _settingCache!.OrderBy(
                item => item.Key,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            AppendRegValue(builder, name, value);
        }
        return builder.ToString();
    }

    /// <summary>
    /// .reg 文件是 UTF-16LE 带 BOM 的文本
    /// </summary>
    private static void StoreWriteFile(string path, string content) =>
        File.WriteAllBytes(
            path,
            [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(content)]
        );

    /// <summary>
    /// 写一行 .reg 值：字符串为 "名"="值"（反斜杠与引号转义），int 为 dword:8 位十六进制，
    /// bool 为 hex:01 / hex:00，列表为 hex(7): 的 UTF-16LE 多字符串字节串。
    /// </summary>
    private static void AppendRegValue(StringBuilder builder, string name, object? value)
    {
        builder.Append('"').Append(EscapeRegString(name)).Append("\"=");
        switch (value)
        {
            case int number:
                builder.Append("dword:").Append(unchecked((uint)number).ToString("x8"));
                break;
            case bool flag:
                builder.Append("hex:").Append(flag ? "01" : "00");
                break;
            case List<string> list:
                // 多字符串以两个 NUL 结尾
                builder
                    .Append("hex(7):")
                    .AppendJoin(
                        ',',
                        Array.ConvertAll<byte, string>(
                            Encoding.Unicode.GetBytes(
                                string.Join("\0", ToMultiString(list)) + "\0\0"
                            ),
                            b => b.ToString("x2")
                        )
                    );
                break;
            default:
                builder.Append('"').Append(EscapeRegString(value?.ToString() ?? "")).Append('"');
                break;
        }
        builder.AppendLine();
    }

    private static string EscapeRegString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// 解析 .reg 文件：只取 <c>HKCU\Software\Starshot</c> 一节下的值，其它节的值一律不碰。
    /// 语法有任何损坏返回 null（整个文件按无效处理）；返回的字典可能为空（合法但没有设置）。
    /// </summary>
    private static Dictionary<string, object?>? StoreReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var values = new Dictionary<string, object?>();
            bool inSection = false;
            foreach (string line in SplitRegLines(ReadTextDetectingEncoding(path)))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == ';')
                    continue;
                if (trimmed[0] == '[')
                {
                    inSection = IsSettingsSection(trimmed);
                    continue;
                }
                if (!inSection)
                    continue;
                if (!TryParseRegValue(trimmed, out string? name, out object? value))
                    return null;
                if (string.IsNullOrEmpty(name) || value is null || IsConfigVersion(name))
                    continue;
                values[name] = value;
            }
            return values;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSettingsSection(string line)
    {
        int end = line.IndexOf(']');
        if (end < 0)
            return false;
        string keyPath = line[1..end].Trim();
        return keyPath.Equals(SettingsRegistryFullPath, StringComparison.OrdinalIgnoreCase)
            || keyPath.Equals(@"HKCU\Software\Starshot", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解析一行 .reg 值。返回 false 表示语法损坏；返回 true 且 name 为 null 表示该行合法但无需导入
    /// （无名默认值、删除标记、其它数据类型）。
    /// </summary>
    private static bool TryParseRegValue(string line, out string? name, out object? value)
    {
        name = null;
        value = null;
        int equals = line.IndexOf('=');
        if (equals < 0)
            return false;
        if (line[0] == '@')
            return true;
        if (line[0] != '"' || !TryParseRegString(line, 0, out string parsedName, out int nameEnd))
            return false;
        string data = line[(equals + 1)..].Trim();
        if (parsedName.Length == 0 || nameEnd > equals || data.Length == 0)
            return false;
        if (data == "-")
            return true;
        name = parsedName;
        if (data[0] == '"')
        {
            if (!TryParseRegString(data, 0, out string text, out int end) || end != data.Length)
                return false;
            value = text;
            return true;
        }
        int colon = data.IndexOf(':');
        if (colon < 0)
            return false;
        byte[]? bytes = ParseRegHex(data[(colon + 1)..]);
        if (bytes is null)
            return false;
        switch (data[..colon].ToLowerInvariant())
        {
            case "dword":
                if (bytes.Length != 4)
                    return false;
                // 文本里的 8 位十六进制是高位在前
                value = unchecked(
                    (int)(
                        (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3]
                    )
                );
                return true;
            case "hex":
                value = bytes.Length == 1 ? (object?)(bytes[0] != 0) : null;
                return true;
            case "hex(7)":
                value = Encoding
                    .Unicode.GetString(bytes)
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                return true;
            case "hex(2)":
                value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return true;
            default:
                name = null;
                return true;
        }
    }

    /// <summary>
    /// 从 start 处的双引号起读一个 .reg 字符串（转义：\\ → \，\" → "）。end 为结束双引号之后的位置。
    /// </summary>
    private static bool TryParseRegString(string text, int start, out string value, out int end)
    {
        var builder = new StringBuilder();
        value = "";
        end = start;
        int i = start + 1;
        while (i < text.Length)
        {
            char c = text[i++];
            if (c == '"')
            {
                value = builder.ToString();
                end = i;
                return true;
            }
            if (c == '\\' && i < text.Length)
            {
                char next = text[i++];
                if (next is '\\' or '"')
                {
                    c = next;
                }
                else
                {
                    builder.Append('\\');
                }
            }
            builder.Append(c);
        }
        return false;
    }

    private static byte[]? ParseRegHex(string hex)
    {
        string s = hex.Replace(" ", "").Replace("\t", "").TrimEnd(',');
        if (s.Length % 2 != 0)
            return null;
        var bytes = new byte[s.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (
                !byte.TryParse(
                    s.AsSpan(i * 2, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out bytes[i]
                )
            )
            {
                return null;
            }
        }
        return bytes;
    }

    /// <summary>
    /// 按行拆开并接上续行（注册表编辑器导出的长 hex 值以行尾单个反斜杠续到下一行）
    /// </summary>
    private static List<string> SplitRegLines(string text)
    {
        var lines = new List<string>();
        string? pending = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (pending is not null)
            {
                line = pending + line;
                pending = null;
            }
            if (line.EndsWith('\\') && !line.EndsWith("\\\\", StringComparison.Ordinal))
                pending = line[..^1];
            else
                lines.Add(line);
        }
        if (pending is not null)
            lines.Add(pending);
        return lines;
    }

    /// <summary>
    /// 读 .reg 文本：按 BOM 识别 UTF-16LE / UTF-16BE / UTF-8，无 BOM 按 ANSI（REGEDIT4 旧格式）
    /// </summary>
    private static string ReadTextDetectingEncoding(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        return Encoding.Default.GetString(bytes);
    }

    #endregion
#endif
}
