const SUBKEY: &str = "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize";
const VALUE: &str = "AppsUseLightTheme";

pub fn is_dark_mode() -> windows_registry::Result<bool> {
    let hkcu = windows_registry::CURRENT_USER;
    let subkey = hkcu.options().read().open(SUBKEY)?;
    let dword: u32 = subkey.get_u32(VALUE)?;
    Ok(dword == 0)
}

/// Whether the system UI language is Chinese — used to pick the language of
/// native dialogs shown before the webview (and its i18n) is available.
pub fn is_chinese_ui() -> bool {
    use windows::Win32::Globalization::GetUserDefaultUILanguage;
    let lang = unsafe { GetUserDefaultUILanguage() };
    (lang & 0x3ff) == 0x04 // PRIMARYLANGID == LANG_CHINESE
}

/// Pick a string by system UI language for pre-webview native dialogs.
pub fn tr<'a>(zh: &'a str, en: &'a str) -> &'a str {
    if is_chinese_ui() {
        zh
    } else {
        en
    }
}
