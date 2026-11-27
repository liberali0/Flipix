#include "core/registration.h"

#include <algorithm>
#include <fstream>
#include <sstream>

#include "core/formats.h"
#include "core/menu_model.h"
#include "core/text.h"
#include "test_harness.h"

using qic::kShellExtensionClsid;
using qic::LegacyRegistrationKeys;
using qic::LegacyRegistrationValues;
using qic::RegistryValue;

namespace {
const std::wstring kDll = L"C:\\Program Files\\QIC\\Flipix.ShellExtension.dll";

bool Contains(const std::vector<RegistryValue>& values, const RegistryValue& wanted) {
    return std::find(values.begin(), values.end(), wanted) != values.end();
}

std::string ReadRepoFile(const std::string& relative) {
    std::ifstream file(std::string(QIC_REPO_ROOT) + "/" + relative, std::ios::binary);
    std::ostringstream contents;
    contents << file.rdbuf();
    return contents.str();
}

// Every value of attribute `name` in document order, e.g. Type="..." -> [".png", ...].
std::vector<std::string> AttributeValues(const std::string& xml, const std::string& name) {
    std::vector<std::string> values;
    const std::string marker = " " + name + "=\"";
    for (auto at = xml.find(marker); at != std::string::npos; at = xml.find(marker, at + 1)) {
        const auto start = at + marker.size();
        values.push_back(xml.substr(start, xml.find('"', start) - start));
    }
    return values;
}
}  // namespace

TEST(LegacyRegistration_RegistersInprocServer) {
    const auto values = LegacyRegistrationValues(kShellExtensionClsid, kDll);
    const std::wstring clsidKey = L"Software\\Classes\\CLSID\\{233119CD-5628-4066-AB18-D528BC05C51F}";
    CHECK(Contains(values, {clsidKey, L"", L"빠른 이미지 포맷 변환"}));
    CHECK(Contains(values, {clsidKey + L"\\InprocServer32", L"", kDll}));
    CHECK(Contains(values, {clsidKey + L"\\InprocServer32", L"ThreadingModel", L"Apartment"}));
}

TEST(LegacyRegistration_AddsVerbForEveryReadableExtension) {
    const auto values = LegacyRegistrationValues(kShellExtensionClsid, kDll);
    for (const auto& extension : qic::SupportedInputExtensions()) {
        const std::wstring verbKey =
            L"Software\\Classes\\SystemFileAssociations\\" + extension + L"\\shell\\QuickImageConvert";
        CHECK(Contains(values, {verbKey, L"MUIVerb", qic::RootTitle()}));
        CHECK(Contains(values, {verbKey, L"ExplorerCommandHandler", std::wstring(kShellExtensionClsid)}));
        // Without "Player" the classic menu hides the verb when more than 15 files are selected.
        CHECK(Contains(values, {verbKey, L"MultiSelectModel", L"Player"}));
    }
    CHECK_EQ(values.size(), 3 + 3 * qic::SupportedInputExtensions().size());
}

TEST(LegacyRegistration_UnregisterRemovesEveryKeyItWrites) {
    const auto keys = LegacyRegistrationKeys(kShellExtensionClsid);
    for (const auto& value : LegacyRegistrationValues(kShellExtensionClsid, kDll)) {
        const bool covered = std::any_of(keys.begin(), keys.end(), [&](const std::wstring& key) {
            return value.subKey == key || value.subKey.rfind(key + L"\\", 0) == 0;
        });
        if (!covered) ::testing::ReportFailure(__FILE__, __LINE__, "not removed: " + ::testing::Printable(value.subKey));
    }
    // Never delete shared parents such as ...\SystemFileAssociations\.png\shell.
    for (const auto& key : keys) {
        CHECK(key.find(L"QuickImageConvert") != std::wstring::npos || key.find(L"{233119CD") != std::wstring::npos);
    }
}

TEST(ManifestFragment_ListsExactlyTheReadableExtensions) {
    const std::string xml = ReadRepoFile("packaging/shell-extension/AppxManifest.fragment.xml");
    CHECK(!xml.empty());
    std::vector<std::string> expected;
    for (const auto& extension : qic::SupportedInputExtensions()) expected.push_back(qic::ToUtf8(extension));
    CHECK_EQ(AttributeValues(xml, "Type"), expected);
}

TEST(ManifestFragment_UsesTheDllClsidVerbAndFileName) {
    const std::string xml = ReadRepoFile("packaging/shell-extension/AppxManifest.fragment.xml");
    const std::string clsid = qic::ToUtf8(kShellExtensionClsid.substr(1, kShellExtensionClsid.size() - 2));
    const std::size_t extensions = qic::SupportedInputExtensions().size();

    const auto verbClsids = AttributeValues(xml, "Clsid");
    CHECK_EQ(verbClsids, std::vector<std::string>(extensions, clsid));
    CHECK_EQ(AttributeValues(xml, "Id"), [&] {
        std::vector<std::string> ids(extensions, qic::ToUtf8(qic::kVerbId));
        ids.push_back(clsid);  // <com:Class Id="...">
        return ids;
    }());
    CHECK_EQ(AttributeValues(xml, "Path"), std::vector<std::string>{"Flipix.ShellExtension.dll"});
    CHECK_EQ(AttributeValues(xml, "ThreadingModel"), std::vector<std::string>{"STA"});
}

TEST(LegacyRegistration_EnglishMenuName) {
    const auto values = LegacyRegistrationValues(kShellExtensionClsid, kDll, true);
    const std::wstring verbKey = L"Software\\Classes\\SystemFileAssociations\\.png\\shell\\QuickImageConvert";
    CHECK(Contains(values, {verbKey, L"MUIVerb", L"Quick image format convert"}));
}
