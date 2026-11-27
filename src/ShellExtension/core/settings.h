// The quick-menu part of the settings file shared with the app
// (%LOCALAPPDATA%\QuickImageConverter\settings.json, section "quickMenu").
#pragma once

#include <functional>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace qic {

// Top-level "language" in settings.json: "auto" (Windows display language), "korean" or "english".
enum class UiLanguage { Auto, Korean, English };

// Korean when chosen, or on Auto when Windows shows Korean; English otherwise.
bool UseEnglish(UiLanguage preference, bool windowsUsesKorean);

struct QuickMenuSettings {
    std::vector<std::string> formats;  // output format ids, in menu order
    bool showDetails = true;           // show "자세히..." (open the app)
    bool deleteOriginal = false;       // after a successful quick convert, move the source to the Recycle Bin
    bool moveOriginal = false;         // ...or move it into moveOriginalTo (wins over deleteOriginal)
    std::string moveOriginalTo{};      // UTF-8 full folder path; empty when unset or not a full path
    bool notifySuccess = false;        // quickMenu.notify == "always": also tell the user when everything worked
    bool checkForUpdates = true;       // top-level checkForUpdates: look for a new version about once a day
    bool allFrames = false;            // top-level allFrames: animated GIF/WebP -> every frame (imgconv --all-frames)
    UiLanguage language = UiLanguage::Auto;  // top-level language: menu and message text
};

// True when the settings ask to move converted originals into a usable folder.
bool MovesOriginals(const QuickMenuSettings& settings);

bool operator==(const QuickMenuSettings& left, const QuickMenuSettings& right);

// png, jpg, bmp + details, originals kept: what the menu shows before the user changes anything.
QuickMenuSettings DefaultQuickMenuSettings();

// Reads the "quickMenu" section. Never fails: anything missing or malformed falls back to
// the defaults. Ids are lower-cased, aliases (jpeg, tif) mapped, and ids the engine cannot
// write or that repeat are dropped. An explicit empty list means "no format items".
QuickMenuSettings ParseQuickMenuSettings(std::string_view utf8Json);

// "Name_Version_Arch_ResourceId_PublisherId" -> "Name_PublisherId".
std::optional<std::wstring> PackageFamilyNameFromFullName(std::wstring_view packageFullName);

// Where to look for the settings file, most specific first. When the DLL lives inside an
// MSIX package (...\WindowsApps\<full name>\...), the app's AppData writes are virtualized to
// the package LocalCache, so that copy comes first.
std::vector<std::wstring> SettingsPathCandidates(std::wstring_view localAppData, std::wstring_view modulePath);

// Parses the first candidate that readFile can read, or returns the defaults.
QuickMenuSettings LoadQuickMenuSettings(
    const std::vector<std::wstring>& candidates,
    const std::function<std::optional<std::string>(const std::wstring&)>& readFile);

}  // namespace qic
