#include "core/command_line.h"

#include "test_harness.h"

using qic::BuildLaunchPlan;
using qic::FormatFailureMessage;
using qic::QuoteArg;

TEST(QuoteArg_LeavesSimpleArgumentsAlone) {
    CHECK_EQ(QuoteArg(L"convert"), std::wstring(L"convert"));
    CHECK_EQ(QuoteArg(L"C:\\Photos\\a.png"), std::wstring(L"C:\\Photos\\a.png"));
    CHECK_EQ(QuoteArg(L"\uD55C\uAE00.png"), std::wstring(L"\uD55C\uAE00.png"));
}

TEST(QuoteArg_QuotesSpacesAndEmpty) {
    CHECK_EQ(QuoteArg(L"C:\\새 폴더\\a b.png"), std::wstring(L"\"C:\\새 폴더\\a b.png\""));
    CHECK_EQ(QuoteArg(L"tab\there"), std::wstring(L"\"tab\there\""));
    CHECK_EQ(QuoteArg(L""), std::wstring(L"\"\""));
}

TEST(QuoteArg_EscapesQuotesAndTheirBackslashes) {
    // Examples from the "Parsing C++ command-line arguments" rules.
    CHECK_EQ(QuoteArg(L"a\"b"), std::wstring(L"\"a\\\"b\""));
    CHECK_EQ(QuoteArg(L"a\\\"b"), std::wstring(L"\"a\\\\\\\"b\""));
    CHECK_EQ(QuoteArg(L"a\\\\b c"), std::wstring(L"\"a\\\\b c\""));  // backslashes not before a quote stay
}

TEST(QuoteArg_DoublesTrailingBackslashesInsideQuotes) {
    CHECK_EQ(QuoteArg(L"C:\\my dir\\"), std::wstring(L"\"C:\\my dir\\\\\""));
}

TEST(BuildLaunchPlan_PutsFewFilesOnTheCommandLine) {
    const auto plan = BuildLaunchPlan(L"C:\\Program Files\\QIC\\imgconv.exe", {L"convert", L"--to", L"png"},
                                      {L"C:\\사진\\a.jpg", L"C:\\사진\\새 폴더\\b.heic"}, L"C:\\Temp\\list.txt");
    CHECK(!plan.usesResponseFile);
    CHECK(plan.responseFileUtf8.empty());
    CHECK_EQ(plan.commandLine,
             std::wstring(L"\"C:\\Program Files\\QIC\\imgconv.exe\" convert --to png C:\\사진\\a.jpg "
                          L"\"C:\\사진\\새 폴더\\b.heic\""));
}

TEST(BuildLaunchPlan_AlwaysQuotesTheExecutable) {
    const auto plan = BuildLaunchPlan(L"C:\\QIC\\Flipix.exe", {}, {L"C:\\a.png"}, L"C:\\t.txt");
    CHECK_EQ(plan.commandLine, std::wstring(L"\"C:\\QIC\\Flipix.exe\" C:\\a.png"));
}

TEST(BuildLaunchPlan_UsesResponseFileWhenTooLong) {
    std::vector<std::wstring> files;
    for (int i = 0; i < 3; ++i) files.push_back(L"C:\\사진\\file " + std::to_wstring(i) + L".png");
    const auto plan = BuildLaunchPlan(L"C:\\QIC\\imgconv.exe", {L"convert", L"--to", L"bmp"}, files,
                                      L"C:\\Users\\kim\\AppData\\Local\\Temp\\QIC sel.txt", 60);
    CHECK(plan.usesResponseFile);
    CHECK_EQ(plan.commandLine,
             std::wstring(L"\"C:\\QIC\\imgconv.exe\" convert --to bmp \"@C:\\Users\\kim\\AppData\\Local\\Temp\\QIC sel.txt\""));
    CHECK_EQ(plan.responseFileUtf8,
             std::string("C:\\\xEC\x82\xAC\xEC\xA7\x84\\file 0.png\r\n"
                         "C:\\\xEC\x82\xAC\xEC\xA7\x84\\file 1.png\r\n"
                         "C:\\\xEC\x82\xAC\xEC\xA7\x84\\file 2.png\r\n"));
}

