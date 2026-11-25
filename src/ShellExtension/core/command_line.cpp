#include "core/command_line.h"

#include "core/text.h"

namespace qic {
namespace {

std::wstring QuoteExecutable(std::wstring_view exePath) {
    // argv[0] is parsed without escapes, and Windows paths cannot contain '"'.
    return L"\"" + std::wstring(exePath) + L"\"";
}

std::wstring Trim(std::wstring_view text) {
    const auto first = text.find_first_not_of(L" \t\r");
    if (first == std::wstring_view::npos) return {};
    const auto last = text.find_last_not_of(L" \t\r");
    return std::wstring(text.substr(first, last - first + 1));
}

}  // namespace

std::wstring QuoteArg(std::wstring_view argument) {
    if (!argument.empty() && argument.find_first_of(L" \t\n\v\"") == std::wstring_view::npos) {
        return std::wstring(argument);
    }
    std::wstring quoted = L"\"";
    std::size_t backslashes = 0;
    for (const wchar_t ch : argument) {
        if (ch == L'\\') {
            ++backslashes;
            continue;
        }
        if (ch == L'"') {
            quoted.append(backslashes * 2 + 1, L'\\');
        } else {
            quoted.append(backslashes, L'\\');
        }
        backslashes = 0;
        quoted += ch;
    }
    quoted.append(backslashes * 2, L'\\');  // so the closing quote is not escaped
    quoted += L'"';
    return quoted;
}

LaunchPlan BuildLaunchPlan(std::wstring_view exePath,
                           const std::vector<std::wstring>& leadingArgs,
                           const std::vector<std::wstring>& files,
                           std::wstring_view responseFilePath,
                           std::size_t maxChars) {
    LaunchPlan plan;
    std::wstring prefix = QuoteExecutable(exePath);
    for (const auto& argument : leadingArgs) prefix += L" " + QuoteArg(argument);

    plan.commandLine = prefix;
    for (const auto& file : files) plan.commandLine += L" " + QuoteArg(file);
    if (plan.commandLine.size() <= maxChars) return plan;

    plan.usesResponseFile = true;
    plan.commandLine = prefix + L" " + QuoteArg(L"@" + std::wstring(responseFilePath));
    for (const auto& file : files) plan.responseFileUtf8 += ToUtf8(file) + "\r\n";
    return plan;
}

std::wstring FormatFailureMessage(unsigned long exitCode, std::string_view stderrUtf8, std::size_t maxLines) {
    std::wstring message;
    switch (exitCode) {
        case 1: message = L"일부 파일을 변환하지 못했어요."; break;
        case 2: message = L"변환을 시작하지 못했어요."; break;
        default: message = L"변환 중 문제가 생겼어요."; break;
    }

    std::vector<std::wstring> lines;
    const std::wstring text = FromUtf8(stderrUtf8);
    std::size_t start = 0;
    while (start <= text.size()) {
        const auto newline = text.find(L'\n', start);
        const std::wstring line = Trim(std::wstring_view(text).substr(start, newline - start));
        if (!line.empty()) lines.push_back(line);
        if (newline == std::wstring::npos) break;
        start = newline + 1;
    }

    if (lines.empty()) return message + L"\n\n(종료 코드 " + std::to_wstring(exitCode) + L")";

    message += L"\n";
    for (std::size_t i = 0; i < lines.size() && i < maxLines; ++i) message += L"\n" + lines[i];
    if (lines.size() > maxLines) message += L"\n… 외 " + std::to_wstring(lines.size() - maxLines) + L"개";
    return message;
}

bool IsStaleTempFile(std::wstring_view fileName, unsigned long long ageSeconds) {
    constexpr unsigned long long kOneDay = 24ULL * 60 * 60;
    const auto startsWith = [&](std::wstring_view prefix) { return fileName.substr(0, prefix.size()) == prefix; };
    const bool ours = (startsWith(kSelectionFilePrefix) || startsWith(kErrorsFilePrefix)) &&
                      ExtensionOf(fileName) == L".txt";
    return ours && ageSeconds >= kOneDay;
}

}  // namespace qic
