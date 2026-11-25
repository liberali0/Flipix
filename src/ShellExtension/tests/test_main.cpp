#include "test_harness.h"

#include <cstdio>
#include <cstring>
#include <exception>

namespace testing {
namespace {
int g_failures = 0;
}  // namespace

std::vector<TestCase>& Registry() {
    static std::vector<TestCase> registry;
    return registry;
}

void ReportFailure(const char* file, int line, const std::string& message) {
    ++g_failures;
    std::fprintf(stderr, "    %s:%d: %s\n", file, line, message.c_str());
}

std::string Printable(const std::wstring& value) {
    // Escape everything outside printable ASCII so output is stable on every console.
    std::string out = "L\"";
    for (wchar_t ch : value) {
        const auto code = static_cast<unsigned long>(ch);
        if (code >= 0x20 && code < 0x7F) {
            out += static_cast<char>(code);
        } else {
            char buffer[16];
            std::snprintf(buffer, sizeof buffer, "\\u%04lX", code);
            out += buffer;
        }
    }
    return out + "\"";
}

}  // namespace testing

int main(int argc, char** argv) {
    const char* filter = argc > 1 ? argv[1] : nullptr;
    int run = 0;
    int failedTests = 0;
    for (const auto& test : testing::Registry()) {
        if (filter != nullptr && std::strstr(test.name, filter) == nullptr) continue;
        const int before = testing::g_failures;
        ++run;
        try {
            test.body();
        } catch (const std::exception& error) {
            testing::ReportFailure(__FILE__, __LINE__, std::string("uncaught exception: ") + error.what());
        } catch (...) {
            testing::ReportFailure(__FILE__, __LINE__, "uncaught non-standard exception");
        }
        if (testing::g_failures != before) {
            ++failedTests;
            std::fprintf(stderr, "[FAIL] %s\n", test.name);
        }
    }
    std::printf("%d tests, %d failed\n", run, failedTests);
    return failedTests == 0 && run > 0 ? 0 : 1;
}
