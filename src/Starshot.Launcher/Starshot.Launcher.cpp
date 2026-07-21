#include <cctype>
#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>
#include <Windows.h>
#include <shellapi.h>

#pragma comment(lib, "shell32.lib")
#pragma comment(linker, "/subsystem:windows /entry:wmainCRTStartup")

int wmain(int argc, wchar_t* argv[])
{
    const std::filesystem::path base_folder = std::filesystem::path(argv[0]).parent_path();

    // --clean wipes old app-*. Without a pid it just retries 10 times and gives up (no process kill).
    // With --clean=<pid> it retries harder (once per minute for 5 min) then force-kills that pid.
    bool cleanup = false;
    DWORD oldPid = 0;
    const std::wstring cleanPrefix = L"--clean=";
    for (int i = 1; i < argc; ++i)
    {
        std::wstring arg(argv[i]);
        if (arg == L"--clean") { cleanup = true; }
        else if (arg.rfind(cleanPrefix, 0) == 0) { cleanup = true; oldPid = (DWORD)_wtol(arg.c_str() + cleanPrefix.size()); }
    }

    // version.ini decides app dir: CI/CD release has it -> app-{version}; missing -> app (debug/local)
    std::filesystem::path target_exe = base_folder / "app" / "Starshot.exe";
    const std::filesystem::path version_ini = base_folder / "version.ini";
    if (std::filesystem::exists(version_ini))
    {
        // version.ini is UTF-8, single line "version=xxx" (workflow Set-Content -Encoding UTF8)
        std::ifstream fin(version_ini);
        std::string line;
        if (std::getline(fin, line))
        {
            if (line.size() >= 3 && line[0] == '\xEF' && line[1] == '\xBB' && line[2] == '\xBF')
                line = line.substr(3);  // skip UTF-8 BOM
            auto eq = line.find('=');
            if (eq != std::string::npos)
            {
                std::string v = line.substr(eq + 1);
                if (!v.empty() && v.back() == '\r') v.pop_back();
                // version.ini keeps original case (shown in About); app dir is lowercase (release tag is lowercase)
                for (char& c : v) c = (char)tolower((unsigned char)c);
                if (!v.empty())
                    target_exe = base_folder / (L"app-" + std::wstring(v.begin(), v.end())) / L"Starshot.exe";
            }
        }
    }

    if (std::filesystem::exists(target_exe))
    {
        STARTUPINFOW si{};
        si.cb = sizeof(si);
        PROCESS_INFORMATION pi{};
        // Rebuild from argv[1..] so quoting does not depend on GetCommandLine format
        std::wstring cmdline = L"\"" + target_exe.wstring() + L"\"";
        for (int i = 1; i < argc; ++i)
        {
            cmdline += L" \"";
            cmdline += argv[i];
            cmdline += L"\"";
        }
        CreateProcess(target_exe.c_str(), (LPWSTR)cmdline.c_str(), NULL, NULL, false, 0, NULL, NULL, &si, &pi);
        CloseHandle(pi.hProcess);
        CloseHandle(pi.hThread);

        if (cleanup)
        {
            // Clean up old app-* directories (keep only the one we just launched).
            std::filesystem::path current_app_dir = target_exe.parent_path();
            std::error_code iter_ec;
            for (const auto& entry : std::filesystem::directory_iterator(base_folder, iter_ec))
            {
                if (!entry.is_directory()) continue;
                const std::wstring name = entry.path().filename().wstring();
                if (name.rfind(L"app-", 0) != 0) continue;
                if (entry.path() == current_app_dir) continue;
                // Phase 1: 10 quick attempts (200ms apart) — always done
                std::error_code rm_ec;
                for (int i = 0; i < 10; ++i) {
                    std::filesystem::remove_all(entry.path(), rm_ec);
                    if (!std::filesystem::exists(entry.path())) break;
                    Sleep(200);
                }
                if (!std::filesystem::exists(entry.path())) continue;
                // No pid -> give up here (simple mode). With pid -> long retry then force-kill.
                if (oldPid == 0) continue;
                // Phase 2: retry once per minute for up to 5 min
                auto deadline = std::chrono::steady_clock::now() + std::chrono::minutes(5);
                while (std::chrono::steady_clock::now() < deadline) {
                    Sleep(60000);
                    std::filesystem::remove_all(entry.path(), rm_ec);
                    if (!std::filesystem::exists(entry.path())) break;
                }
                if (!std::filesystem::exists(entry.path())) continue;
                // Phase 3: force-kill the old main process (still holding the dir locked) and try once more
                HANDLE h = OpenProcess(PROCESS_TERMINATE, FALSE, oldPid);
                if (h) { TerminateProcess(h, 1); CloseHandle(h); }
                std::filesystem::remove_all(entry.path(), rm_ec);
            }
        }
    }
    else
    {
        SetProcessDPIAware();
        int result = MessageBox(NULL,
            L"It seems Starshot is not installed.\n"
            L"Would you like to open the project page to install it?\n\n"
            L"https://github.com/loliri/Starshot",
            L"Starshot",
            MB_ICONQUESTION | MB_YESNO | MB_SETFOREGROUND);
        if (result == IDYES)
        {
            ShellExecute(NULL, L"open", L"https://github.com/loliri/Starshot", NULL, NULL, SW_SHOWNORMAL);
        }
    }
}

