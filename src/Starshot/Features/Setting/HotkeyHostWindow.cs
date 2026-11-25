using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;

namespace Starshot.Features.Setting;

/// <summary>
/// 全局热键由进程常驻的 message-only 窗口（HWND_MESSAGE）接收，
/// 并在 UI 线程分发截图命令。
/// </summary>
internal sealed class HotkeyHostWindow
{
    private const string ClassName = "Starshot.HotkeyHost.MessageWindow";

    private static readonly HotkeyHostWindow Instance = new();

    private readonly User32.WindowProc _wndProc;
    private readonly HWND _hwnd;

    // 防止委托被 GC 回收（RegisterClass 只存函数指针，不持有托管引用）
    private readonly User32.WindowProc _wndProcKeepAlive;

    private HotkeyHostWindow()
    {
        _wndProc = WndProc;
        _wndProcKeepAlive = _wndProc;

        HINSTANCE hInstance = new(Kernel32.GetModuleHandle(null).DangerousGetHandle());
        var wndClass = new User32.WNDCLASS
        {
            lpfnWndProc = _wndProc,
            hInstance = hInstance,
            lpszClassName = ClassName,
        };
        Kernel32.ATOM atom = User32.RegisterClass(in wndClass);
        if (atom.IsInvalid)
        {
            AppConfig
                .GetLogger<HotkeyHostWindow>()
                .LogWarning("RegisterClass failed, err={Error}", Kernel32.GetLastError());
        }

        // HWND_MESSAGE：message-only 窗口，不出现在任何 Z 序/任务栏，只接收消息
        _hwnd = User32.CreateWindowEx(
            (User32.WindowStylesEx)0,
            ClassName,
            "Starshot Hotkey Host",
            (User32.WindowStyles)0,
            0,
            0,
            0,
            0,
            (HWND)(nint)(-3), // HWND_MESSAGE = (HWND)-3
            HMENU.NULL,
            hInstance,
            IntPtr.Zero
        );
        if (_hwnd.IsNull)
        {
            AppConfig
                .GetLogger<HotkeyHostWindow>()
                .LogWarning("CreateWindowEx (HWND_MESSAGE) failed, err={Error}", Kernel32.GetLastError());
        }
        else
        {
            AppConfig
                .GetLogger<HotkeyHostWindow>()
                .LogInformation("Hotkey host window created, hwnd={Hwnd}", _hwnd.DangerousGetHandle());
        }
    }

    /// <summary>
    /// 供 HotkeyManager 注册热键用。必须在 UI 线程首次调用（窗口创建在 UI 线程）。
    /// </summary>
    public static nint Handle => Instance._hwnd.DangerousGetHandle();

    private nint WndProc(HWND hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == (uint)User32.WindowMessage.WM_HOTKEY)
        {
            AppConfig
                .GetLogger<HotkeyHostWindow>()
                .LogInformation("WM_HOTKEY received, id={HotkeyId}, hwnd={Hwnd}", (int)wParam, hWnd);

            HotkeyManager.HotkeyInfo? info = HotkeyManager.GetHotkeyInfo((int)wParam);
            if (info is not null)
            {
                HotkeyManager.DispatchHotkey(info.Id);
            }
            return IntPtr.Zero;
        }
        return User32.DefWindowProc(hWnd, uMsg, wParam, lParam);
    }
}
