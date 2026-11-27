#include "core/menu_model.h"

#include "test_harness.h"

using qic::ConfiguredSubmenu;
using qic::ConvertArguments;
using qic::DefaultQuickMenuSettings;
using qic::FilesToConvert;
using qic::IsItemVisible;
using qic::MenuItem;
using qic::MenuItemKind;
using qic::QuickMenuSettings;
using qic::RootTitle;
using qic::SupportedFiles;
using qic::VisibleSubmenu;

namespace {
using Paths = std::vector<std::wstring>;

MenuItem Convert(const char* id, const wchar_t* title) { return {MenuItemKind::Convert, id, title}; }
MenuItem Separator() { return {MenuItemKind::Separator, "", L""}; }
MenuItem Details() { return {MenuItemKind::Details, "", L"자세히..."}; }

std::vector<std::wstring> Titles(const std::vector<MenuItem>& items) {
    std::vector<std::wstring> titles;
    for (const auto& item : items) titles.push_back(item.kind == MenuItemKind::Separator ? L"---" : item.title);
    return titles;
}
}  // namespace

TEST(RootTitle_IsKoreanMenuName) { CHECK_EQ(RootTitle(), std::wstring(L"빠른 이미지 포맷 변환")); }

TEST(ConfiguredSubmenu_DefaultSettings) {
    const std::vector<MenuItem> expected = {Convert("png", L"to PNG"), Convert("jpg", L"to JPG (손실 있음)"),
                                            Convert("bmp", L"to BMP"), Separator(), Details()};
    CHECK_EQ(ConfiguredSubmenu(DefaultQuickMenuSettings()), expected);
}

TEST(ConfiguredSubmenu_FollowsUserOrderAndChoices) {
    const QuickMenuSettings settings{{"webp", "avif", "tiff"}, false};
    CHECK_EQ(Titles(ConfiguredSubmenu(settings)), (Paths{L"to WEBP", L"to AVIF", L"to TIFF"}));
}

TEST(ConfiguredSubmenu_DetailsOnlyHasNoSeparator) {
    const QuickMenuSettings settings{{}, true};
    CHECK_EQ(ConfiguredSubmenu(settings), (std::vector<MenuItem>{Details()}));
}

TEST(ConfiguredSubmenu_SkipsIdsTheEngineCannotWrite) {
    const QuickMenuSettings settings{{"heic", "png"}, false};
    CHECK_EQ(Titles(ConfiguredSubmenu(settings)), (Paths{L"to PNG"}));
}

TEST(VisibleSubmenu_HidesTargetEveryFileAlreadyHas) {
    const Paths pngs = {L"C:\\a\\1.png", L"C:\\a\\2.PNG"};
    CHECK_EQ(Titles(VisibleSubmenu(DefaultQuickMenuSettings(), pngs)),
             (Paths{L"to JPG (손실 있음)", L"to BMP", L"---", L"자세히..."}));
}

TEST(VisibleSubmenu_TreatsJpegSpellingsAsJpg) {
    const Paths jpegs = {L"C:\\a\\1.jpeg", L"C:\\a\\2.JFIF", L"C:\\a\\3.jpg"};
    CHECK_EQ(Titles(VisibleSubmenu(DefaultQuickMenuSettings(), jpegs)),
             (Paths{L"to PNG", L"to BMP", L"---", L"자세히..."}));
}

TEST(VisibleSubmenu_ShowsTargetWhenAnyFileNeedsIt) {
    const Paths mixed = {L"C:\\a\\1.png", L"C:\\a\\2.heic"};
    CHECK_EQ(Titles(VisibleSubmenu(DefaultQuickMenuSettings(), mixed)),
             (Paths{L"to PNG", L"to JPG (손실 있음)", L"to BMP", L"---", L"자세히..."}));
}

TEST(VisibleSubmenu_EmptyForNonImages) {
    CHECK(VisibleSubmenu(DefaultQuickMenuSettings(), {L"C:\\a\\notes.txt", L"C:\\a\\folder"}).empty());
    CHECK(VisibleSubmenu(DefaultQuickMenuSettings(), {}).empty());
}

