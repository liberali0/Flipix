// Minimal dependency-free test harness shared by the Linux (g++) and Windows (MSVC) builds.
#pragma once

#include <cstddef>
#include <functional>
#include <optional>
#include <sstream>
#include <string>
#include <type_traits>
#include <utility>
#include <vector>

namespace testing {

struct TestCase {
    const char* name;
    std::function<void()> body;
};

std::vector<TestCase>& Registry();
void ReportFailure(const char* file, int line, const std::string& message);
std::string Printable(const std::wstring& value);

struct Registrar {
    Registrar(const char* name, std::function<void()> body) { Registry().push_back({name, std::move(body)}); }
};

template <typename T, typename = void>
struct IsStreamable : std::false_type {};
template <typename T>
struct IsStreamable<T, std::void_t<decltype(std::declval<std::ostream&>() << std::declval<const T&>())>>
    : std::true_type {};

template <typename T>
std::string Printable(const T& value);

inline std::string Printable(const std::string& value) { return "\"" + value + "\""; }
inline std::string Printable(std::string_view value) { return Printable(std::string(value)); }
inline std::string Printable(const char* value) { return Printable(std::string(value)); }
inline std::string Printable(std::wstring_view value) { return Printable(std::wstring(value)); }
inline std::string Printable(const wchar_t* value) { return Printable(std::wstring(value)); }
inline std::string Printable(bool value) { return value ? "true" : "false"; }

template <typename T>
std::string Printable(const std::optional<T>& value) {
    return value ? "optional(" + Printable(*value) + ")" : "nullopt";
}

template <typename T>
std::string Printable(const std::vector<T>& values) {
    std::string out = "[";
    for (std::size_t i = 0; i < values.size(); ++i) {
        if (i != 0) out += ", ";
        out += Printable(values[i]);
    }
    return out + "]";
}

template <typename T>
std::string Printable(const T& value) {
    if constexpr (std::is_enum_v<T>) {
        return "enum(" + std::to_string(static_cast<long long>(value)) + ")";
    } else if constexpr (IsStreamable<T>::value) {
        std::ostringstream stream;
        stream << value;
        return stream.str();
    } else {
        return "<value>";
    }
}

template <typename A, typename B>
std::string DescribeMismatch(const char* left, const char* right, const A& a, const B& b) {
    return std::string("CHECK_EQ(") + left + ", " + right + ")\n      left:  " + Printable(a) +
           "\n      right: " + Printable(b);
}

}  // namespace testing

#define QIC_CONCAT_INNER(a, b) a##b
#define QIC_CONCAT(a, b) QIC_CONCAT_INNER(a, b)

#define TEST(name)                                                                   \
    static void name();                                                              \
    static ::testing::Registrar QIC_CONCAT(name, _registrar)(#name, &name);          \
    static void name()

#define CHECK(expr)                                                                  \
    do {                                                                             \
        if (!(expr)) ::testing::ReportFailure(__FILE__, __LINE__, "CHECK(" #expr ")"); \
    } while (0)

// Like CHECK, but stops the current test when it fails.
#define REQUIRE(expr)                                                                \
    do {                                                                             \
        if (!(expr)) {                                                               \
            ::testing::ReportFailure(__FILE__, __LINE__, "REQUIRE(" #expr ")");      \
            return;                                                                  \
        }                                                                            \
    } while (0)

#define CHECK_EQ(left, right)                                                        \
    do {                                                                             \
        const auto& qic_left_ = (left);                                              \
        const auto& qic_right_ = (right);                                            \
        if (!(qic_left_ == qic_right_))                                              \
            ::testing::ReportFailure(__FILE__, __LINE__,                             \
                ::testing::DescribeMismatch(#left, #right, qic_left_, qic_right_));  \
    } while (0)
