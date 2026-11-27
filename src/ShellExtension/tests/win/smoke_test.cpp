// End-to-end check of the real DLL through the same COM calls Explorer makes, using real
// shell items for files in a temporary folder. Runs on Windows only (CI: windows-latest).
// The working directory is the DLL's folder, where fake_cli.exe was copied as imgconv.exe and
// ImageConverter.exe.
#include <windows.h>

#include <shlobj.h>
#include <shobjidl.h>
#include <wrl/client.h>

#include <fstream>
#include <string>
#include <vector>

#include "core/registration.h"
#include "core/text.h"
#include "test_harness.h"
#include "win/module.h"

using Microsoft::WRL::ComPtr;

namespace {

struct Environment {
    std::wstring dllPath;
    std::wstring root;  // temporary folder for this run
    std::wstring localAppData;
    std::wstring files;
    std::wstring log;
    HMODULE dll = nullptr;
};

std::wstring CurrentDirectory() {
    wchar_t buffer[MAX_PATH] = {};
    const DWORD length = GetCurrentDirectoryW(MAX_PATH, buffer);
    return std::wstring(buffer, length);
}

void WriteFileUtf8(const std::wstring& path, const std::string& contents) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return;
    DWORD written = 0;
    WriteFile(file, contents.data(), static_cast<DWORD>(contents.size()), &written, nullptr);
    CloseHandle(file);
}

Environment& Env() {
    static Environment env = [] {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        Environment e;
        e.dllPath = CurrentDirectory() + L"\\ImageConverter.ShellExtension.dll";
        wchar_t temp[MAX_PATH] = {};
        GetTempPathW(MAX_PATH, temp);
        // %TEMP% may be an 8.3 short path (C:\Users\RUNNER~1\...); shell items report long paths.
        wchar_t longTemp[MAX_PATH] = {};
        const DWORD longLength = GetLongPathNameW(temp, longTemp, MAX_PATH);
        const std::wstring tempFolder = longLength > 0 && longLength < MAX_PATH ? longTemp : temp;
        e.root = tempFolder + L"qic-smoke-" + std::to_wstring(GetCurrentProcessId());
        e.localAppData = e.root + L"\\LocalAppData";
        e.files = e.root + L"\\사진 폴더";
        e.log = e.root + L"\\fake-cli.log";
        CreateDirectoryW(e.root.c_str(), nullptr);
        CreateDirectoryW(e.localAppData.c_str(), nullptr);
        CreateDirectoryW(e.files.c_str(), nullptr);
        SetEnvironmentVariableW(L"LOCALAPPDATA", e.localAppData.c_str());
        SetEnvironmentVariableW(L"QIC_FAKE_LOG", e.log.c_str());
        e.dll = LoadLibraryW(e.dllPath.c_str());
        return e;
    }();
    return env;
}

std::wstring MakeFile(const std::wstring& name) {
    const std::wstring path = Env().files + L"\\" + name;
    WriteFileUtf8(path, "x");
    return path;
}

ComPtr<IShellItemArray> ShellItems(const std::vector<std::wstring>& paths) {
    std::vector<PIDLIST_ABSOLUTE> pidls;
    for (const auto& path : paths) {
        ComPtr<IShellItem> item;
        PIDLIST_ABSOLUTE pidl = nullptr;
        if (SUCCEEDED(SHCreateItemFromParsingName(path.c_str(), nullptr, IID_PPV_ARGS(&item))) &&
            SUCCEEDED(SHGetIDListFromObject(item.Get(), &pidl))) {
            pidls.push_back(pidl);
        }
    }
    ComPtr<IShellItemArray> array;
    if (!pidls.empty()) {
        SHCreateShellItemArrayFromIDLists(static_cast<UINT>(pidls.size()),
                                          const_cast<PCIDLIST_ABSOLUTE_ARRAY>(pidls.data()), &array);
    }
    for (auto pidl : pidls) CoTaskMemFree(pidl);
    return array;
}

