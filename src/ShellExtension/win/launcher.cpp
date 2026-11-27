#include "win/launcher.h"

#include <objbase.h>

#include <memory>
#include <new>

#include "core/command_line.h"
#include "core/menu_model.h"
#include "win/module.h"

namespace qic::win {
namespace {

constexpr DWORD kMaxErrorBytes = 64 * 1024;

class Handle {
public:
    Handle() = default;
    explicit Handle(HANDLE handle) : handle_(handle == INVALID_HANDLE_VALUE ? nullptr : handle) {}
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : handle_(other.Release()) {}
    Handle& operator=(Handle&& other) noexcept {
        if (this != &other) Reset(other.Release());
        return *this;
    }
    ~Handle() { Reset(); }
    HANDLE Get() const { return handle_; }
    HANDLE Release() {
        HANDLE handle = handle_;
        handle_ = nullptr;
        return handle;
    }
    void Reset(HANDLE handle = nullptr) {
        if (handle_ != nullptr) CloseHandle(handle_);
        handle_ = handle == INVALID_HANDLE_VALUE ? nullptr : handle;
    }
    explicit operator bool() const { return handle_ != nullptr; }

private:
    HANDLE handle_ = nullptr;
};

void ShowMessage(const std::wstring& text, UINT icon) {
    MessageBoxW(nullptr, text.c_str(), RootTitle().c_str(), MB_OK | icon | MB_SETFOREGROUND);
}

std::wstring UniqueName(std::wstring_view prefix) {
    GUID guid{};
    wchar_t text[64] = {};
    if (FAILED(CoCreateGuid(&guid)) || StringFromGUID2(guid, text, 64) == 0) {
        return std::wstring(prefix) + std::to_wstring(GetTickCount64()) + L".txt";
    }
    std::wstring name = std::wstring(prefix) + (text + 1);  // drop '{'
    name.pop_back();                                       // drop '}'
    return name + L".txt";
}

std::wstring TempFolder() {
    wchar_t buffer[MAX_PATH + 1] = {};
    const DWORD length = GetTempPathW(MAX_PATH + 1, buffer);
    if (length == 0 || length > MAX_PATH) return {};
    std::wstring folder = std::wstring(buffer, length) + L"QuickImageConverter";
    CreateDirectoryW(folder.c_str(), nullptr);  // fine if it already exists
    return folder;
}

// Removes day-old files a previous launch left behind (the app's response files, crashes).
void DeleteStaleTempFiles(const std::wstring& folder) {
    WIN32_FIND_DATAW entry{};
    HANDLE search = FindFirstFileW((folder + L"\\*.txt").c_str(), &entry);
    if (search == INVALID_HANDLE_VALUE) return;
    FILETIME nowFile{};
    GetSystemTimeAsFileTime(&nowFile);
    const ULARGE_INTEGER now{{nowFile.dwLowDateTime, nowFile.dwHighDateTime}};
    do {
        const ULARGE_INTEGER written{{entry.ftLastWriteTime.dwLowDateTime, entry.ftLastWriteTime.dwHighDateTime}};
        const unsigned long long ageSeconds =
            now.QuadPart > written.QuadPart ? (now.QuadPart - written.QuadPart) / 10000000ULL : 0;
        if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0 && IsStaleTempFile(entry.cFileName, ageSeconds)) {
            DeleteFileW((folder + L"\\" + entry.cFileName).c_str());
        }
    } while (FindNextFileW(search, &entry));
    FindClose(search);
}

bool WriteWholeFile(const std::wstring& path, const std::string& bytes) {
    Handle file(CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr));
    if (!file) return false;
    DWORD written = 0;
    return WriteFile(file.Get(), bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr) &&
           written == bytes.size();
}

std::string ReadFromStart(HANDLE file) {
    std::string text;
    LARGE_INTEGER zero{};
    if (!SetFilePointerEx(file, zero, nullptr, FILE_BEGIN)) return text;
    text.resize(kMaxErrorBytes);
    DWORD read = 0;
    if (!ReadFile(file, text.data(), kMaxErrorBytes, &read, nullptr)) read = 0;
    text.resize(read);
    return text;
}

