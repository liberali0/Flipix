#include "core/json.h"

#include <charconv>
#include <system_error>

#include "core/text.h"

namespace qic {

const JsonValue* JsonValue::Find(std::string_view key) const {
    if (type != Type::Object) return nullptr;
    const std::string wanted = LowerAscii(key);
    const JsonValue* found = nullptr;
    for (const auto& member : object) {
        if (LowerAscii(std::string_view(member.key)) == wanted) found = &member.value;
    }
    return found;
}

namespace {

class Parser {
public:
    explicit Parser(std::string_view text) : text_(text) {}

    std::optional<JsonValue> ParseDocument() {
        static constexpr std::string_view kBom = "\xEF\xBB\xBF";
        if (text_.substr(0, kBom.size()) == kBom) pos_ = kBom.size();
        JsonValue value;
        SkipWhitespace();
        if (!ParseValue(value, 0)) return std::nullopt;
        SkipWhitespace();
        if (pos_ != text_.size()) return std::nullopt;
        return value;
    }

private:
    bool AtEnd() const { return pos_ >= text_.size(); }
    char Peek() const { return text_[pos_]; }

    void SkipWhitespace() {
        while (!AtEnd() && (Peek() == ' ' || Peek() == '\t' || Peek() == '\n' || Peek() == '\r')) ++pos_;
    }

    bool Consume(char expected) {
        if (AtEnd() || Peek() != expected) return false;
        ++pos_;
        return true;
    }

    bool ConsumeLiteral(std::string_view literal) {
        if (text_.substr(pos_, literal.size()) != literal) return false;
        pos_ += literal.size();
        return true;
    }

    bool ParseValue(JsonValue& out, int depth) {
        if (AtEnd()) return false;
        switch (Peek()) {
            case '{': return ParseObject(out, depth + 1);
            case '[': return ParseArray(out, depth + 1);
            case '"':
                out.type = JsonValue::Type::String;
                return ParseString(out.string);
            case 't':
                out.type = JsonValue::Type::Bool;
                out.boolean = true;
                return ConsumeLiteral("true");
            case 'f':
                out.type = JsonValue::Type::Bool;
                out.boolean = false;
                return ConsumeLiteral("false");
            case 'n':
                out.type = JsonValue::Type::Null;
                return ConsumeLiteral("null");
            default: return ParseNumber(out);
        }
    }

    bool ParseObject(JsonValue& out, int depth) {
        if (depth > kMaxJsonDepth) return false;
        out.type = JsonValue::Type::Object;
        ++pos_;  // '{'
        SkipWhitespace();
        if (Consume('}')) return true;
        for (;;) {
            SkipWhitespace();
            JsonMember member;
            if (AtEnd() || Peek() != '"' || !ParseString(member.key)) return false;
            SkipWhitespace();
            if (!Consume(':')) return false;
            SkipWhitespace();
            if (!ParseValue(member.value, depth)) return false;
            out.object.push_back(std::move(member));
            SkipWhitespace();
            if (Consume('}')) return true;
            if (!Consume(',')) return false;
        }
    }

    bool ParseArray(JsonValue& out, int depth) {
        if (depth > kMaxJsonDepth) return false;
        out.type = JsonValue::Type::Array;
        ++pos_;  // '['
        SkipWhitespace();
        if (Consume(']')) return true;
        for (;;) {
            SkipWhitespace();
            JsonValue element;
            if (!ParseValue(element, depth)) return false;
            out.array.push_back(std::move(element));
            SkipWhitespace();
            if (Consume(']')) return true;
            if (!Consume(',')) return false;
        }
    }

    bool ParseHex4(char32_t& out) {
        if (pos_ + 4 > text_.size()) return false;
        out = 0;
        for (int i = 0; i < 4; ++i) {
            const char ch = text_[pos_++];
            out <<= 4;
            if (ch >= '0' && ch <= '9') {
                out |= static_cast<char32_t>(ch - '0');
            } else if (ch >= 'a' && ch <= 'f') {
                out |= static_cast<char32_t>(ch - 'a' + 10);
            } else if (ch >= 'A' && ch <= 'F') {
                out |= static_cast<char32_t>(ch - 'A' + 10);
            } else {
                return false;
            }
        }
        return true;
    }

    bool ParseUnicodeEscape(std::string& out) {
        char32_t code = 0;
        if (!ParseHex4(code)) return false;
        if (code >= 0xDC00 && code <= 0xDFFF) return false;  // lone low surrogate
        if (code >= 0xD800 && code <= 0xDBFF) {
            char32_t low = 0;
            if (!ConsumeLiteral("\\u") || !ParseHex4(low) || low < 0xDC00 || low > 0xDFFF) return false;
            code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
        }
        std::wstring wide;
        if constexpr (sizeof(wchar_t) == 2) {
            if (code >= 0x10000) {
                wide += static_cast<wchar_t>(0xD800 + ((code - 0x10000) >> 10));
                wide += static_cast<wchar_t>(0xDC00 + ((code - 0x10000) & 0x3FF));
            } else {
                wide += static_cast<wchar_t>(code);
            }
        } else {
            wide += static_cast<wchar_t>(code);
        }
        out += ToUtf8(wide);
        return true;
    }

    bool ParseString(std::string& out) {
        ++pos_;  // opening quote
        while (!AtEnd()) {
            const char ch = text_[pos_++];
            if (ch == '"') return true;
            if (static_cast<unsigned char>(ch) < 0x20) return false;  // raw control characters
            if (ch != '\\') {
                out += ch;
                continue;
            }
            if (AtEnd()) return false;
            switch (text_[pos_++]) {
                case '"': out += '"'; break;
                case '\\': out += '\\'; break;
                case '/': out += '/'; break;
                case 'b': out += '\b'; break;
                case 'f': out += '\f'; break;
                case 'n': out += '\n'; break;
                case 'r': out += '\r'; break;
                case 't': out += '\t'; break;
                case 'u':
                    if (!ParseUnicodeEscape(out)) return false;
                    break;
                default: return false;
            }
        }
        return false;  // unterminated
    }

    static bool IsDigit(char ch) { return ch >= '0' && ch <= '9'; }

    bool ParseNumber(JsonValue& out) {
        const std::size_t start = pos_;
        Consume('-');
        if (AtEnd() || !IsDigit(Peek())) return false;
        if (Peek() == '0') {
            ++pos_;
        } else {
            while (!AtEnd() && IsDigit(Peek())) ++pos_;
        }
        if (Consume('.')) {
            if (AtEnd() || !IsDigit(Peek())) return false;
            while (!AtEnd() && IsDigit(Peek())) ++pos_;
        }
        if (!AtEnd() && (Peek() == 'e' || Peek() == 'E')) {
            ++pos_;
            if (!AtEnd() && (Peek() == '+' || Peek() == '-')) ++pos_;
            if (AtEnd() || !IsDigit(Peek())) return false;
            while (!AtEnd() && IsDigit(Peek())) ++pos_;
        }
        out.type = JsonValue::Type::Number;
        const char* first = text_.data() + start;
        const char* last = text_.data() + pos_;
        const auto result = std::from_chars(first, last, out.number);
        // Out-of-range values are still valid JSON; they just are not exact.
        return result.ptr == last || result.ec == std::errc::result_out_of_range;
    }

    std::string_view text_;
    std::size_t pos_ = 0;
};

}  // namespace

std::optional<JsonValue> ParseJson(std::string_view text) { return Parser(text).ParseDocument(); }

}  // namespace qic
