using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Display;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.Win32;

namespace Starshot.Features.Telemetry;

/// <summary>
/// 匿名心跳：每天一次，上报安装 ID、版本、分发线、架构、系统版本、HDR、语言与更新结果。
/// 与全局快捷键、托盘同一条常驻生命周期（不依赖主窗口）；全程静默，失败只写本地日志。
/// </summary>
public static class TelemetryService
{
    private const string Endpoint = AppConfig.TelemetryBase + "/v1/ping";

    private const int InitialDelayMinSeconds = 30;

    private const int InitialDelayMaxSeconds = 60;

    /// <summary>
    /// 进程内轮询间隔（与更新检查同节奏）
    /// </summary>
    private const int IntervalHours = 5;

    /// <summary>
    /// 发送失败后的重试间隔：5 小时对一次临时网络故障来说太长
    /// </summary>
    private const int RetryDelayMinutes = 10;

    private static readonly HttpClient _http = CreateClient();

    // 走 AppConfig.GetLogger：与全应用同一个 Serilog 实例（自行 LoggerFactory.Create 会在启动早期
    // 绑到 SilentLogger 上，之后全局 Logger 被替换也不会更新，日志永远不落盘）
    private static readonly ILogger _logger = AppConfig.GetLogger("TelemetryService");

#if !DEBUG
    private static Timer? _timer;
#endif

    private static int _busy;

    /// <summary>
    /// UI 线程队列（Start 里赋值；Debug 构建不启动调度，保持 null 时 RunOnUiThread 直接在当前线程执行）
    /// </summary>
    private static DispatcherQueue? _dispatcher;

    /// <summary>
    /// 启动时检测到的版本迁移（from → to），随本次运行的生命周期有效：心跳成功后清空。
    /// 不落盘——版本迁移只可能发生在启动那一刻，进程内记着就够了。
    /// </summary>
    private static (string from, string to)? _pendingUpdate;

