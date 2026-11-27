// Stand-in for imgconv.exe / Flipix.exe in the smoke tests. Records how it was started
// into the file named by QIC_FAKE_LOG (UTF-8): its own file name, then one argument per line,
// then the lines of any @response file prefixed with "@> ".
#include <windows.h>

#include <cstdio>
#include <string>

#include "core/text.h"

namespace {

std::wstring Env(const wchar_t* name) {
    wchar_t buffer[1024] = {};
    const DWORD length = GetEnvironmentVariableW(name, buffer, 1024);
    return length > 0 && length < 1024 ? std::wstring(buffer, length) : std::wstring();
}

std::string ReadAll(const std::wstring& path) {
    std::string bytes;
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return "<unreadable>";
    char buffer[65536];
    DWORD read = 0;
    while (ReadFile(file, buffer, sizeof buffer, &read, nullptr) && read > 0) bytes.append(buffer, read);
    CloseHandle(file);
    return bytes;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
    std::wstring self = argv[0];
    self = self.substr(self.find_last_of(L"\\/") + 1);
    std::string log = qic::ToUtf8(self) + "\n";
    for (int i = 1; i < argc; ++i) {
        log += qic::ToUtf8(argv[i]) + "\n";
        if (argv[i][0] == L'@') {
            const std::string list = ReadAll(argv[i] + 1);
            std::size_t start = 0;
            while (start < list.size()) {
                const auto end = list.find("\r\n", start);
                log += "@> " + list.substr(start, end - start) + "\n";
                if (end == std::string::npos) break;
                start = end + 2;
            }
        }
    }

    const std::wstring target = Env(L"QIC_FAKE_LOG");
    if (!target.empty()) {
        // Write then rename so the test never reads a half-written log.
        const std::wstring partial = target + L".partial";
        HANDLE file = CreateFileW(partial.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
        if (file != INVALID_HANDLE_VALUE) {
            DWORD written = 0;
            WriteFile(file, log.data(), static_cast<DWORD>(log.size()), &written, nullptr);
            CloseHandle(file);
            MoveFileExW(partial.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING);
        }
    }
    return 0;
}
