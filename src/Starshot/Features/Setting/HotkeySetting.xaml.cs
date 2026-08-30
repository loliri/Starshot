using System;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Starshot.Frameworks;
using Starshot.Helpers;
using Vanara.PInvoke;

namespace Starshot.Features.Setting;

public sealed partial class HotkeySetting : PageBase
{
    private readonly ILogger<HotkeySetting> _logger = AppConfig.GetLogger<HotkeySetting>();

    public nint WindowHandle => XamlRoot.GetWindowHandle();

    public HotkeySetting()
    {
        InitializeComponent();
        InitializeHotkeyInput();
    }

    private void InitializeHotkeyInput()
    {
        try
        {
            HotkeyManager.InitializeHotkeyInput(HotkeyInput_ScreenshotCapture);
            HotkeyManager.InitializeHotkeyInput(HotkeyInput_RegionCapture);
            HotkeyManager.InitializeHotkeyInput(HotkeyInput_RegionCopy);
            HotkeyManager.InitializeHotkeyInput(HotkeyInput_RegionOcr);
        }
        catch { }
    }

    private void HotkeyInput_HotkeyEditing(object sender, HotkeyInputEventArg e)
    {
        try
        {
            HotkeyManager.UnregisterHotkey(e.WindowHandle, e.HotkeyId);
        }
        catch { }
    }

    /// <summary>
    /// 恢复默认快捷键：注销当前 → 按默认值重注册（值变化时 RegisterHotkey 内部写 DB）→ 刷新输入框
    /// </summary>
    private void Button_RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (
                var input in new[]
                {
                    HotkeyInput_ScreenshotCapture,
                    HotkeyInput_RegionCapture,
                    HotkeyInput_RegionCopy,
                    HotkeyInput_RegionOcr,
                }
            )
            {
                if (HotkeyManager.GetHotkeyInfo(input.HotkeyId) is not { } info)
                    continue;
                HotkeyManager.UnregisterHotkey(WindowHandle, input.HotkeyId);
                Win32Error error = HotkeyManager.RegisterHotkey(
                    WindowHandle,
                    input.HotkeyId,
                    info.DefaultModifiers,
                    info.DefaultKey
                );
                input.SetHotkey((uint)info.DefaultModifiers, (uint)info.DefaultKey);
                input.State = error.Succeeded ? HoykeyInputState.None : HoykeyInputState.Warning;
                if (error.Failed)
                {
                    string? hotkey = HotkeyInput.GetHotkeyText(
                        (uint)info.DefaultModifiers,
                        (uint)info.DefaultKey
                    );
                    if (error == Win32Error.ERROR_HOTKEY_ALREADY_REGISTERED)
                    {
                        InAppToast.MainWindow?.Warning(
                            null,
                            string.Format(
                                Lang.HotkeyManager_TheShortcutKeys0IsAlreadyInUse,
                                hotkey
                            ),
                            5000
                        );
                    }
                    else
                    {
                        InAppToast.MainWindow?.Warning(
                            null,
                            string.Format(
                                Lang.HotkeyManager_FailedToRegisterTheShortcutKeys0,
                                hotkey
                            ),
                            5000
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore default hotkeys");
        }
    }

    private void HotkeyInput_HotkeyDeleted(object sender, HotkeyInputEventArg e)
    {
        try
        {
            this.XamlRoot.Content.Focus(FocusState.Programmatic);
            HotkeyManager.DeleteHotkey(e.WindowHandle, e.HotkeyId);
        }
        catch { }
    }

    private void HotkeyInput_HotkeyEditFinished(object sender, HotkeyEditFinishedEventArg e)
    {
        try
        {
            this.XamlRoot.Content.Focus(FocusState.Programmatic);
            if (e.HotkeyAvaliable)
            {
                Win32Error error = HotkeyManager.RegisterHotkey(
                    e.WindowHandle,
                    e.HotkeyId,
                    (User32.HotKeyModifiers)e.fsModifiers,
                    (User32.VK)e.Key
                );
                if (error.Succeeded && e.HotkeyChanged)
                {
                    ((HotkeyInput)sender).State = HoykeyInputState.Success;
                }
                else
                {
                    if (e.HotkeyChanged)
                    {
                        string? hotkey = HotkeyInput.GetHotkeyText(e.fsModifiers, (uint)e.Key);
                        if (error == Win32Error.ERROR_HOTKEY_ALREADY_REGISTERED)
                        {
                            InAppToast.MainWindow?.Warning(
                                null,
                                string.Format(
                                    Lang.HotkeyManager_TheShortcutKeys0IsAlreadyInUse,
                                    hotkey
                                ),
                                5000
                            );
                        }
                        else
                        {
                            InAppToast.MainWindow?.Warning(
                                null,
                                string.Format(
                                    Lang.HotkeyManager_FailedToRegisterTheShortcutKeys0,
                                    hotkey
                                ),
                                5000
                            );
                        }
                        ((HotkeyInput)sender).State = HoykeyInputState.Warning;
                    }
                    else
                    {
                        var info = HotkeyManager.GetHotkeyInfo(e.HotkeyId);
                        if (info?.Error.Failed ?? false)
                        {
                            ((HotkeyInput)sender).State = HoykeyInputState.Warning;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotkey edit finished");
            InAppToast.MainWindow?.Error(ex, Lang.HotkeySetting_FailedToRegisterTheShortcutKeys);
        }
    }
}
