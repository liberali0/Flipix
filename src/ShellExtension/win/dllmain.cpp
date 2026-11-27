// DLL entry points. Explorer (or regsvr32) is the only caller.
#include <windows.h>

#include <olectl.h>
#include <shlobj.h>

#include "core/registration.h"
#include "win/explorer_command.h"
#include "win/module.h"

using namespace qic;

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        win::SetModule(module);
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clsid, REFIID riid, LPVOID* result) {
    if (result == nullptr) return E_POINTER;
    *result = nullptr;
    if (clsid != win::kClsid) return CLASS_E_CLASSNOTAVAILABLE;
    return win::CreateClassFactory(riid, result);
}

STDAPI DllCanUnloadNow() { return win::CanUnload() ? S_OK : S_FALSE; }

// Per-user registration for the classic context menu of an unpackaged install
// (`regsvr32 Flipix.ShellExtension.dll`). MSIX installs use the manifest instead.
STDAPI DllUnregisterServer() {
    HRESULT result = S_OK;
    try {
        for (const auto& key : LegacyRegistrationKeys(kShellExtensionClsid)) {
            const LSTATUS status = RegDeleteTreeW(HKEY_CURRENT_USER, key.c_str());
            if (status != ERROR_SUCCESS && status != ERROR_FILE_NOT_FOUND) result = HRESULT_FROM_WIN32(status);
        }
    } catch (...) {
        return E_OUTOFMEMORY;
    }
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return result == S_OK ? S_OK : SELFREG_E_CLASS;
}

STDAPI DllRegisterServer() {
    try {
        const std::wstring dllPath = win::ModulePath();
        if (dllPath.empty()) return SELFREG_E_CLASS;
        for (const auto& value : LegacyRegistrationValues(kShellExtensionClsid, dllPath, win::EnglishUi(win::LoadSettings()))) {
            const LSTATUS status = RegSetKeyValueW(
                HKEY_CURRENT_USER, value.subKey.c_str(), value.name.empty() ? nullptr : value.name.c_str(), REG_SZ,
                value.data.c_str(), static_cast<DWORD>((value.data.size() + 1) * sizeof(wchar_t)));
            if (status != ERROR_SUCCESS) {
                DllUnregisterServer();
                return SELFREG_E_CLASS;
            }
        }
    } catch (...) {
        DllUnregisterServer();
        return E_OUTOFMEMORY;
    }
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return S_OK;
}
