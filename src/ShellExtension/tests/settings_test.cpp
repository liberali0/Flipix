#include "core/settings.h"

#include <map>

#include "test_harness.h"

using qic::DefaultQuickMenuSettings;
using qic::LoadQuickMenuSettings;
using qic::MovesOriginals;
using qic::PackageFamilyNameFromFullName;
using qic::ParseQuickMenuSettings;
using qic::QuickMenuSettings;
using qic::SettingsPathCandidates;

using Formats = std::vector<std::string>;

TEST(DefaultQuickMenuSettings_IsPngJpgBmpWithDetails) {
    const QuickMenuSettings defaults = DefaultQuickMenuSettings();
    CHECK_EQ(defaults.formats, (Formats{"png", "jpg", "bmp"}));
    CHECK(defaults.showDetails);
}

TEST(ParseQuickMenuSettings_ReadsAppWrittenFile) {
    // Shape written by the app's SettingsStore (System.Text.Json, camelCase, indented).
    const char* json = R"({
  "quickMenu": {
    "formats": [
      "webp",
      "png"
    ],
    "showDetails": false
  },
  "defaultTargetFormat": "png",
  "maxParallelism": 0,
  "conflict": "rename",
  "customOutputFolder": null,
  "preserveStructure": true
})";
    const QuickMenuSettings settings = ParseQuickMenuSettings(json);
    CHECK_EQ(settings.formats, (Formats{"webp", "png"}));
    CHECK(!settings.showDetails);
}

TEST(ParseQuickMenuSettings_DecodesEscapedIds) {
    // System.Text.Json may escape characters; ids must still be recognized.
    const QuickMenuSettings settings = ParseQuickMenuSettings(R"({"quickMenu":{"formats":["\u0061vif"]}})");
    CHECK_EQ(settings.formats, (Formats{"avif"}));
}

TEST(ParseQuickMenuSettings_NormalizesAndFiltersIds) {
    const QuickMenuSettings settings = ParseQuickMenuSettings(
        R"({"quickMenu":{"formats":["PNG","jpeg","Tif","heic","gif","png",5,null,"","webp"]}})");
    CHECK_EQ(settings.formats, (Formats{"png", "jpg", "tiff", "webp"}));
}

TEST(ParseQuickMenuSettings_RespectsExplicitEmptyList) {
    const QuickMenuSettings settings = ParseQuickMenuSettings(R"({"quickMenu":{"formats":[],"showDetails":true}})");
    CHECK(settings.formats.empty());
    CHECK(settings.showDetails);
}

TEST(ParseQuickMenuSettings_FallsBackWhenFieldsAreMissingOrWrong) {
    CHECK_EQ(ParseQuickMenuSettings("{}"), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings(R"({"quickMenu": null})"), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings(R"({"quickMenu": {"formats": "png"}})"), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings(R"({"quickMenu": {"showDetails": "no"}})"), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings("[1, 2]"), DefaultQuickMenuSettings());

    const QuickMenuSettings onlyDetails = ParseQuickMenuSettings(R"({"quickMenu": {"showDetails": false}})");
    CHECK_EQ(onlyDetails.formats, DefaultQuickMenuSettings().formats);
    CHECK(!onlyDetails.showDetails);
}

TEST(ParseQuickMenuSettings_FallsBackOnBrokenJson) {
    CHECK_EQ(ParseQuickMenuSettings(""), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings(R"({"quickMenu": {"formats": ["png")"), DefaultQuickMenuSettings());
    CHECK_EQ(ParseQuickMenuSettings("\xEF\xBB\xBF{\"quickMenu\":{\"formats\":[\"bmp\"]}}").formats, (Formats{"bmp"}));
}

TEST(PackageFamilyNameFromFullName_DropsVersionArchAndResource) {
    CHECK_EQ(PackageFamilyNameFromFullName(L"QuickImageConverter_1.0.0.0_x64__8wekyb3d8bbwe"),
             std::optional<std::wstring>(L"QuickImageConverter_8wekyb3d8bbwe"));
    CHECK_EQ(PackageFamilyNameFromFullName(L"Contoso.App_2.1.0.0_neutral_split.scale-100_abc123"),
             std::optional<std::wstring>(L"Contoso.App_abc123"));
    CHECK_EQ(PackageFamilyNameFromFullName(L"NotAPackage"), std::optional<std::wstring>());
    CHECK_EQ(PackageFamilyNameFromFullName(L"a_1_x64__"), std::optional<std::wstring>());
}

