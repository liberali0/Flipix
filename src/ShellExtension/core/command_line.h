// Building the command lines the menu uses to start the engine CLI and the app.
#pragma once

#include <cstddef>
#include <string>
#include <string_view>
#include <vector>

namespace qic {

// Quotes one argument so CommandLineToArgvW / the MSVC runtime parse it back unchanged.
std::wstring QuoteArg(std::wstring_view argument);

struct LaunchPlan {
    std::wstring commandLine;        // full command line, starting with the quoted exe path
    bool usesResponseFile = false;   // files are in responseFileUtf8 instead of commandLine
    std::string responseFileUtf8;    // one path per line, CRLF, UTF-8 without BOM
};

// CreateProcess accepts at most 32,767 characters; stay well below.
inline constexpr std::size_t kMaxCommandLineChars = 32000;

// `"exe" leadingArgs... files...`, or `"exe" leadingArgs... "@responseFilePath"` with the files
// moved into the response file when the direct form would exceed maxChars.
LaunchPlan BuildLaunchPlan(std::wstring_view exePath,
                           const std::vector<std::wstring>& leadingArgs,
                           const std::vector<std::wstring>& files,
                           std::wstring_view responseFilePath,
                           std::size_t maxChars = kMaxCommandLineChars);

// Message box text for a CLI run that exited with a non-zero code. stderrUtf8 holds the CLI's
// "ERROR <path>: <message>" lines; at most maxLines of them are shown.
std::wstring FormatFailureMessage(unsigned long exitCode, std::string_view stderrUtf8, std::size_t maxLines = 12);

// Temporary files the menu writes to %TEMP%\QuickImageConverter: "<prefix><guid>.txt".
inline constexpr std::wstring_view kSelectionFilePrefix = L"selection-";
inline constexpr std::wstring_view kErrorsFilePrefix = L"errors-";

// True for a temporary file the menu created (by name) that is at least a day old. Files the
// app was handed are never deleted right away, because the app may still be reading them.
bool IsStaleTempFile(std::wstring_view fileName, unsigned long long ageSeconds);

}  // namespace qic
