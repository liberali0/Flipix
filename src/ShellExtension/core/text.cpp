#include "core/text.h"

#include <cstdint>

namespace qic {
namespace {

constexpr char32_t kReplacement = 0xFFFD;

void AppendUtf8(std::string& out, char32_t code) {
    if (code < 0x80) {
        out += static_cast<char>(code);
    } else if (code < 0x800) {
        out += static_cast<char>(0xC0 | (code >> 6));
        out += static_cast<char>(0x80 | (code & 0x3F));
    } else if (code < 0x10000) {
        out += static_cast<char>(0xE0 | (code >> 12));
        out += static_cast<char>(0x80 | ((code >> 6) & 0x3F));
        out += static_cast<char>(0x80 | (code & 0x3F));
    } else {
        out += static_cast<char>(0xF0 | (code >> 18));
        out += static_cast<char>(0x80 | ((code >> 12) & 0x3F));
        out += static_cast<char>(0x80 | ((code >> 6) & 0x3F));
        out += static_cast<char>(0x80 | (code & 0x3F));
    }
}

void AppendWide(std::wstring& out, char32_t code) {
    if constexpr (sizeof(wchar_t) == 2) {
        if (code >= 0x10000) {
            const char32_t v = code - 0x10000;
            out += static_cast<wchar_t>(0xD800 + (v >> 10));
            out += static_cast<wchar_t>(0xDC00 + (v & 0x3FF));
            return;
        }
    }
    out += static_cast<wchar_t>(code);
}

bool IsSurrogate(char32_t code) { return code >= 0xD800 && code <= 0xDFFF; }

}  // namespace

std::string ToUtf8(std::wstring_view text) {
    std::string out;
    out.reserve(text.size());
    for (std::size_t i = 0; i < text.size(); ++i) {
        char32_t code = static_cast<char32_t>(text[i]);
        if constexpr (sizeof(wchar_t) == 2) {
            code &= 0xFFFF;
            if (code >= 0xD800 && code <= 0xDBFF && i + 1 < text.size()) {
                const char32_t low = static_cast<char32_t>(text[i + 1]) & 0xFFFF;
                if (low >= 0xDC00 && low <= 0xDFFF) {
                    code = 0x10000 + ((code - 0xD800) << 10) + (low - 0xDC00);
                    ++i;
                }
            }
        }
        if (IsSurrogate(code) || code > 0x10FFFF) code = kReplacement;
        AppendUtf8(out, code);
    }
    return out;
}

std::wstring FromUtf8(std::string_view text) {
    std::wstring out;
    out.reserve(text.size());
    std::size_t i = 0;
    while (i < text.size()) {
        const auto lead = static_cast<unsigned char>(text[i]);
        int length = 0;
        char32_t code = 0;
        char32_t minimum = 0;
        if (lead < 0x80) {
            length = 1;
            code = lead;
        } else if (lead >= 0xC2 && lead <= 0xDF) {
            length = 2;
            code = lead & 0x1F;
            minimum = 0x80;
        } else if (lead >= 0xE0 && lead <= 0xEF) {
            length = 3;
            code = lead & 0x0F;
            minimum = 0x800;
        } else if (lead >= 0xF0 && lead <= 0xF4) {
            length = 4;
            code = lead & 0x07;
            minimum = 0x10000;
        } else {
            AppendWide(out, kReplacement);  // stray continuation byte or invalid lead (C0, C1, F5+)
            ++i;
            continue;
        }

        int consumed = 1;
        bool valid = true;
        for (; consumed < length; ++consumed) {
            if (i + consumed >= text.size()) {
                valid = false;
                break;
            }
            const auto next = static_cast<unsigned char>(text[i + consumed]);
            if ((next & 0xC0) != 0x80) {
                valid = false;
                break;
            }
            code = (code << 6) | (next & 0x3F);
        }
        if (valid && (code < minimum || IsSurrogate(code) || code > 0x10FFFF)) valid = false;

        AppendWide(out, valid ? code : kReplacement);
        i += static_cast<std::size_t>(consumed);
    }
    return out;
}

std::wstring LowerAscii(std::wstring_view text) {
    std::wstring out(text);
    for (auto& ch : out) {
        if (ch >= L'A' && ch <= L'Z') ch = static_cast<wchar_t>(ch - L'A' + L'a');
    }
    return out;
}

std::string LowerAscii(std::string_view text) {
    std::string out(text);
    for (auto& ch : out) {
        if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
    }
    return out;
}

std::wstring ExtensionOf(std::wstring_view path) {
    const auto separator = path.find_last_of(L"\\/");
    const std::wstring_view name = separator == std::wstring_view::npos ? path : path.substr(separator + 1);
    const auto dot = name.find_last_of(L'.');
    if (dot == std::wstring_view::npos || dot + 1 == name.size()) return {};
    return LowerAscii(name.substr(dot));
}

}  // namespace qic
