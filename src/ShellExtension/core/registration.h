// Identity of the shell extension and its per-user registration for the classic context menu
// (Windows 10, and "추가 옵션 표시" on Windows 11) when the app is installed without MSIX.
// The MSIX install registers the same CLSID through packaging/shell-extension/AppxManifest.fragment.xml
// instead; never do both or the item appears twice.
#pragma once

#include <iosfwd>
#include <string>
#include <string_view>
#include <vector>

namespace qic {

inline constexpr std::wstring_view kShellExtensionClsid = L"{233119CD-5628-4066-AB18-D528BC05C51F}";
inline constexpr std::wstring_view kVerbId = L"QuickImageConvert";

// One REG_SZ value under HKEY_CURRENT_USER. An empty name is the key's default value.
struct RegistryValue {
    std::wstring subKey;
    std::wstring name;
    std::wstring data;
};

bool operator==(const RegistryValue& left, const RegistryValue& right);
std::ostream& operator<<(std::ostream& stream, const RegistryValue& value);

// Values DllRegisterServer writes: the in-proc COM class, and one ExplorerCommandHandler verb
// per readable extension under Software\Classes\SystemFileAssociations.
std::vector<RegistryValue> LegacyRegistrationValues(std::wstring_view clsid, std::wstring_view dllPath);

// Keys DllUnregisterServer deletes with their subtrees.
std::vector<std::wstring> LegacyRegistrationKeys(std::wstring_view clsid);

}  // namespace qic