TEST(SettingsPathCandidates_UnpackagedUsesLocalAppData) {
    const auto paths = SettingsPathCandidates(L"C:\\Users\\kim\\AppData\\Local",
                                              L"C:\\Program Files\\QuickImageConverter\\ImageConverter.ShellExtension.dll");
    CHECK_EQ(paths, (std::vector<std::wstring>{
                        L"C:\\Users\\kim\\AppData\\Local\\QuickImageConverter\\settings.json"}));
}

TEST(SettingsPathCandidates_PackagedPrefersLocalCache) {
    const auto paths = SettingsPathCandidates(
        L"C:\\Users\\kim\\AppData\\Local\\",
        L"C:\\Program Files\\windowsapps\\QuickImageConverter_1.0.0.0_x64__8wekyb3d8bbwe\\ImageConverter.ShellExtension.dll");
    CHECK_EQ(paths, (std::vector<std::wstring>{
                        L"C:\\Users\\kim\\AppData\\Local\\Packages\\QuickImageConverter_8wekyb3d8bbwe\\LocalCache\\Local\\QuickImageConverter\\settings.json",
                        L"C:\\Users\\kim\\AppData\\Local\\QuickImageConverter\\settings.json"}));
}

TEST(SettingsPathCandidates_EmptyWithoutLocalAppData) {
    CHECK(SettingsPathCandidates(L"", L"C:\\x\\a.dll").empty());
}

TEST(LoadQuickMenuSettings_UsesFirstReadableCandidate) {
    const std::map<std::wstring, std::string> files = {
        {L"second", R"({"quickMenu":{"formats":["webp"]}})"},
        {L"third", R"({"quickMenu":{"formats":["avif"]}})"},
    };
    const auto read = [&](const std::wstring& path) -> std::optional<std::string> {
        const auto found = files.find(path);
        if (found == files.end()) return std::nullopt;
        return found->second;
    };
    CHECK_EQ(LoadQuickMenuSettings({L"first", L"second", L"third"}, read).formats, (Formats{"webp"}));
    CHECK_EQ(LoadQuickMenuSettings({L"first"}, read), DefaultQuickMenuSettings());
    CHECK_EQ(LoadQuickMenuSettings({}, read), DefaultQuickMenuSettings());
}

TEST(ParseQuickMenuSettings_KeepsOriginalsUnlessExplicitlyTrue) {
    CHECK(!DefaultQuickMenuSettings().deleteOriginal);
    CHECK(!ParseQuickMenuSettings(R"({"quickMenu":{"formats":["png"]}})").deleteOriginal);
    CHECK(!ParseQuickMenuSettings(R"({"quickMenu":{"deleteOriginal":"yes"}})").deleteOriginal);
    CHECK(!ParseQuickMenuSettings(R"({"quickMenu":{"deleteOriginal":1}})").deleteOriginal);
    CHECK(!ParseQuickMenuSettings(R"({"quickMenu":{"deleteOriginal":false}})").deleteOriginal);
    CHECK(ParseQuickMenuSettings(R"({"quickMenu":{"deleteOriginal":true}})").deleteOriginal);
}

TEST(ParseQuickMenuSettings_ReadsMoveOriginalFolder) {
    const auto settings = ParseQuickMenuSettings(
        R"({"quickMenu":{"moveOriginal":true,"moveOriginalTo":"D:\\\uC6D0\uBCF8 \uBCF4\uAD00"}})");
    CHECK(settings.moveOriginal);
    CHECK_EQ(settings.moveOriginalTo, std::string("D:\\\xEC\x9B\x90\xEB\xB3\xB8 \xEB\xB3\xB4\xEA\xB4\x80"));
    CHECK(MovesOriginals(settings));
}

TEST(ParseQuickMenuSettings_MovesNothingWithoutAFullFolderPath) {
    CHECK(!MovesOriginals(DefaultQuickMenuSettings()));
    CHECK(!MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":true}})")));
    CHECK(!MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":true,"moveOriginalTo":"relative\\dir"}})")));
    CHECK(!MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":true,"moveOriginalTo":""}})")));
    CHECK(!MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":"yes","moveOriginalTo":"D:\\keep"}})")));
    CHECK(!MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":false,"moveOriginalTo":"D:\\keep"}})")));
    CHECK(MovesOriginals(ParseQuickMenuSettings(R"({"quickMenu":{"moveOriginal":true,"moveOriginalTo":"\\\\nas\\photos"}})")));
}
