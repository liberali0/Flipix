#include "core/formats.h"

#include "test_harness.h"

using qic::FindOutputFormat;
using qic::FormatIdForPath;
using qic::IsSupportedInput;
using qic::SupportedInputExtensions;

TEST(FormatIdForPath_MapsEveryJpegSpelling) {
    CHECK_EQ(FormatIdForPath(L"C:\\a\\B.JPEG"), std::optional<std::string_view>("jpg"));
    CHECK_EQ(FormatIdForPath(L"C:\\a\\b.jpg"), std::optional<std::string_view>("jpg"));
    CHECK_EQ(FormatIdForPath(L"C:\\a\\b.jpe"), std::optional<std::string_view>("jpg"));
    CHECK_EQ(FormatIdForPath(L"C:\\a\\b.Jfif"), std::optional<std::string_view>("jpg"));
}

TEST(FormatIdForPath_MapsOtherFormats) {
    CHECK_EQ(FormatIdForPath(L"x.HEIF"), std::optional<std::string_view>("heic"));
    CHECK_EQ(FormatIdForPath(L"x.heic"), std::optional<std::string_view>("heic"));
    CHECK_EQ(FormatIdForPath(L"x.tif"), std::optional<std::string_view>("tiff"));
    CHECK_EQ(FormatIdForPath(L"x.dib"), std::optional<std::string_view>("bmp"));
    CHECK_EQ(FormatIdForPath(L"x.webp"), std::optional<std::string_view>("webp"));
    CHECK_EQ(FormatIdForPath(L"x.avif"), std::optional<std::string_view>("avif"));
    CHECK_EQ(FormatIdForPath(L"anim.GIF"), std::optional<std::string_view>("gif"));
}

TEST(FormatIdForPath_RejectsUnknownAndMissingExtensions) {
    CHECK_EQ(FormatIdForPath(L"C:\\a.b\\noext"), std::optional<std::string_view>());
    CHECK_EQ(FormatIdForPath(L"C:\\a\\vector.svg"), std::optional<std::string_view>());
    CHECK_EQ(FormatIdForPath(L"C:\\a\\notes.txt"), std::optional<std::string_view>());
}

TEST(FindOutputFormat_KnowsEngineOutputsOnly) {
    for (const char* id : {"png", "jpg", "bmp", "tiff", "webp", "avif"}) {
        CHECK(FindOutputFormat(id) != nullptr);
    }
    CHECK(FindOutputFormat("heic") == nullptr);  // engine reads HEIC but cannot write it
    CHECK(FindOutputFormat("gif") == nullptr);
    CHECK(FindOutputFormat("PNG") == nullptr);   // ids are exact lower case
    CHECK(FindOutputFormat("") == nullptr);
}

TEST(FindOutputFormat_OnlyJpgIsLossy) {
    for (const char* id : {"png", "bmp", "tiff", "webp", "avif"}) {
        const auto* format = FindOutputFormat(id);
        CHECK(format != nullptr && !format->lossyOutput);
    }
    const auto* jpg = FindOutputFormat("jpg");
    CHECK(jpg != nullptr && jpg->lossyOutput);
}

static std::wstring LabelOf(const char* id) {
    const auto* format = FindOutputFormat(id);
    return format == nullptr ? std::wstring(L"<missing>") : std::wstring(format->label);
}

TEST(FindOutputFormat_HasMenuLabels) {
    CHECK_EQ(LabelOf("png"), std::wstring(L"PNG"));
    CHECK_EQ(LabelOf("jpg"), std::wstring(L"JPG"));
    CHECK_EQ(LabelOf("tiff"), std::wstring(L"TIFF"));
    CHECK_EQ(LabelOf("webp"), std::wstring(L"WEBP"));
    CHECK_EQ(LabelOf("avif"), std::wstring(L"AVIF"));
}

TEST(SupportedInputExtensions_MatchesContract) {
    const std::vector<std::wstring> expected = {L".png", L".jpg", L".jpeg", L".jpe", L".jfif",
                                                L".bmp", L".dib", L".tif", L".tiff", L".webp",
                                                L".avif", L".heic", L".heif", L".gif"};
    CHECK_EQ(SupportedInputExtensions(), expected);
}

TEST(IsSupportedInput_IsCaseInsensitive) {
    CHECK(IsSupportedInput(L"D:\\DCIM\\IMG_0001.HEIC"));
    CHECK(IsSupportedInput(L"a.PNG"));
    CHECK(IsSupportedInput(L"a.GIF"));
    CHECK(!IsSupportedInput(L"a.svg"));
    CHECK(!IsSupportedInput(L"C:\\folder.png\\inside"));
}
