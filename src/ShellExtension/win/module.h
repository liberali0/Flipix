// Process-wide state of the DLL and small Win32 helpers shared by the COM classes.
#pragma once

#include <windows.h>

#include <optional>
#include <string>

#include "core/settings.h"

namespace qic::win {

// {233119CD-5628-4066-AB18-D528BC05C51F}; must equal qic::kShellExtensionClsid.
inline constexpr CLSID kClsid = {0x233119CD, 0x5628, 0x4066, {0xAB, 0x18, 0xD5, 0x28, 0xBC, 0x05, 0xC5, 0x1F}};

// Executables the menu starts; installed in the same folder as this DLL.
inline constexpr wchar_t kCliExeName[] = L"imgconv.exe";
inline constexpr wchar_t kAppExeName[] = L"ImageConverter.exe";

void SetModule(HMODULE module);
HMODULE Module();

// Live COM objects and server locks, for DllCanUnloadNow.
void AddObjectRef();
void ReleaseObjectRef();
void LockServer(bool lock);
bool CanUnload();

std::wstring ModulePath();
std::wstring ModuleDirectory();  // without trailing backslash
std::wstring PathInModuleDirectory(const wchar_t* fileName);

// %LOCALAPPDATA%, or an empty string when it cannot be found.
std::wstring LocalAppDataPath();

// Whole file as bytes, or nullopt when it is missing, unreadable or larger than maxBytes.
std::optional<std::string> ReadSmallFile(const std::wstring& path, DWORD maxBytes = 1024 * 1024);

// The user's quick-menu settings (defaults when no settings file exists).
QuickMenuSettings LoadSettings();

}  // namespace qic::win