TEST(VisibleSubmenu_EmptyWhenNothingLeftToShow) {
    const QuickMenuSettings onlyPng{{"png"}, false};
    CHECK(VisibleSubmenu(onlyPng, {L"C:\\a\\1.png"}).empty());
}

TEST(VisibleSubmenu_DropsSeparatorWhenNoConvertItemShows) {
    const QuickMenuSettings onlyPng{{"png"}, true};
    CHECK_EQ(VisibleSubmenu(onlyPng, {L"C:\\a\\1.png"}), (std::vector<MenuItem>{Details()}));
}

TEST(IsItemVisible_AgreesWithVisibleSubmenu) {
    const QuickMenuSettings settings = DefaultQuickMenuSettings();
    const Paths pngs = {L"C:\\a\\1.png"};
    const auto items = ConfiguredSubmenu(settings);
    CHECK_EQ(items.size(), std::size_t{5});
    if (items.size() != 5) return;
    CHECK(!IsItemVisible(items[0], settings, pngs));  // to PNG
    CHECK(IsItemVisible(items[1], settings, pngs));   // to JPG
    CHECK(IsItemVisible(items[3], settings, pngs));   // separator
    CHECK(IsItemVisible(items[4], settings, pngs));   // details
    CHECK(!IsItemVisible(items[4], settings, {L"C:\\a\\x.txt"}));
}

TEST(SupportedFiles_KeepsReadableImagesInOrder) {
    CHECK_EQ(SupportedFiles({L"C:\\b.txt", L"C:\\a.HEIC", L"C:\\dir", L"C:\\c.webp"}),
             (Paths{L"C:\\a.HEIC", L"C:\\c.webp"}));
}

TEST(FilesToConvert_SkipsUnsupportedAndSameFormat) {
    const Paths selection = {L"C:\\1.png", L"C:\\2.jpeg", L"C:\\3.txt", L"C:\\4.heic", L"C:\\5.jpg"};
    CHECK_EQ(FilesToConvert("jpg", selection), (Paths{L"C:\\1.png", L"C:\\4.heic"}));
    CHECK_EQ(FilesToConvert("png", selection), (Paths{L"C:\\2.jpeg", L"C:\\4.heic", L"C:\\5.jpg"}));
}

TEST(ConvertArguments_KeepsOriginalsByDefault) {
    CHECK_EQ(ConvertArguments("png", DefaultQuickMenuSettings()),
             (std::vector<std::wstring>{L"convert", L"--to", L"png"}));
}

TEST(ConvertArguments_AsksCliToRecycleOriginalsWhenEnabled) {
    QuickMenuSettings settings = DefaultQuickMenuSettings();
    settings.deleteOriginal = true;
    CHECK_EQ(ConvertArguments("webp", settings),
             (std::vector<std::wstring>{L"convert", L"--to", L"webp", L"--delete-source"}));
}

TEST(ConvertArguments_AsksCliToMoveOriginalsWhenEnabled) {
    QuickMenuSettings settings = DefaultQuickMenuSettings();
    settings.deleteOriginal = true;  // moving wins
    settings.moveOriginal = true;
    settings.moveOriginalTo = "D:\\\xEC\x9B\x90\xEB\xB3\xB8";
    CHECK_EQ(ConvertArguments("png", settings),
             (std::vector<std::wstring>{L"convert", L"--to", L"png", L"--move-source-to", L"D:\\\uC6D0\uBCF8"}));
}

TEST(ConvertArguments_AsksForEveryFrameWhenEnabled) {
    QuickMenuSettings settings = DefaultQuickMenuSettings();
    settings.allFrames = true;
    CHECK_EQ(ConvertArguments("png", settings),
             (std::vector<std::wstring>{L"convert", L"--to", L"png", L"--all-frames"}));
    CHECK(qic::ParseQuickMenuSettings(R"({"allFrames":true})").allFrames);
    CHECK(!qic::ParseQuickMenuSettings(R"({"allFrames":"yes"})").allFrames);
}