TEST(BuildLaunchPlan_DefaultLimitSwitchesForThousandsOfFiles) {
    std::vector<std::wstring> files(1000, L"C:\\Users\\kim\\Pictures\\Vacation 2026\\IMG_000000.HEIC");
    const auto plan = BuildLaunchPlan(L"C:\\QIC\\imgconv.exe", {L"convert", L"--to", L"png"}, files, L"C:\\t.txt");
    CHECK(plan.usesResponseFile);
    CHECK(plan.commandLine.size() < qic::kMaxCommandLineChars);
}

TEST(FormatFailureMessage_ListsCliErrorLines) {
    const std::string stderrText =
        "ERROR C:\\\xEC\x82\xAC\xEC\xA7\x84\\a.heic: decode failed\r\n"
        "\r\n"
        "ERROR C:\\b.png: output exists\n";
    CHECK_EQ(FormatFailureMessage(1, stderrText),
             std::wstring(L"일부 파일을 변환하지 못했어요.\n\n"
                          L"ERROR C:\\사진\\a.heic: decode failed\n"
                          L"ERROR C:\\b.png: output exists"));
}

TEST(FormatFailureMessage_TruncatesLongLists) {
    std::string stderrText;
    for (int i = 1; i <= 5; ++i) stderrText += "ERROR C:\\" + std::to_string(i) + ".png: bad\n";
    CHECK_EQ(FormatFailureMessage(1, stderrText, 2),
             std::wstring(L"일부 파일을 변환하지 못했어요.\n\n"
                          L"ERROR C:\\1.png: bad\n"
                          L"ERROR C:\\2.png: bad\n"
                          L"… 외 3개"));
}

TEST(FormatFailureMessage_ExplainsUsageErrorsAndUnknownCodes) {
    CHECK_EQ(FormatFailureMessage(2, ""), std::wstring(L"변환을 시작하지 못했어요.\n\n(종료 코드 2)"));
    CHECK_EQ(FormatFailureMessage(1, "  \r\n"), std::wstring(L"일부 파일을 변환하지 못했어요.\n\n(종료 코드 1)"));
    CHECK_EQ(FormatFailureMessage(3221225477UL, ""),
             std::wstring(L"변환 중 문제가 생겼어요.\n\n(종료 코드 3221225477)"));
}

TEST(IsStaleTempFile_OnlyOldFilesTheMenuCreated) {
    using qic::IsStaleTempFile;
    CHECK(IsStaleTempFile(L"selection-1234ABCD.txt", 24 * 60 * 60));
    CHECK(IsStaleTempFile(L"errors-1234ABCD.txt", 3 * 24 * 60 * 60));
    CHECK(!IsStaleTempFile(L"selection-1234ABCD.txt", 24 * 60 * 60 - 1));
    CHECK(!IsStaleTempFile(L"notes.txt", 10 * 24 * 60 * 60));
    CHECK(!IsStaleTempFile(L"selection-1234ABCD.png", 10 * 24 * 60 * 60));
}

TEST(IsUpdateCheckDue_OncePerDay) {
    CHECK(qic::IsUpdateCheckDue(false, 0));
    CHECK(!qic::IsUpdateCheckDue(true, 60));
    CHECK(!qic::IsUpdateCheckDue(true, 24 * 60 * 60 - 1));
    CHECK(qic::IsUpdateCheckDue(true, 24 * 60 * 60));
}

TEST(FollowUpArguments_OnlyWhenThereIsSomethingToDo) {
    using Args = std::vector<std::wstring>;
    CHECK(qic::FollowUpArguments(false, 3, false).empty());
    CHECK(qic::FollowUpArguments(true, 0, false).empty());
    CHECK(qic::FollowUpArguments(true, 3, false) == (Args{L"--background", L"--converted", L"3"}));
    CHECK(qic::FollowUpArguments(false, 3, true) == (Args{L"--background", L"--check-update"}));
    CHECK(qic::FollowUpArguments(true, 2, true) == (Args{L"--background", L"--converted", L"2", L"--check-update"}));
}

TEST(FormatFailureMessage_English) {
    CHECK_EQ(FormatFailureMessage(2, "", 12, true), std::wstring(L"The conversion could not start.\n\n(exit code 2)"));
    std::string stderrText;
    for (int i = 1; i <= 3; ++i) stderrText += "ERROR C:\\" + std::to_string(i) + ".png: bad\n";
    CHECK_EQ(FormatFailureMessage(1, stderrText, 1, true),
             std::wstring(L"Some files could not be converted.\n\n"
                          L"ERROR C:\\1.png: bad\n"
                          L"… and 2 more"));
}