    /// <summary>
    /// 启动心跳调度。只在主实例、配置初始化完成后调用一次（与 SystemTrayWindow 同一条生命周期）。
    /// </summary>
    public static void Start(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
#if DEBUG
        // Debug 构建不发任何请求：直接不挂计时器
        return;
#else
        if (_timer is not null)
            return;
        // 本地构建（0.0.0-local）不发任何请求：直接不挂计时器
        if (AppConfig.AppVersion.EndsWith("-local", StringComparison.OrdinalIgnoreCase))
            return;
        // 启动时已由 BuildServiceProvider 比对过 LastAppVersion，这里取走同一次判定结果
        if (AppConfig.TakePendingVersionFrom() is { } from)
            _pendingUpdate = (from, AppConfig.AppVersion);
        _timer = new Timer(
            _ => _ = TickAsync(),
            null,
            Random.Shared.Next(InitialDelayMinSeconds, InitialDelayMaxSeconds + 1) * 1000,
            IntervalHours * 3600 * 1000
        );
#endif
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { UseProxy = true };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"Starshot/{AppConfig.AppVersion}");
        return c;
    }

    /// <summary>
    /// 调试页展示用的状态快照。
    /// </summary>
    public static (
        string installId,
        string lastSentDay,
        string pendingUpdate,
        bool isVm
    ) GetDebugInfo() =>
        (
            AppConfig.TelemetryInstallId ?? "",
            AppConfig.TelemetryLastSentDay ?? "",
            _pendingUpdate is { } upd ? $"{upd.from} → {upd.to}" : "",
            IsVirtualMachine()
        );

    /// <summary>
    /// 当天是否还需要发送：换日，或本次启动检测到了版本迁移。配置读取在 UI 线程执行。
    /// </summary>
    private static bool NeedsSend =>
        _pendingUpdate is not null
        || RunOnUiThread(() =>
            AppConfig.TelemetryLastSentDay
            != DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        );

    private static async Task TickAsync()
    {
        if (!NeedsSend)
        {
            // 当天已发过（或无需发送）：保持 5 小时节奏
            ApplyInterval(success: true);
            return;
        }
        bool success = await SendAsync();
        ApplyInterval(success);
    }

    /// <summary>
    /// 失败 → 10 分钟后重试；成功 → 恢复 5 小时节奏
    /// </summary>
    private static void ApplyInterval(bool success)
    {
#if !DEBUG
        _timer?.Change(
            success ? TimeSpan.FromHours(IntervalHours) : TimeSpan.FromMinutes(RetryDelayMinutes),
            TimeSpan.FromHours(IntervalHours)
        );
#endif
    }

    private static async Task<bool> SendAsync()
    {
        // 上一次发送尚未结束就再次进入：不重入，按成功处理以免把节奏切到重试
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return true;
        try
        {
            string? id = EnsureInstallId();
            if (id is null)
            {
                // 安装 ID 落盘失败（如便携版放在只读目录）：不发，避免每次启动都被记成新安装
                _logger.LogWarning("Telemetry skipped: install id could not be persisted");
                return false;
            }

            var (code, respBody) = await PostAsync(id).ConfigureAwait(false);

            // 服务端拒收安装 ID（配置被手改或损坏）：清掉重生成，立即补发一次。
            // 只补发一次——重生成的 ID 仍是 Guid，再被拒说明不是 ID 的问题，避免死循环。
            if (code == 400 && respBody.Contains("bad field: id", StringComparison.Ordinal))
            {
                _logger.LogWarning("Telemetry install id rejected by server, regenerating");
                RunOnUiThread(() => AppConfig.TelemetryInstallId = null);
                id = EnsureInstallId();
                if (id is not null)
                    (code, respBody) = await PostAsync(id).ConfigureAwait(false);
            }

            if (code == 204 || code == 400 || code == 413)
            {
                // 400/413 是请求本身的问题，重试也永远失败：当作已发送，避免每天空转
                if (code != 204)
                    _logger.LogWarning("Telemetry rejected by server: HTTP {Code}", code);
                RunOnUiThread(() =>
                {
                    AppConfig.TelemetryLastSentDay = DateTime.UtcNow.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture
                    );
                });
                _pendingUpdate = null;
                return code == 204;
            }

            // 5xx / 网络错误：不改状态，10 分钟后再试
            _logger.LogWarning("Telemetry send failed: HTTP {Code}", code);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry send failed");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>
    /// 发一次心跳，返回状态码与响应体（响应体用于识别服务端拒绝的具体字段）。
    /// </summary>
    private static async Task<(int code, string body)> PostAsync(string id)
    {
        string body = BuildPayload(id);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(Endpoint, content).ConfigureAwait(false);
        string respBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return ((int)resp.StatusCode, respBody);
    }

    /// <summary>
    /// 安装 ID 格式：32 位小写十六进制，与服务端 validate() 的 RE_ID 一致。
    /// </summary>
    private static bool IsValidInstallId(string? id)
    {
        if (id is null || id.Length != 32)
            return false;
        foreach (char c in id)
        {
            if (c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// 取安装 ID；不存在或格式不合法时重新生成并落盘。落盘失败返回 null。
    /// </summary>
    private static string? EnsureInstallId()
    {
        string? id = RunOnUiThread(() => AppConfig.TelemetryInstallId);
        // 配置被手改或损坏时本地就能发现，不用等一次网络往返
        if (IsValidInstallId(id))
            return id;
        string generated = Guid.NewGuid().ToString("N");
        // SetValue 先改内存缓存再写盘，写盘异常被吞掉，回读属性看不出失败；回读文件才能确认真的落盘
        bool saved = RunOnUiThread(() =>
        {
            AppConfig.TelemetryInstallId = generated;
            try
            {
                return File.Exists(AppConfig.ConfigFilePath)
                    && File.ReadAllText(AppConfig.ConfigFilePath)
                        .Contains(generated, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        });
        if (!saved)
        {
            // 撤销内存缓存里的值，下次心跳再试，避免拿一个没落盘的 ID 上报成新安装
            RunOnUiThread(() => AppConfig.TelemetryInstallId = null);
            return null;
        }
        return generated;
    }

    private static string BuildPayload(string id)
    {
        // 配置读取与显示器查询都在 UI 线程取一次快照（WinRT 外呼不上 UI 线程），序列化留在后台线程
        var (app, dist, src, hdr, monitors, langSetting) = RunOnUiThread(() =>
            (
                AppConfig.AppVersion,
                AppConfig.Installer ? "installer" : "portable",
                AppConfig.Installer ? "installer"
                : AppConfig.UpdateSource == 0 ? "cdn"
                : "github",
                AnyHdrDisplay(),
                CountDisplays(),
                AppConfig.Language
            )
        );
        // 界面语言只有三套资源（en / zh-CN / ja-JP）：配置为空表示跟随系统，此时按运行时生效的
        // UI 文化判定（与 SetLanguage 同源），其余区域实际看到的是回退的英文，统一归到 en
        string effective = langSetting ?? CultureInfo.CurrentUICulture.Name;
        string lang = effective switch
        {
            "zh-CN" => "zh-CN",
            "ja-JP" => "ja-JP",
            _ => "en",
        };
        string locale = CultureInfo.CurrentCulture.Name;

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("id", id);
            w.WriteString("app", app);
            w.WriteString("dist", dist);
            w.WriteString("arch", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
            var os = Environment.OSVersion.Version;
            w.WriteString("os", $"{os.Major}.{os.Minor}.{os.Build}");
            w.WriteBoolean("hdr", hdr);
            w.WriteNumber("monitors", monitors);
            w.WriteBoolean("vm", IsVirtualMachine());
            w.WriteString("lang", lang);
            w.WriteString("locale", string.IsNullOrEmpty(locale) ? "und" : locale);
            w.WriteString("src", src);
            if (_pendingUpdate is { } upd)
            {
                w.WriteStartObject("upd");
                w.WriteString("from", TrimTagPrefix(upd.from));
                w.WriteString("to", TrimTagPrefix(upd.to));
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 版本号去掉开头的 v/V（Release tag 保留原始前缀，接口约定不带）。
    /// </summary>
    private static string TrimTagPrefix(string version) =>
        version.Length > 0 && (version[0] == 'v' || version[0] == 'V') ? version[1..] : version;

    /// <summary>
    /// 是否存在已开启 HDR 的显示器。索引访问而非 foreach：FindAll 的投影列表枚举会 QI 失败。
    /// 查询失败按「无 HDR」处理——这只是附属字段，不该拖垮整条心跳。
    /// </summary>
    private static bool AnyHdrDisplay()
    {
        try
        {
            var displays = DisplayArea.FindAll();
            for (int i = 0; i < displays.Count; i++)
            {
                using var di = DisplayInformation.CreateForDisplayId(displays[i].DisplayId);
                if (
                    di.GetAdvancedColorInfo().CurrentAdvancedColorKind
                    is DisplayAdvancedColorKind.HighDynamicRange
                )
                    return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// 显示器数量。服务端要求 ≥1，而 FindAll 返回空列表时 Count 是 0（不抛异常），
    /// 会收到 400 并被当成已发送，当天数据丢失；因此无论查询失败还是空列表都兜底为 1。
    /// </summary>
    private static int CountDisplays()
    {
        try
        {
            return Math.Max(1, DisplayArea.FindAll().Count);
        }
        catch
        {
            return 1;
        }
    }

    /// <summary>
    /// 是否运行在虚拟机中。查询失败按物理机处理，不该拖垮心跳。
    /// <para>
    /// x64 走 CPUID，判据与任务管理器一致（任务管理器 RVA 0xc11f0 反汇编还原）：
    /// <list type="number">
    /// <item>leaf 1 的 ECX bit31 是 hypervisor 位，为 0 即不是虚拟机。</item>
    /// <item>leaf 0x40000000 取 hypervisor 厂商串，**只跟 "Microsoft Hv" 比较一次**；
    /// 其余厂商（VMware / VirtualBox / KVM / QEMU / Xen / Parallels …）不做任何区分，
    /// 一律直接判定为虚拟机——任务管理器本身就是这么做的，二进制里根本没有那些厂商名。</item>
    /// <item>只有厂商是 Microsoft Hv 时才多查一步：leaf 0x40000003 的 **EBX bit0**
    /// 区分 root partition（开了 Hyper-V 的物理机，宿主本身）与 child partition（真正的客户机）。</item>
    /// </list>
    /// 这一步是 Hyper-V 专属后门：开了 Hyper-V / WSL2 / Windows 沙盒的物理机同样带 hypervisor 位，
    /// 不排除就会被误判成虚拟机；而 VBS 等基于 Hyper-V 的安全功能也走这条分支，同样能被正确排除。
    /// </para>
    /// <para>
    /// ARM64 没有 CPUID，退回 SMBIOS / 磁盘枚举的字符串匹配。
    /// </para>
    /// </summary>
    private static bool IsVirtualMachine()
    {
        try
        {
            if (X86Base.IsSupported)
                return IsVirtualMachineByCpuid();
            return IsVirtualMachineBySmbios();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// CPUID 判据，逐位还原任务管理器的实现。只在 x64 下调用。
    /// </summary>
    private static bool IsVirtualMachineByCpuid()
    {
        // leaf 1, ECX bit31 = hypervisor present
        if ((X86Base.CpuId(1, 0).Ecx & (1u << 31)) == 0)
            return false;

        // leaf 0x40000000：EBX/ECX/EDX 依次是厂商 ID 的前 4/中 4/后 4 字节
        var vendorLeaf = X86Base.CpuId(0x40000000, 0);
        Span<byte> bytes = stackalloc byte[12];
        BitConverter.TryWriteBytes(bytes[..4], vendorLeaf.Ebx);
        BitConverter.TryWriteBytes(bytes[4..8], vendorLeaf.Ecx);
        BitConverter.TryWriteBytes(bytes[8..], vendorLeaf.Edx);
        string vendor = Encoding.ASCII.GetString(bytes).TrimEnd('\0');

        // 非 Microsoft 的 hypervisor 一律算虚拟机，不再细分厂商
        if (vendor != "Microsoft Hv")
            return true;

        // Hyper-V：EBX bit0 = 1 表示当前是 root partition（宿主物理机），0 才是真正的客户机
        return (X86Base.CpuId(0x40000003, 0).Ebx & 1u) == 0;
    }

    /// <summary>
    /// SMBIOS / 磁盘枚举的字符串匹配，ARM64 没有 CPUID 时用。
    /// </summary>
    private static bool IsVirtualMachineBySmbios()
    {
        using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        using var disk = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\Disk\Enum"
        );
        string all = string.Join(
            '|',
            bios?.GetValue("SystemManufacturer") as string,
            bios?.GetValue("SystemProductName") as string,
            bios?.GetValue("SystemFamily") as string,
            bios?.GetValue("BIOSVendor") as string,
            bios?.GetValue("BIOSVersion") as string,
            disk?.GetValue("0") as string
        );
        foreach (string marker in VmMarkers)
        {
            if (all.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// SMBIOS / 磁盘枚举里的虚拟机标识。都是厂商写入的固定串，不会出现在物理机上。
    /// 仅 ARM64 的兜底路径使用。
    /// </summary>
    private static readonly string[] VmMarkers =
    [
        "VMware",
        "VirtualBox",
        "innotek",
        "QEMU",
        "Xen",
        "Parallels",
        "Virtual Machine",
        "Virtual Disk",
        "Hyper-V",
        "Bochs",
        "Bhyve",
        "Amazon EC2",
        "Google Compute Engine",
    ];

    /// <summary>
    /// 配置读写一律回到 UI 线程执行，与现有设置写入同一条线程，不引入并发写 config.sjson。
    /// 已在 UI 线程时直接执行；UI 线程不可用（退出中）时返回默认值。
    /// </summary>
    private static T RunOnUiThread<T>(Func<T> func)
    {
        if (_dispatcher is null || _dispatcher.HasThreadAccess)
            return func();
        T result = default!;
        using var done = new ManualResetEventSlim(false);
        if (
            !_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    result = func();
                }
                catch { }
                finally
                {
                    done.Set();
                }
            })
        )
            return result;
        done.Wait(TimeSpan.FromSeconds(5));
        return result;
    }

    private static void RunOnUiThread(Action action) =>
        RunOnUiThread(() =>
        {
            action();
            return true;
        });
}
