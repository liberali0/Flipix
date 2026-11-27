#include "core/menu_model.h"

#include <ostream>

#include "core/formats.h"
#include "core/text.h"

namespace qic {
namespace {

bool NeedsConversion(std::string_view formatId, const std::wstring& path) {
    const auto source = FormatIdForPath(path);
    return source.has_value() && *source != formatId;
}

bool AnySupported(const std::vector<std::wstring>& paths) {
    for (const auto& path : paths) {
        if (IsSupportedInput(path)) return true;
    }
    return false;
}

bool AnyNeedsConversion(std::string_view formatId, const std::vector<std::wstring>& paths) {
    for (const auto& path : paths) {
        if (NeedsConversion(formatId, path)) return true;
    }
    return false;
}

}  // namespace

bool operator==(const MenuItem& left, const MenuItem& right) {
    return left.kind == right.kind && left.formatId == right.formatId && left.title == right.title;
}

std::ostream& operator<<(std::ostream& stream, const MenuItem& item) {
    switch (item.kind) {
        case MenuItemKind::Convert: return stream << "Convert(" << item.formatId << ", " << ToUtf8(item.title) << ")";
        case MenuItemKind::Separator: return stream << "Separator";
        case MenuItemKind::Details: return stream << "Details(" << ToUtf8(item.title) << ")";
    }
    return stream;
}

std::wstring RootTitle(bool english) { return english ? L"Quick image format convert" : L"빠른 이미지 포맷 변환"; }

std::vector<MenuItem> ConfiguredSubmenu(const QuickMenuSettings& settings, bool english) {
    std::vector<MenuItem> items;
    for (const auto& id : settings.formats) {
        const FormatInfo* format = FindOutputFormat(id);
        if (format == nullptr) continue;
        std::wstring title = L"to " + std::wstring(format->label);
        if (format->lossyOutput) title += english ? L" (lossy)" : L" (손실 있음)";
        items.push_back({MenuItemKind::Convert, id, std::move(title)});
    }
    if (settings.showDetails) {
        if (!items.empty()) items.push_back({MenuItemKind::Separator, "", L""});
        items.push_back({MenuItemKind::Details, "", english ? L"More..." : L"자세히..."});
    }
    return items;
}

bool IsItemVisible(const MenuItem& item, const QuickMenuSettings& settings, const std::vector<std::wstring>& paths) {
    if (!AnySupported(paths)) return false;
    switch (item.kind) {
        case MenuItemKind::Convert: return AnyNeedsConversion(item.formatId, paths);
        case MenuItemKind::Details: return settings.showDetails;
        case MenuItemKind::Separator:
            if (!settings.showDetails) return false;
            for (const auto& other : ConfiguredSubmenu(settings)) {
                if (other.kind == MenuItemKind::Convert && AnyNeedsConversion(other.formatId, paths)) return true;
            }
            return false;
    }
    return false;
}

std::vector<MenuItem> VisibleSubmenu(const QuickMenuSettings& settings, const std::vector<std::wstring>& paths,
                                     bool english) {
    std::vector<MenuItem> visible;
    for (auto& item : ConfiguredSubmenu(settings, english)) {
        if (IsItemVisible(item, settings, paths)) visible.push_back(std::move(item));
    }
    return visible;
}

std::vector<std::wstring> SupportedFiles(const std::vector<std::wstring>& paths) {
    std::vector<std::wstring> supported;
    for (const auto& path : paths) {
        if (IsSupportedInput(path)) supported.push_back(path);
    }
    return supported;
}

std::vector<std::wstring> FilesToConvert(std::string_view formatId, const std::vector<std::wstring>& paths) {
    std::vector<std::wstring> files;
    for (const auto& path : paths) {
        if (NeedsConversion(formatId, path)) files.push_back(path);
    }
    return files;
}

std::vector<std::wstring> ConvertArguments(std::string_view formatId, const QuickMenuSettings& settings) {
    std::vector<std::wstring> arguments = {L"convert", L"--to", std::wstring(formatId.begin(), formatId.end())};
    if (settings.allFrames) arguments.push_back(L"--all-frames");
    if (MovesOriginals(settings)) {
        arguments.push_back(L"--move-source-to");
        arguments.push_back(FromUtf8(settings.moveOriginalTo));
    } else if (settings.deleteOriginal) {
        arguments.push_back(L"--delete-source");
    }
    return arguments;
}

}  // namespace qic