ComPtr<IExplorerCommand> CreateRoot() {
    using GetClassObject = HRESULT(STDAPICALLTYPE*)(REFCLSID, REFIID, void**);
    auto getClassObject = reinterpret_cast<GetClassObject>(
        reinterpret_cast<void*>(GetProcAddress(Env().dll, "DllGetClassObject")));
    ComPtr<IClassFactory> factory;
    ComPtr<IExplorerCommand> root;
    if (getClassObject != nullptr && SUCCEEDED(getClassObject(qic::win::kClsid, IID_PPV_ARGS(&factory)))) {
        factory->CreateInstance(nullptr, IID_PPV_ARGS(&root));
    }
    return root;
}

std::wstring Title(IExplorerCommand* command) {
    PWSTR title = nullptr;
    std::wstring result = SUCCEEDED(command->GetTitle(nullptr, &title)) && title != nullptr ? title : L"<none>";
    CoTaskMemFree(title);
    return result;
}

std::vector<ComPtr<IExplorerCommand>> SubCommands(IExplorerCommand* root) {
    std::vector<ComPtr<IExplorerCommand>> commands;
    ComPtr<IEnumExplorerCommand> enumerator;
    if (FAILED(root->EnumSubCommands(&enumerator))) return commands;
    for (;;) {
        ComPtr<IExplorerCommand> command;
        ULONG fetched = 0;
        if (enumerator->Next(1, &command, &fetched) != S_OK || fetched != 1) break;
        commands.push_back(command);
    }
    return commands;
}

ComPtr<IExplorerCommand> SubCommandTitled(IExplorerCommand* root, const std::wstring& title) {
    for (auto& command : SubCommands(root)) {
        if (Title(command.Get()) == title) return command;
    }
    return nullptr;
}

EXPCMDSTATE State(IExplorerCommand* command, IShellItemArray* items) {
    EXPCMDSTATE state = ECS_DISABLED;
    command->GetState(items, TRUE, &state);
    return state;
}

// Lines the fake CLI logged for the next launch; waits up to 15 seconds.
std::vector<std::string> TakeLog() {
    for (int i = 0; i < 150; ++i) {
        if (GetFileAttributesW(Env().log.c_str()) != INVALID_FILE_ATTRIBUTES) break;
        Sleep(100);
    }
    std::vector<std::string> lines;
    std::ifstream file(Env().log.c_str(), std::ios::binary);
    for (std::string line; std::getline(file, line);) lines.push_back(line);
    file.close();
    DeleteFileW(Env().log.c_str());
    return lines;
}

void ClearSettings() {
    DeleteFileW((Env().localAppData + L"\\QuickImageConverter\\settings.json").c_str());
    // A fresh update stamp: no follow-up app start after a conversion, so the log holds only the CLI call.
    CreateDirectoryW((Env().localAppData + L"\\QuickImageConverter").c_str(), nullptr);
    WriteFileUtf8(Env().localAppData + L"\\QuickImageConverter\\update-check.stamp", "now");
}

void WriteSettings(const std::string& json) {
    CreateDirectoryW((Env().localAppData + L"\\QuickImageConverter").c_str(), nullptr);
    WriteFileUtf8(Env().localAppData + L"\\QuickImageConverter\\settings.json", json);
}

std::wstring ReadRegistryString(const std::wstring& subKey, const wchar_t* name) {
    wchar_t buffer[1024] = {};
    DWORD size = sizeof buffer;
    if (RegGetValueW(HKEY_CURRENT_USER, subKey.c_str(), name, RRF_RT_REG_SZ, nullptr, buffer, &size) != ERROR_SUCCESS) {
        return L"<missing>";
    }
    return buffer;
}

}  // namespace