struct WatchContext {
    Handle process;
    Handle errors;  // stderr capture, delete-on-close; may be empty
    std::wstring responseFile;
    bool reportFailures = false;
    std::wstring followUpExe;
    bool notifySuccess = false;
    bool checkForUpdates = false;
    std::size_t fileCount = 0;
    HMODULE pinnedModule = nullptr;
};

bool StartVisible(std::wstring& commandLine, const std::wstring& workingDirectory, PROCESS_INFORMATION& process);

// The app in background mode: a small notice in the corner, then it exits by itself.
void StartFollowUp(const WatchContext& context, bool succeeded) {
    if (context.followUpExe.empty()) return;
    const std::vector<std::wstring> args =
        FollowUpArguments(context.notifySuccess && succeeded, context.fileCount, context.checkForUpdates);
    if (args.empty()) return;
    if (GetFileAttributesW(context.followUpExe.c_str()) == INVALID_FILE_ATTRIBUTES) return;
    LaunchPlan plan = BuildLaunchPlan(context.followUpExe, args, {}, L"");
    const std::wstring folder = context.followUpExe.substr(0, context.followUpExe.find_last_of(L"\\/"));
    PROCESS_INFORMATION process{};
    if (StartVisible(plan.commandLine, folder, process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }
}

DWORD WINAPI WatchProcess(void* parameter) {
    std::unique_ptr<WatchContext> context(static_cast<WatchContext*>(parameter));
    WaitForSingleObject(context->process.Get(), INFINITE);
    DWORD exitCode = 0;
    if (!GetExitCodeProcess(context->process.Get(), &exitCode)) exitCode = 0;
    if (!context->responseFile.empty()) DeleteFileW(context->responseFile.c_str());
    if (exitCode != 0 && context->reportFailures) {
        const std::string stderrText = context->errors ? ReadFromStart(context->errors.Get()) : std::string();
        ShowMessage(FormatFailureMessage(exitCode, stderrText), MB_ICONWARNING);
    }
    StartFollowUp(*context, exitCode == 0);
    HMODULE module = context->pinnedModule;
    context.reset();
    ReleaseObjectRef();
    FreeLibraryAndExitThread(module, 0);
}

// Inheritable handle to the NUL device, for the child's stdin/stdout.
Handle OpenNul() {
    SECURITY_ATTRIBUTES inherit{sizeof inherit, nullptr, TRUE};
    return Handle(CreateFileW(L"NUL", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, &inherit,
                              OPEN_EXISTING, 0, nullptr));
}

// Inheritable temporary file that disappears once the last handle closes.
Handle CreateErrorCapture(const std::wstring& folder) {
    SECURITY_ATTRIBUTES inherit{sizeof inherit, nullptr, TRUE};
    const std::wstring path = folder + L"\\" + UniqueName(kErrorsFilePrefix);
    return Handle(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE,
                              FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, &inherit, CREATE_NEW,
                              FILE_ATTRIBUTE_TEMPORARY | FILE_FLAG_DELETE_ON_CLOSE, nullptr));
}

// CreateProcessW for a hidden console tool that inherits only the given handles.
bool StartHidden(std::wstring& commandLine, const std::wstring& workingDirectory, HANDLE nul, HANDLE errors,
                 PROCESS_INFORMATION& process) {
    // The handle list must not contain duplicates, or CreateProcess fails.
    HANDLE inherited[2] = {nul, errors};
    const SIZE_T inheritedBytes = (errors != nul ? 2 : 1) * sizeof(HANDLE);
    SIZE_T size = 0;
    InitializeProcThreadAttributeList(nullptr, 1, 0, &size);
    std::unique_ptr<char[]> storage(new (std::nothrow) char[size]);
    if (!storage) return false;
    auto* attributes = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(storage.get());
    if (!InitializeProcThreadAttributeList(attributes, 1, 0, &size)) return false;
    bool started = false;
    if (UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, inheritedBytes,
                                  nullptr, nullptr)) {
        STARTUPINFOEXW startup{};
        startup.StartupInfo.cb = sizeof startup;
        startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        startup.StartupInfo.hStdInput = nul;
        startup.StartupInfo.hStdOutput = nul;
        startup.StartupInfo.hStdError = errors;
        startup.lpAttributeList = attributes;
        started = CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, TRUE,
                                 EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT,
                                 nullptr, workingDirectory.c_str(), &startup.StartupInfo, &process) != FALSE;
    }
    DeleteProcThreadAttributeList(attributes);
    return started;
}

