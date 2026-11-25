// What the "빠른 이미지 포맷 변환" submenu contains for given settings and selection.
#pragma once

#include <iosfwd>
#include <string>
#include <string_view>
#include <vector>

#include "core/settings.h"

namespace qic {

enum class MenuItemKind { Convert, Separator, Details };

struct MenuItem {
    MenuItemKind kind;
    std::string formatId;  // Convert only
    std::wstring title;
};

bool operator==(const MenuItem& left, const MenuItem& right);
std::ostream& operator<<(std::ostream& stream, const MenuItem& item);

// L"빠른 이미지 포맷 변환"
std::wstring RootTitle();

// Every item the settings ask for, ignoring the selection: one "to X" per format, then a
// separator and "자세히..." when details are on. Explorer asks for subcommands without
// telling which files are selected, so this is what EnumSubCommands returns.
std::vector<MenuItem> ConfiguredSubmenu(const QuickMenuSettings& settings);

// Whether an item from ConfiguredSubmenu should be shown for this selection.
bool IsItemVisible(const MenuItem& item, const QuickMenuSettings& settings, const std::vector<std::wstring>& paths);

// ConfiguredSubmenu filtered by IsItemVisible. Empty means: hide the whole menu.
std::vector<MenuItem> VisibleSubmenu(const QuickMenuSettings& settings, const std::vector<std::wstring>& paths);

// The selected files the engine can read, in selection order.
std::vector<std::wstring> SupportedFiles(const std::vector<std::wstring>& paths);

// The CLI arguments before the files for "to <formatId>": convert --to <id>, then
// --move-source-to <folder> or --delete-source when the settings ask for it.
std::vector<std::wstring> ConvertArguments(std::string_view formatId, const QuickMenuSettings& settings);

// The files "to <formatId>" should convert: readable and not already in that format.
std::vector<std::wstring> FilesToConvert(std::string_view formatId, const std::vector<std::wstring>& paths);

}  // namespace qic
