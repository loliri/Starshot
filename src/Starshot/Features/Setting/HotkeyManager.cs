using Starshot.Helpers;
using System;
using System.Diagnostics;
using Vanara.PInvoke;

namespace Starshot.Features.Setting;

internal static class HotkeyManager
{


    public static HotkeyInfo ScreenshotCapture { get; private set; } = new HotkeyInfo(nameof(AppConfig.ScreenshotCaptureHotkey), 44445, User32.HotKeyModifiers.MOD_ALT, User32.VK.VK_W);


    public static HotkeyInfo RegionCapture { get; private set; } = new HotkeyInfo(nameof(AppConfig.RegionCaptureHotkey), 44446, User32.HotKeyModifiers.MOD_ALT, User32.VK.VK_Q);


    public static HotkeyInfo RegionCopyOnly { get; private set; } = new HotkeyInfo(nameof(AppConfig.RegionCopyHotkey), 44447, User32.HotKeyModifiers.MOD_ALT, User32.VK.VK_A);




    public static void InitializeHotkey(nint hwnd)
    {
        try
        {
            foreach (var item in new HotkeyInfo[] { ScreenshotCapture, RegionCapture, RegionCopyOnly })
            {
                User32.HotKeyModifiers modifiers = User32.HotKeyModifiers.MOD_NONE;
                User32.VK key = 0;
                string? hotkey = AppConfig.GetValue<string>(null, item.ConfigSetting);
                var mk = GetModifiersKey(hotkey);
                if (mk.HasValue)
                {
                    if (HotkeyInput.IsHotkeyAvaliable((uint)mk.Value.Modifiers, (uint)mk.Value.Key))
                    {
                        modifiers = mk.Value.Modifiers;
                        key = mk.Value.Key;
                    }
                }
                else
                {
                    modifiers = item.DefaultModifiers;
                    key = item.DefaultKey;
                }
                item.Modifiers = modifiers;
                item.Key = key;
                Win32Error error = RegisterHotkey(hwnd, item.Id, modifiers, key);
                if (error.Failed && InAppToast.MainWindow is not null)
                {
                    item.ErrorShown = true;
                    hotkey = HotkeyInput.GetHotkeyText((uint)modifiers, (uint)key);
                    if (error == Win32Error.ERROR_HOTKEY_ALREADY_REGISTERED)
                    {
                        InAppToast.MainWindow.Warning(null, string.Format(Lang.HotkeyManager_TheShortcutKeys0IsAlreadyInUsePleaseModifyItInSettingsPage, hotkey), 0);
                    }
                    else
                    {
                        InAppToast.MainWindow.Warning(null, string.Format(Lang.HotkeyManager_FailedToRegisterTheShortcutKeys0PleaseRetryInSettingsPage, hotkey), 0);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.Write(ex);
        }
    }



    /// <summary>
    /// 补弹注册失败提示：--hide 启动时 MainWindow 未建、toast 无宿主；MainWindow 打开后调此补弹未显示的错误。
    /// </summary>
    public static void ShowRegistrationErrors()
    {
        foreach (var item in new HotkeyInfo[] { ScreenshotCapture, RegionCapture, RegionCopyOnly })
        {
            if (item.ErrorShown || item.Error.Succeeded) continue;
            item.ErrorShown = true;
            string hotkey = HotkeyInput.GetHotkeyText((uint)item.Modifiers, (uint)item.Key);
            if (item.Error == Win32Error.ERROR_HOTKEY_ALREADY_REGISTERED)
                InAppToast.MainWindow?.Warning(null, string.Format(Lang.HotkeyManager_TheShortcutKeys0IsAlreadyInUsePleaseModifyItInSettingsPage, hotkey), 0);
            else
                InAppToast.MainWindow?.Warning(null, string.Format(Lang.HotkeyManager_FailedToRegisterTheShortcutKeys0PleaseRetryInSettingsPage, hotkey), 0);
        }
    }


    public static Win32Error RegisterHotkey(nint hwnd, int id, User32.HotKeyModifiers modifiers, User32.VK key)
    {
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            if (info.IsRegistered)
            {
                return Win32Error.ERROR_SUCCESS;
            }

            if (modifiers == 0 && key == 0)
            {
                return Win32Error.ERROR_SUCCESS;
            }
            User32.RegisterHotKey(hwnd, id, modifiers | User32.HotKeyModifiers.MOD_NOREPEAT, (uint)key);
            Win32Error error = Kernel32.GetLastError();
            if (error.Succeeded && (info.Modifiers != modifiers || info.Key != key))
            {
                AppConfig.SetValue($"{(uint)modifiers}+{(uint)key}", info.ConfigSetting);
            }
            info.Modifiers = modifiers;
            info.Key = key;
            info.IsRegistered = error.Succeeded;
            info.Error = error;
            return error;
        }
        else
        {
            return Win32Error.ERROR_BAD_ARGUMENTS;
        }
    }


    public static Win32Error UnregisterHotkey(nint hwnd, int id)
    {
        User32.UnregisterHotKey(hwnd, id);
        Win32Error error = Kernel32.GetLastError();
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            info.IsRegistered = false;
            info.Error = Win32Error.ERROR_SUCCESS;
        }
        return error;
    }


    public static Win32Error DeleteHotkey(nint hwnd, int id)
    {
        User32.UnregisterHotKey(hwnd, id);
        Win32Error error = Kernel32.GetLastError();
        if (GetHotkeyInfo(id) is HotkeyInfo info)
        {
            info.Modifiers = 0;
            info.Key = 0;
            info.IsRegistered = false;
            info.Error = Win32Error.ERROR_SUCCESS;
            AppConfig.SetValue("0", info.ConfigSetting);
        }
        return error;
    }


    public static void InitializeHotkeyInput(HotkeyInput hotkeyInput)
    {
        if (GetHotkeyInfo(hotkeyInput.HotkeyId) is HotkeyInfo info)
        {
            hotkeyInput.SetHotkey((uint)info.Modifiers, (uint)info.Key);
            hotkeyInput.State = info.Error.Succeeded ? HoykeyInputState.None : HoykeyInputState.Warning;
        }
    }


    private static (User32.HotKeyModifiers Modifiers, User32.VK Key)? GetModifiersKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        if (value.Trim() is "0")
        {
            return (0, 0);
        }
        string[] splits = value.Split('+');
        if (splits.Length == 2)
        {
            if (uint.TryParse(splits[0].Trim(), out uint modifiers) && uint.TryParse(splits[1].Trim(), out uint key))
            {
                return ((User32.HotKeyModifiers)modifiers, (User32.VK)key);
            }
        }
        return null;
    }




    public static HotkeyInfo? GetHotkeyInfo(int id)
    {
        return id switch
        {
            44445 => ScreenshotCapture,
            44446 => RegionCapture,
            44447 => RegionCopyOnly,
            _ => null,
        };
    }




    public class HotkeyInfo
    {

        public string ConfigSetting { get; init; }

        public int Id { get; init; }

        public User32.HotKeyModifiers Modifiers { get; set; }

        public User32.VK Key { get; set; }

        public bool IsRegistered { get; set; }

        public bool ErrorShown { get; set; }

        public Win32Error Error { get; set; }

        public User32.HotKeyModifiers DefaultModifiers { get; init; }

        public User32.VK DefaultKey { get; init; }


        public HotkeyInfo(string configSetting, int id, User32.HotKeyModifiers defaultModifiers, User32.VK defaultKey)
        {
            ConfigSetting = configSetting;
            Id = id;
            DefaultModifiers = defaultModifiers;
            DefaultKey = defaultKey;
        }


    }





}