bool StartVisible(std::wstring& commandLine, const std::wstring& workingDirectory, PROCESS_INFORMATION& process) {
    STARTUPINFOW startup{};
    startup.cb = sizeof startup;
    const bool started = CreateProcessW(nullptr, commandLine.data(), nullptr, nullptr, FALSE,
                                        CREATE_UNICODE_ENVIRONMENT, nullptr, workingDirectory.c_str(), &startup,
                                        &process) != FALSE;
    if (started) AllowSetForegroundWindow(process.dwProcessId);
    return started;
}

}  // namespace

HRESULT Launch(const LaunchRequest& request) {
    if (request.files.empty()) return S_OK;

    const DWORD attributes = GetFileAttributesW(request.exePath.c_str());
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0) {
        ShowMessage(L"변환 프로그램을 찾을 수 없어요. 앱을 다시 설치해 주세요.\n\n" + request.exePath, MB_ICONERROR);
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    const std::wstring tempFolder = TempFolder();
    if (!tempFolder.empty()) DeleteStaleTempFiles(tempFolder);
    const std::wstring responseFile = tempFolder.empty() ? std::wstring() : tempFolder + L"\\" + UniqueName(kSelectionFilePrefix);
    LaunchPlan plan = BuildLaunchPlan(request.exePath, request.leadingArgs, request.files, responseFile);
    if (plan.usesResponseFile && (responseFile.empty() || !WriteWholeFile(responseFile, plan.responseFileUtf8))) {
        ShowMessage(L"선택한 파일 목록을 임시 폴더에 저장하지 못했어요.", MB_ICONERROR);
        return E_FAIL;
    }

    const std::wstring exeFolder = request.exePath.substr(0, request.exePath.find_last_of(L"\\/"));
    PROCESS_INFORMATION process{};
    Handle errors;
    bool started = false;
    DWORD error = ERROR_SUCCESS;
    if (request.hidden) {
        Handle nul = OpenNul();
        if (request.reportFailures && !tempFolder.empty()) errors = CreateErrorCapture(tempFolder);
        started = nul && StartHidden(plan.commandLine, exeFolder, nul.Get(), errors ? errors.Get() : nul.Get(), process);
        if (!started) error = GetLastError();
        // The child has its copy; keep ours out of any other process Explorer starts.
        if (errors) SetHandleInformation(errors.Get(), HANDLE_FLAG_INHERIT, 0);
    } else {
        started = StartVisible(plan.commandLine, exeFolder, process);
        if (!started) error = GetLastError();
    }

    if (!started) {
        if (plan.usesResponseFile) DeleteFileW(responseFile.c_str());
        ShowMessage(L"변환 프로그램을 시작하지 못했어요. (오류 " + std::to_wstring(error) + L")\n\n" + request.exePath,
                    MB_ICONERROR);
        return HRESULT_FROM_WIN32(error);
    }
    CloseHandle(process.hThread);

    if (!request.hidden) {
        // The app may hand its file list to an already running window and exit at once, so
        // its response file is left for DeleteStaleTempFiles instead of deleted on exit.
        CloseHandle(process.hProcess);
        return S_OK;
    }

    auto context = std::make_unique<WatchContext>();
    context->process.Reset(process.hProcess);
    context->errors.Reset(errors.Release());
    context->responseFile = plan.usesResponseFile ? responseFile : std::wstring();
    context->reportFailures = request.reportFailures;
    context->followUpExe = request.followUpExe;
    context->notifySuccess = request.notifySuccess;
    context->checkForUpdates = request.checkForUpdates;
    context->fileCount = request.files.size();
    // Keep this DLL loaded until the watcher exits, even if Explorer releases every object.
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, reinterpret_cast<LPCWSTR>(&WatchProcess),
                            &context->pinnedModule)) {
        return S_OK;  // the conversion runs; only the failure report and cleanup are lost
    }
    AddObjectRef();
    HANDLE thread = CreateThread(nullptr, 0, WatchProcess, context.get(), 0, nullptr);
    if (thread == nullptr) {
        ReleaseObjectRef();
        FreeLibrary(context->pinnedModule);
        return S_OK;
    }
    context.release();
    CloseHandle(thread);
    return S_OK;
}

}  // namespace qic::win
