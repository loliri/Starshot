using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Vanara.PInvoke;
using Windows.Foundation.Metadata;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace Starshot.Features.Screenshot;

internal partial class ScreenCaptureHelper
{
    public static readonly bool IsTryCreateFromWindowIdPresent = ApiInformation.IsMethodPresent(
        "Windows.Graphics.Capture.GraphicsCaptureItem",
        "TryCreateFromWindowId"
    );

    public static readonly bool IsTryCreateFromDisplayId = ApiInformation.IsMethodPresent(
        "Windows.Graphics.Capture.GraphicsCaptureItem",
        "TryCreateFromDisplayId"
    );

    public static readonly bool IsIncludeSecondaryWindowsPresent = ApiInformation.IsPropertyPresent(
        "Windows.Graphics.Capture.GraphicsCaptureSession",
        "IncludeSecondaryWindows"
    );

    public static readonly bool IsIsBorderRequiredPresent = ApiInformation.IsPropertyPresent(
        "Windows.Graphics.Capture.GraphicsCaptureSession",
        "IsBorderRequired"
    );

    public static readonly bool IsIsCursorCaptureEnabledPresent = ApiInformation.IsPropertyPresent(
        "Windows.Graphics.Capture.GraphicsCaptureSession",
        "IsCursorCaptureEnabled"
    );

    public static readonly bool IsWin10 = Environment.OSVersion.Version.Build < 22000;

    public static async Task<Direct3D11CaptureFrame> CaptureWindowAsync(
        nint hwnd,
        DirectXPixelFormat pixelFormat,
        CanvasDevice? device = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!User32.IsWindow(hwnd))
        {
            throw new ArgumentException(
                "The provided handle is not a valid window handle.",
                nameof(hwnd)
            );
        }
        if (User32.IsIconic(hwnd))
        {
            throw new InvalidOperationException("Cannot capture a minimized window.");
        }
        GraphicsCaptureItem item = CreateGraphicsCaptureItemForWindow(hwnd);
        return await CaptureAsync(item, pixelFormat, device, cancellationToken);
    }

    public static async Task<Direct3D11CaptureFrame> CaptureMonitorAsync(
        nint monitor,
        DirectXPixelFormat pixelFormat,
        CanvasDevice? device = null,
        CancellationToken cancellationToken = default
    )
    {
        GraphicsCaptureItem item = CreateGraphicsCaptureItemForMonitor(monitor);
        return await CaptureAsync(item, pixelFormat, device, cancellationToken);
    }

    public static async Task<Direct3D11CaptureFrame> CaptureAsync(
        GraphicsCaptureItem item,
        DirectXPixelFormat pixelFormat,
        CanvasDevice? device = null,
        CancellationToken cancellationToken = default
    )
    {
        device ??= CanvasDevice.GetSharedDevice();
        // 必须 CreateFreeThreaded：区域截图在 Task.Run 线程池线程发起捕获（并行抓多显示器），
        // 无 DispatcherQueue——Create 版的 FrameArrived 依赖创建线程的 Dispatcher 投递，线程池上永远不来（Win10 实测 10s 超时）。
        // Win10 格式仍写死 B8G8R8A8（WGC 在 Win10 无 HDR float 捕获），Win11 起用调用方请求的格式
        using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            IsWin10 ? DirectXPixelFormat.R8G8B8A8UIntNormalized : pixelFormat,
            1,
            item.Size
        );
        using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
        if (IsIncludeSecondaryWindowsPresent)
        {
            session.IncludeSecondaryWindows = true;
        }
        if (IsIsBorderRequiredPresent)
        {
            session.IsBorderRequired = false;
        }
        if (IsIsCursorCaptureEnabledPresent)
        {
            session.IsCursorCaptureEnabled = false;
        }
        var completionSource = new TaskCompletionSource<Direct3D11CaptureFrame>();
        cancellationToken.Register(() => completionSource.TrySetCanceled());
        // 额外超时保护：即使外部没传 CancellationToken，也不会永久挂起
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        timeoutCts.Token.Register(() =>
            completionSource.TrySetException(
                new TimeoutException("Screen capture timed out after 10 seconds")
            )
        );
        Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> frameArrived = (
            s,
            _
        ) =>
        {
            if (s.TryGetNextFrame() is Direct3D11CaptureFrame frame)
            {
                session.Dispose();
                completionSource.TrySetResult(frame);
            }
        };
        framePool.FrameArrived += frameArrived;
        session.StartCapture();
        try
        {
            return await completionSource.Task.ConfigureAwait(false);
        }
        finally
        {
            // 显式取消订阅：匿名 lambda 每次是新委托实例，-= 减不掉。具名后才能正确解除，
            // 否则 FrameArrived 闭包（持有 session/completionSource）钉住整组截图对象不释放。
            framePool.FrameArrived -= frameArrived;
        }
    }

    public static GraphicsCaptureItem CreateGraphicsCaptureItemForWindow(nint hwnd)
    {
        GraphicsCaptureItem graphicsCaptureItem;
        if (IsTryCreateFromWindowIdPresent)
        {
            graphicsCaptureItem = GraphicsCaptureItem.TryCreateFromWindowId(
                new WindowId((ulong)hwnd)
            );
        }
        else
        {
            Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            nint abi = GraphicsCaptureItem
                .As<IGraphicsCaptureItemInterop>()
                .CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            graphicsCaptureItem = GraphicsCaptureItem.FromAbi(abi);
        }
        return graphicsCaptureItem;
    }

    public static GraphicsCaptureItem CreateGraphicsCaptureItemForMonitor(nint monitor)
    {
        GraphicsCaptureItem graphicsCaptureItem;
        if (IsTryCreateFromDisplayId)
        {
            graphicsCaptureItem = GraphicsCaptureItem.TryCreateFromDisplayId(
                new DisplayId((ulong)monitor)
            );
        }
        else
        {
            Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            nint abi = GraphicsCaptureItem
                .As<IGraphicsCaptureItemInterop>()
                .CreateForMonitor(monitor, GraphicsCaptureItemGuid);
            graphicsCaptureItem = GraphicsCaptureItem.FromAbi(abi);
        }
        return graphicsCaptureItem;
    }

    [ComVisible(true)]
    [GeneratedComInterface]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [System.Runtime.InteropServices.Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    internal partial interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr window, in Guid iid);

        IntPtr CreateForMonitor(IntPtr monitor, in Guid iid);
    }
}
