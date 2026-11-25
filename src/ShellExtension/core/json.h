// A small strict JSON (RFC 8259) reader, enough to read the shared settings file without
// pulling a third-party library into explorer.exe. Never throws on bad input.
#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace qic {

struct JsonMember;

struct JsonValue {
    enum class Type { Null, Bool, Number, String, Array, Object };

    Type type = Type::Null;
    bool boolean = false;
    double number = 0;
    std::string string;               // UTF-8, escapes decoded
    std::vector<JsonValue> array;
    std::vector<JsonMember> object;   // in document order

    // Member of an object by key, compared ASCII case-insensitively (System.Text.Json reads
    // that way too). The last duplicate wins. nullptr when absent or not an object.
    const JsonValue* Find(std::string_view key) const;
};

struct JsonMember {
    std::string key;
    JsonValue value;
};

// Parses a whole document. A leading UTF-8 BOM is allowed. Returns nullopt on any syntax
// error, trailing garbage, or nesting deeper than kMaxJsonDepth.
std::optional<JsonValue> ParseJson(std::string_view text);

inline constexpr int kMaxJsonDepth = 64;

}  // namespace qic
