#include "core/text.h"

#include "test_harness.h"

using qic::ExtensionOf;
using qic::FromUtf8;
using qic::LowerAscii;
using qic::ToUtf8;

TEST(ToUtf8_EncodesKorean) {
    CHECK_EQ(ToUtf8(L"\uD55C\uAE00"), std::string("\xED\x95\x9C\xEA\xB8\x80"));
}

TEST(ToUtf8_EncodesAsciiUnchanged) {
    CHECK_EQ(ToUtf8(L"C:\\a b\\c.png"), std::string("C:\\a b\\c.png"));
}

TEST(Utf8_RoundTripsEmojiOutsideBmp) {
    const std::string utf8 = "\xF0\x9F\x93\xB7 photo";  // U+1F4F7 CAMERA
    const std::wstring wide = FromUtf8(utf8);
    CHECK_EQ(ToUtf8(wide), utf8);
    if constexpr (sizeof(wchar_t) == 2) {
        CHECK_EQ(wide.size(), std::size_t{8});  // surrogate pair + 6
    } else {
        CHECK_EQ(wide.size(), std::size_t{7});
    }
}

TEST(FromUtf8_DecodesKorean) {
    CHECK_EQ(FromUtf8("\xED\x95\x9C\xEA\xB8\x80"), std::wstring(L"\uD55C\uAE00"));
}

TEST(FromUtf8_ReplacesMalformedBytes) {
    CHECK_EQ(FromUtf8("a\xFF" "b"), std::wstring(L"a\uFFFDb"));
    CHECK_EQ(FromUtf8("\xED\x95"), std::wstring(L"\uFFFD"));  // truncated sequence
    CHECK_EQ(FromUtf8("\xC0\xAF"), std::wstring(L"\uFFFD\uFFFD"));  // overlong '/'
}

TEST(ToUtf8_ReplacesUnpairedSurrogate) {
    if constexpr (sizeof(wchar_t) != 2) return;  // only representable in UTF-16
    const wchar_t lone[] = {L'a', static_cast<wchar_t>(0xD800), L'b', 0};
    CHECK_EQ(ToUtf8(lone), std::string("a\xEF\xBF\xBD" "b"));
}

TEST(LowerAscii_OnlyTouchesAsciiLetters) {
    CHECK_EQ(LowerAscii(std::wstring_view(L"PNG.\uD55C\u00C9")), std::wstring(L"png.\uD55C\u00C9"));
    CHECK_EQ(LowerAscii(std::string_view("JPEG")), std::string("jpeg"));
}

TEST(ExtensionOf_LowercasesLastExtension) {
    CHECK_EQ(ExtensionOf(L"C:\\Photos\\IMG_0001.HEIC"), std::wstring(L".heic"));
    CHECK_EQ(ExtensionOf(L"C:\\a\\archive.tar.GZ"), std::wstring(L".gz"));
    CHECK_EQ(ExtensionOf(L"/home/u/a.png"), std::wstring(L".png"));
}

TEST(ExtensionOf_IgnoresDotsInFolderNames) {
    CHECK_EQ(ExtensionOf(L"C:\\my.photos\\noext"), std::wstring());
    CHECK_EQ(ExtensionOf(L"C:\\a\\trailingdot."), std::wstring());
    CHECK_EQ(ExtensionOf(L""), std::wstring());
}
