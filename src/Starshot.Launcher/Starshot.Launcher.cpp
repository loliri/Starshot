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

