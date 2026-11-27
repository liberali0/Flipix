// Starting the engine CLI or the app from a menu click without blocking Explorer.
#pragma once

#include <windows.h>

#include <string>
#include <vector>

namespace qic::win {

struct LaunchRequest {
    std::wstring exePath;
    std::vector<std::wstring> leadingArgs;  // e.g. convert --to png
    std::vector<std::wstring> files;
    bool hidden = false;          // console tool: no window, stderr captured, exit watched
    bool reportFailures = false;  // hidden only: message box when the process exits non-zero
    // Hidden only: once the CLI exits, start this app in background mode (see qic::FollowUpArguments)
    // to say that every file converted and/or to look for a new version. Empty: never.
    std::wstring followUpExe;
    bool notifySuccess = false;
    bool checkForUpdates = false;  // already decided to be due
    bool english = false;          // message box language
};

// Starts the process and returns immediately. For hidden tools a background thread waits for
// the exit, reports failures, and deletes the temporary response file. Shows a message box
// itself when the executable is missing or cannot be started.
HRESULT Launch(const LaunchRequest& request);

}  // namespace qic::win
