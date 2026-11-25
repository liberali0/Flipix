#include "win/module.h"

#include <knownfolders.h>
#include <shlobj.h>

#include <vector>

namespace qic::win {
namespace {

HMODULE g_module = nullptr;
volatile LONG g_objects = 0;
volatile LONG g_locks = 0;

}  // namespace

void SetModule(HMODULE module) { g_module = module; }
HMODULE Module() { return g_module; }

void AddObjectRef() { InterlockedIncrement(&g_objects); }
void ReleaseObjectRef() { InterlockedDecrement(&g_objects); }
void LockServer(bool lock) {
    if (lock) {
        InterlockedIncrement(&g_locks);
    } else {
        InterlockedDecrement(&g_locks);
    }
}
bool CanUnload() { return g_objects == 0 && g_locks == 0; }

std::wstring ModulePath() {
    std::vector<wchar_t> buffer(MAX_PATH);
    for (;;) {
        const DWORD length = GetModuleFileNameW(g_module, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0) return {};
        if (length < buffer.size()) return std::wstring(buffer.data(), length);
        if (buffer.size() >= 32768) return {};
        buffer.resize(buffer.size() * 2);
    }
}

std::wstring ModuleDirectory() {
    const std::wstring path = ModulePath();
    const auto slash = path.find_last_of(L"\\/");
    return slash == std::wstring::npos ? std::wstring() : path.substr(0, slash);
}

std::wstring PathInModuleDirectory(const wchar_t* fileName) { return ModuleDirectory() + L"\\" + fileName; }

std::wstring LocalAppDataPath() {
    const DWORD needed = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (needed > 1) {
        std::wstring value(needed, L'\0');
        const DWORD written = GetEnvironmentVariableW(L"LOCALAPPDATA", value.data(), needed);
        if (written > 0 && written < needed) {
            value.resize(written);
            return value;
        }
    }
    PWSTR known = nullptr;
    std::wstring result;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &known))) result = known;
    CoTaskMemFree(known);
    return result;
}

std::optional<std::string> ReadSmallFile(const std::wstring& path, DWORD maxBytes) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                              nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return std::nullopt;
    std::optional<std::string> contents;
    LARGE_INTEGER size{};
    if (GetFileSizeEx(file, &size) && size.QuadPart <= static_cast<LONGLONG>(maxBytes)) {
        std::string buffer(static_cast<std::size_t>(size.QuadPart), '\0');
        DWORD read = 0;
        if (buffer.empty() || ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr)) {
            buffer.resize(read);
            contents = std::move(buffer);
        }
    }
    CloseHandle(file);
    return contents;
}

QuickMenuSettings LoadSettings() {
    return LoadQuickMenuSettings(SettingsPathCandidates(LocalAppDataPath(), ModulePath()),
                                 [](const std::wstring& path) { return ReadSmallFile(path); });
}

}  // namespace qic::win
