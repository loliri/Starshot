namespace Starshot.Features.Background;

/// <summary>
/// 主窗口可见性变化：Hide=隐藏（托盘等），Activate=被激活（含托盘点开/点击聚焦）。
/// AppBackground 据此暂停/续播视频壁纸，避免不可见时占 GPU。移植自 Starward。
/// </summary>
public sealed class MainWindowStateChangedMessage
{
    public bool Activate { get; init; }

    public bool Hide { get; init; }
}