TEST(Smoke_DllLoadsAndClsidMatchesCore) {
    CHECK(Env().dll != nullptr);
    wchar_t text[64] = {};
    StringFromGUID2(qic::win::kClsid, text, 64);
    CHECK_EQ(std::wstring(text), std::wstring(qic::kShellExtensionClsid));
}

TEST(Smoke_RootTitleFlagsAndVisibility) {
    ClearSettings();
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    CHECK_EQ(Title(root.Get()), std::wstring(L"빠른 이미지 포맷 변환"));
    EXPCMDFLAGS flags = ECF_DEFAULT;
    root->GetFlags(&flags);
    CHECK_EQ(static_cast<int>(flags), static_cast<int>(ECF_HASSUBCOMMANDS));

    const auto images = ShellItems({MakeFile(L"사진 1.png"), MakeFile(L"b.jpg")});
    const auto text = ShellItems({MakeFile(L"notes.txt")});
    CHECK_EQ(static_cast<int>(State(root.Get(), images.Get())), static_cast<int>(ECS_ENABLED));
    CHECK_EQ(static_cast<int>(State(root.Get(), text.Get())), static_cast<int>(ECS_HIDDEN));
    CHECK_EQ(static_cast<int>(State(root.Get(), nullptr)), static_cast<int>(ECS_HIDDEN));
}

TEST(Smoke_DefaultSubmenu) {
    ClearSettings();
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    std::vector<std::wstring> titles;
    for (auto& command : SubCommands(root.Get())) {
        EXPCMDFLAGS flags = ECF_DEFAULT;
        command->GetFlags(&flags);
        titles.push_back((flags & ECF_ISSEPARATOR) != 0 ? L"---" : Title(command.Get()));
    }
    CHECK_EQ(titles, (std::vector<std::wstring>{L"to PNG", L"to JPG (손실 있음)", L"to BMP", L"---", L"자세히..."}));
}

TEST(Smoke_SubmenuFollowsSettingsFile) {
    WriteSettings(R"({"quickMenu":{"formats":["webp","PNG","heic"],"showDetails":false},"conflict":"rename"})");
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    std::vector<std::wstring> titles;
    for (auto& command : SubCommands(root.Get())) titles.push_back(Title(command.Get()));
    CHECK_EQ(titles, (std::vector<std::wstring>{L"to WEBP", L"to PNG"}));
    ClearSettings();
}

TEST(Smoke_HidesTargetTheSelectionAlreadyHas) {
    ClearSettings();
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    auto toPng = SubCommandTitled(root.Get(), L"to PNG");
    REQUIRE(toPng != nullptr);
    const auto pngs = ShellItems({MakeFile(L"only.png")});
    const auto mixed = ShellItems({MakeFile(L"only.png"), MakeFile(L"photo.HEIC")});
    CHECK_EQ(static_cast<int>(State(toPng.Get(), pngs.Get())), static_cast<int>(ECS_HIDDEN));
    CHECK_EQ(static_cast<int>(State(toPng.Get(), mixed.Get())), static_cast<int>(ECS_ENABLED));
}

TEST(Smoke_ConvertStartsCliWithReadableFilesOnly) {
    ClearSettings();
    DeleteFileW(Env().log.c_str());
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    auto toBmp = SubCommandTitled(root.Get(), L"to BMP");
    REQUIRE(toBmp != nullptr);
    const std::wstring png = MakeFile(L"사진 1.png");
    const std::wstring heic = MakeFile(L"IMG_0001.HEIC");
    const std::wstring bmp = MakeFile(L"already.bmp");
    const auto items = ShellItems({png, MakeFile(L"notes.txt"), heic, bmp});
    CHECK_EQ(toBmp->Invoke(items.Get(), nullptr), S_OK);
    CHECK_EQ(TakeLog(), (std::vector<std::string>{"imgconv.exe", "convert", "--to", "bmp", qic::ToUtf8(png),
                                                  qic::ToUtf8(heic)}));
}

