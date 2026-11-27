#include "core/registration.h"

#include <ostream>

#include "core/formats.h"
#include "core/menu_model.h"
#include "core/text.h"

namespace qic {
namespace {

std::wstring ClsidKey(std::wstring_view clsid) { return L"Software\\Classes\\CLSID\\" + std::wstring(clsid); }

std::wstring VerbKey(const std::wstring& extension) {
    return L"Software\\Classes\\SystemFileAssociations\\" + extension + L"\\shell\\" + std::wstring(kVerbId);
}

}  // namespace

bool operator==(const RegistryValue& left, const RegistryValue& right) {
    return left.subKey == right.subKey && left.name == right.name && left.data == right.data;
}

std::ostream& operator<<(std::ostream& stream, const RegistryValue& value) {
    return stream << ToUtf8(value.subKey) << " [" << ToUtf8(value.name) << "] = " << ToUtf8(value.data);
}

std::vector<RegistryValue> LegacyRegistrationValues(std::wstring_view clsid, std::wstring_view dllPath,
                                                   bool english) {
    const std::wstring classKey = ClsidKey(clsid);
    std::vector<RegistryValue> values = {
        {classKey, L"", RootTitle(english)},
        {classKey + L"\\InprocServer32", L"", std::wstring(dllPath)},
        {classKey + L"\\InprocServer32", L"ThreadingModel", L"Apartment"},
    };
    for (const auto& extension : SupportedInputExtensions()) {
        const std::wstring verbKey = VerbKey(extension);
        values.push_back({verbKey, L"MUIVerb", RootTitle(english)});
        values.push_back({verbKey, L"ExplorerCommandHandler", std::wstring(clsid)});
        // The default ("Document") model hides the verb for selections of more than 15 files.
        values.push_back({verbKey, L"MultiSelectModel", L"Player"});
    }
    return values;
}

std::vector<std::wstring> LegacyRegistrationKeys(std::wstring_view clsid) {
    std::vector<std::wstring> keys = {ClsidKey(clsid)};
    for (const auto& extension : SupportedInputExtensions()) keys.push_back(VerbKey(extension));
    return keys;
}

}  // namespace qic
