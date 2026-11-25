// Text helpers shared by every core module. Portable C++17: no Windows headers.
//
// std::wstring holds UTF-16 on Windows (where the DLL runs) and UTF-32 on Linux (where the
// unit tests also run); every function here handles both.
#pragma once

#include <string>
#include <string_view>

namespace qic {

// UTF-16/UTF-32 -> UTF-8. Unpaired surrogates become U+FFFD.
std::string ToUtf8(std::wstring_view text);

// UTF-8 -> UTF-16/UTF-32. Malformed sequences become U+FFFD.
std::wstring FromUtf8(std::string_view text);

// Lower-cases A-Z only, so results never depend on the user's locale.
std::wstring LowerAscii(std::wstring_view text);
std::string LowerAscii(std::string_view text);

// Lower-cased extension of the last path component including the dot (L".png"),
// or an empty string when the name has no extension. Accepts '\\' and '/' separators.
std::wstring ExtensionOf(std::wstring_view path);

}  // namespace qic