TEST(Smoke_DetailsOpensAppWithSelectedImages) {
    ClearSettings();
    DeleteFileW(Env().log.c_str());
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    auto details = SubCommandTitled(root.Get(), L"자세히...");
    REQUIRE(details != nullptr);
    const std::wstring jpg = MakeFile(L"b.jpg");
    const auto items = ShellItems({MakeFile(L"notes.txt"), jpg});
    CHECK_EQ(details->Invoke(items.Get(), nullptr), S_OK);
    CHECK_EQ(TakeLog(), (std::vector<std::string>{"ImageConverter.exe", qic::ToUtf8(jpg)}));
}

TEST(Smoke_LargeSelectionGoesThroughResponseFile) {
    ClearSettings();
    DeleteFileW(Env().log.c_str());
    auto root = CreateRoot();
    REQUIRE(root != nullptr);
    auto toPng = SubCommandTitled(root.Get(), L"to PNG");
    REQUIRE(toPng != nullptr);
    std::vector<std::wstring> paths;
    for (int i = 0; i < 600; ++i) {
        paths.push_back(MakeFile(L"여름 휴가 사진 모음 2026 - 아주 긴 파일 이름 " + std::to_wstring(i) + L".jpg"));
    }
    const auto items = ShellItems(paths);
    CHECK_EQ(toPng->Invoke(items.Get(), nullptr), S_OK);
    const auto log = TakeLog();
    REQUIRE(log.size() == 5 + 600);
    CHECK_EQ(log[3], std::string("png"));
    CHECK(log[4].rfind("@", 0) == 0);
    for (int i = 0; i < 600; ++i) CHECK_EQ(log[5 + i], "@> " + qic::ToUtf8(paths[i]));
    // The watcher deletes the response file after the CLI exits.
    const std::wstring responseFile = qic::FromUtf8(log[4].substr(1));
    for (int i = 0; i < 100 && GetFileAttributesW(responseFile.c_str()) != INVALID_FILE_ATTRIBUTES; ++i) Sleep(100);
    CHECK(GetFileAttributesW(responseFile.c_str()) == INVALID_FILE_ATTRIBUTES);
}

TEST(Smoke_RegisterAndUnregisterClassicMenu) {
    using Entry = HRESULT(STDAPICALLTYPE*)();
    auto registerServer = reinterpret_cast<Entry>(reinterpret_cast<void*>(GetProcAddress(Env().dll, "DllRegisterServer")));
    auto unregisterServer =
        reinterpret_cast<Entry>(reinterpret_cast<void*>(GetProcAddress(Env().dll, "DllUnregisterServer")));
    REQUIRE(registerServer != nullptr && unregisterServer != nullptr);

    const std::wstring clsid(qic::kShellExtensionClsid);
    const std::wstring verbKey = L"Software\\Classes\\SystemFileAssociations\\.heic\\shell\\QuickImageConvert";
    const std::wstring serverKey = L"Software\\Classes\\CLSID\\" + clsid + L"\\InprocServer32";

    CHECK_EQ(registerServer(), S_OK);
    CHECK_EQ(ReadRegistryString(verbKey, L"ExplorerCommandHandler"), clsid);
    CHECK_EQ(ReadRegistryString(verbKey, L"MultiSelectModel"), std::wstring(L"Player"));
    CHECK_EQ(ReadRegistryString(verbKey, L"MUIVerb"), std::wstring(L"빠른 이미지 포맷 변환"));
    CHECK_EQ(ReadRegistryString(serverKey, nullptr), Env().dllPath);
    CHECK_EQ(ReadRegistryString(serverKey, L"ThreadingModel"), std::wstring(L"Apartment"));

    CHECK_EQ(unregisterServer(), S_OK);
    CHECK_EQ(ReadRegistryString(verbKey, L"ExplorerCommandHandler"), std::wstring(L"<missing>"));
    CHECK_EQ(ReadRegistryString(serverKey, nullptr), std::wstring(L"<missing>"));
}
