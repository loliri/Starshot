namespace Starshot.Features.Background;

/// <summary>
/// 壁纸变更消息。发送方（设置页）改了 EnableWallpaper/WallpaperFile 后 Send，
/// AppBackground 收到后从 AppConfig 重新加载。
/// </summary>
public sealed class BackgroundChangedMessage
{
}
