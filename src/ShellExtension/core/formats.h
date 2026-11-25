// The single table of image formats the shell extension knows about. It mirrors the engine
// CLI contract (docs/cli.md): ids are the `imgconv convert --to` values.
#pragma once

#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace qic {

struct FormatInfo {
    std::string_view id;                     // "png", "jpg", ... (lower case)
    std::wstring_view label;                 // L"PNG", shown in the menu
    bool canOutput;                          // the engine can write it (`--to <id>`)
    bool lossyOutput;                        // writing it can change pixels (JPG)
    std::vector<std::wstring_view> extensions;  // lower case, with the dot
};

const std::vector<FormatInfo>& AllFormats();

// Output format by exact lower-case id, or nullptr when the id is unknown or input-only.
const FormatInfo* FindOutputFormat(std::string_view id);

// Format id for a file name by its extension (case-insensitive), or nullopt.
std::optional<std::string_view> FormatIdForPath(std::wstring_view path);

// True when the engine can read the file, judged by extension.
bool IsSupportedInput(std::wstring_view path);

// Every readable extension in table order, e.g. L".png", L".jpg", L".jpeg", ...
std::vector<std::wstring> SupportedInputExtensions();

}  // namespace qic
