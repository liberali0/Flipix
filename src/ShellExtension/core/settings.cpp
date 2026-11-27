#include "core/settings.h"

#include <algorithm>

#include "core/formats.h"
#include "core/json.h"
#include "core/text.h"

namespace qic {
namespace {

// The settings folder was renamed to Flipix in v0.1.5. The app copies an old file over the first time it
// starts; until then the old file still applies.
constexpr std::wstring_view kSettingsRelativePaths[] = {L"Flipix\\settings.json", L"QuickImageConverter\\settings.json"};

std::optional<std::string> NormalizeFormatId(std::string_view raw) {
    std::string id = LowerAscii(raw);
    if (id == "jpeg") id = "jpg";
    if (id == "tif") id = "tiff";
    if (FindOutputFormat(id) == nullptr) return std::nullopt;
    return id;
}

// "C:\..." or "\\server\share\...": the CLI runs with an unrelated working folder, so a
// relative path would land somewhere the user did not choose.
bool IsFullWindowsPath(std::string_view path) {
    if (path.size() >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) &&
        path[1] == ':' && (path[2] == '\\' || path[2] == '/')) {
        return true;
    }
    return path.size() > 2 && path[0] == '\\' && path[1] == '\\';
}

std::wstring JoinPath(std::wstring_view folder, std::wstring_view relative) {
    std::wstring path(folder);
    if (!path.empty() && path.back() != L'\\' && path.back() != L'/') path += L'\\';
    path += relative;
    return path;
}

}  // namespace

bool operator==(const QuickMenuSettings& left, const QuickMenuSettings& right) {
    return left.formats == right.formats && left.showDetails == right.showDetails &&
           left.deleteOriginal == right.deleteOriginal && left.moveOriginal == right.moveOriginal &&
           left.moveOriginalTo == right.moveOriginalTo && left.notifySuccess == right.notifySuccess &&
           left.checkForUpdates == right.checkForUpdates && left.allFrames == right.allFrames &&
           left.language == right.language;
}

bool UseEnglish(UiLanguage preference, bool windowsUsesKorean) {
    switch (preference) {
        case UiLanguage::Korean: return false;
        case UiLanguage::English: return true;
        case UiLanguage::Auto: break;
    }
    return !windowsUsesKorean;
}

bool MovesOriginals(const QuickMenuSettings& settings) {
    return settings.moveOriginal && !settings.moveOriginalTo.empty();
}

QuickMenuSettings DefaultQuickMenuSettings() { return QuickMenuSettings{{"png", "jpg", "bmp"}, true, false, false, {}}; }

QuickMenuSettings ParseQuickMenuSettings(std::string_view utf8Json) {
    QuickMenuSettings settings = DefaultQuickMenuSettings();
    const std::optional<JsonValue> document = ParseJson(utf8Json);
    if (!document) return settings;
    if (const JsonValue* updates = document->Find("checkForUpdates");
        updates != nullptr && updates->type == JsonValue::Type::Bool) {
        settings.checkForUpdates = updates->boolean;
    }
    if (const JsonValue* frames = document->Find("allFrames");
        frames != nullptr && frames->type == JsonValue::Type::Bool) {
        settings.allFrames = frames->boolean;
    }
    if (const JsonValue* language = document->Find("language");
        language != nullptr && language->type == JsonValue::Type::String) {
        const std::string value = LowerAscii(language->string);
        if (value == "korean") settings.language = UiLanguage::Korean;
        if (value == "english") settings.language = UiLanguage::English;
    }
    const JsonValue* section = document->Find("quickMenu");
    if (section == nullptr || section->type != JsonValue::Type::Object) return settings;

    if (const JsonValue* formats = section->Find("formats");
        formats != nullptr && formats->type == JsonValue::Type::Array) {
        settings.formats.clear();
        for (const auto& entry : formats->array) {
            if (entry.type != JsonValue::Type::String) continue;
            const auto id = NormalizeFormatId(entry.string);
            if (!id) continue;
            if (std::find(settings.formats.begin(), settings.formats.end(), *id) == settings.formats.end()) {
                settings.formats.push_back(*id);
            }
        }
    }

    if (const JsonValue* details = section->Find("showDetails");
        details != nullptr && details->type == JsonValue::Type::Bool) {
        settings.showDetails = details->boolean;
    }

    // Only an explicit true deletes anything; every other value keeps the originals.
    if (const JsonValue* deleteOriginal = section->Find("deleteOriginal");
        deleteOriginal != nullptr && deleteOriginal->type == JsonValue::Type::Bool) {
        settings.deleteOriginal = deleteOriginal->boolean;
    }

    if (const JsonValue* move = section->Find("moveOriginal");
        move != nullptr && move->type == JsonValue::Type::Bool) {
        settings.moveOriginal = move->boolean;
    }
    if (const JsonValue* folder = section->Find("moveOriginalTo");
        folder != nullptr && folder->type == JsonValue::Type::String && IsFullWindowsPath(folder->string) &&
        folder->string.find('\0') == std::string::npos) {
        settings.moveOriginalTo = folder->string;
    }
    // "failures" (default) or "always"; anything else keeps the quiet default.
    if (const JsonValue* notify = section->Find("notify");
        notify != nullptr && notify->type == JsonValue::Type::String) {
        settings.notifySuccess = LowerAscii(notify->string) == "always";
    }
    return settings;
}

std::optional<std::wstring> PackageFamilyNameFromFullName(std::wstring_view packageFullName) {
    std::vector<std::wstring_view> parts;
    std::size_t start = 0;
    for (;;) {
        const auto underscore = packageFullName.find(L'_', start);
        parts.push_back(packageFullName.substr(start, underscore - start));
        if (underscore == std::wstring_view::npos) break;
        start = underscore + 1;
    }
    if (parts.size() != 5 || parts[0].empty() || parts[4].empty()) return std::nullopt;
    return std::wstring(parts[0]) + L"_" + std::wstring(parts[4]);
}

std::vector<std::wstring> SettingsPathCandidates(std::wstring_view localAppData, std::wstring_view modulePath) {
    std::vector<std::wstring> candidates;
    if (localAppData.empty()) return candidates;

    static constexpr std::wstring_view kMarker = L"\\windowsapps\\";
    const std::wstring lowered = LowerAscii(modulePath);
    const auto marker = lowered.find(kMarker);
    if (marker != std::wstring::npos) {
        const std::size_t nameStart = marker + kMarker.size();
        const std::size_t nameEnd = modulePath.find_first_of(L"\\/", nameStart);
        if (nameEnd != std::wstring_view::npos) {
            if (const auto family = PackageFamilyNameFromFullName(modulePath.substr(nameStart, nameEnd - nameStart))) {
                for (const auto relative : kSettingsRelativePaths) {
                    candidates.push_back(JoinPath(localAppData, L"Packages\\" + *family + L"\\LocalCache\\Local\\" +
                                                                    std::wstring(relative)));
                }
            }
        }
    }
    for (const auto relative : kSettingsRelativePaths) candidates.push_back(JoinPath(localAppData, relative));
    return candidates;
}

QuickMenuSettings LoadQuickMenuSettings(
    const std::vector<std::wstring>& candidates,
    const std::function<std::optional<std::string>(const std::wstring&)>& readFile) {
    for (const auto& path : candidates) {
        if (const auto contents = readFile(path)) return ParseQuickMenuSettings(*contents);
    }
    return DefaultQuickMenuSettings();
}

}  // namespace qic
