#include "win/explorer_command.h"

#include <shlobj.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <wrl/client.h>

#include <memory>
#include <new>
#include <string>
#include <utility>
#include <vector>

#include "core/menu_model.h"
#include "win/launcher.h"
#include "win/module.h"

using Microsoft::WRL::ComPtr;

namespace qic::win {
namespace {

// Runs a COM method body; C++ exceptions must never cross into Explorer.
template <typename Body>
HRESULT Guarded(Body&& body) noexcept {
    try {
        return body();
    } catch (const std::bad_alloc&) {
        return E_OUTOFMEMORY;
    } catch (...) {
        return E_UNEXPECTED;
    }
}

std::vector<std::wstring> SelectedPaths(IShellItemArray* items) {
    std::vector<std::wstring> paths;
    if (items == nullptr) return paths;
    DWORD count = 0;
    if (FAILED(items->GetCount(&count))) return paths;
    paths.reserve(count);
    for (DWORD i = 0; i < count; ++i) {
        ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(i, &item))) continue;
        PWSTR path = nullptr;
        // Items without a file system path (zip contents, phones, libraries roots) are skipped.
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path != nullptr) paths.emplace_back(path);
        CoTaskMemFree(path);
    }
    return paths;
}

HRESULT DuplicateString(const std::wstring& text, PWSTR* result) {
    if (result == nullptr) return E_POINTER;
    return SHStrDupW(text.c_str(), result);
}

// Reference counting and the IExplorerCommand defaults shared by every command.
class CommandBase : public IExplorerCommand {
public:
    CommandBase() { AddObjectRef(); }
    CommandBase(const CommandBase&) = delete;
    CommandBase& operator=(const CommandBase&) = delete;
    virtual ~CommandBase() { ReleaseObjectRef(); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override {
        if (result == nullptr) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IExplorerCommand)) {
            *result = static_cast<IExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        *result = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs_)); }
    IFACEMETHODIMP_(ULONG) Release() override {
        const LONG refs = InterlockedDecrement(&refs_);
        if (refs == 0) delete this;
        return static_cast<ULONG>(refs);
    }

    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        if (icon != nullptr) *icon = nullptr;
        return E_NOTIMPL;
    }
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override {
        if (tip != nullptr) *tip = nullptr;
        return E_NOTIMPL;
    }
    IFACEMETHODIMP GetCanonicalName(GUID* name) override {
        if (name == nullptr) return E_POINTER;
        *name = GUID_NULL;
        return S_OK;
    }
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override {
        if (commands != nullptr) *commands = nullptr;
        return E_NOTIMPL;
    }

private:
    volatile LONG refs_ = 1;
};

using SharedSettings = std::shared_ptr<const QuickMenuSettings>;

// One "to PNG" / separator / "자세히..." entry.
class ItemCommand final : public CommandBase {
public:
    ItemCommand(SharedSettings settings, MenuItem item) : settings_(std::move(settings)), item_(std::move(item)) {}

    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override {
        return Guarded([&] { return DuplicateString(item_.title, title); });
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        if (state == nullptr) return E_POINTER;
        return Guarded([&] {
            *state = IsItemVisible(item_, *settings_, SelectedPaths(items)) ? ECS_ENABLED : ECS_HIDDEN;
            return S_OK;
        });
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override {
        if (flags == nullptr) return E_POINTER;
        *flags = item_.kind == MenuItemKind::Separator ? ECF_ISSEPARATOR : ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override {
        return Guarded([&]() -> HRESULT {
            const std::vector<std::wstring> paths = SelectedPaths(items);
            LaunchRequest request;
            switch (item_.kind) {
                case MenuItemKind::Convert:
                    request.exePath = PathInModuleDirectory(kCliExeName);
                    request.leadingArgs = ConvertArguments(item_.formatId, *settings_);
                    request.files = FilesToConvert(item_.formatId, paths);
                    request.hidden = true;
                    request.reportFailures = true;
                    request.followUpExe = PathInModuleDirectory(kAppExeName);
                    request.notifySuccess = settings_->notifySuccess;
                    request.checkForUpdates = settings_->checkForUpdates && UpdateCheckDue();
                    break;
                case MenuItemKind::Details:
                    request.exePath = PathInModuleDirectory(kAppExeName);
                    request.files = SupportedFiles(paths);
                    break;
                case MenuItemKind::Separator: return S_OK;
            }
            return Launch(request);
        });
    }

private:
    SharedSettings settings_;
    MenuItem item_;
};

class CommandEnumerator final : public IEnumExplorerCommand {
public:
    explicit CommandEnumerator(std::vector<ComPtr<IExplorerCommand>> commands) : commands_(std::move(commands)) {
        AddObjectRef();
    }
    CommandEnumerator(const CommandEnumerator&) = delete;
    CommandEnumerator& operator=(const CommandEnumerator&) = delete;
    ~CommandEnumerator() { ReleaseObjectRef(); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override {
        if (result == nullptr) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IEnumExplorerCommand)) {
            *result = static_cast<IEnumExplorerCommand*>(this);
            AddRef();
            return S_OK;
        }
        *result = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs_)); }
    IFACEMETHODIMP_(ULONG) Release() override {
        const LONG refs = InterlockedDecrement(&refs_);
        if (refs == 0) delete this;
        return static_cast<ULONG>(refs);
    }

    IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override {
        if (commands == nullptr || (count > 1 && fetched == nullptr)) return E_POINTER;
        ULONG done = 0;
        while (done < count && next_ < commands_.size()) {
            commands_[next_].CopyTo(&commands[done]);
            ++next_;
            ++done;
        }
        if (fetched != nullptr) *fetched = done;
        return done == count ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Skip(ULONG count) override {
        const std::size_t left = commands_.size() - next_;
        next_ += count < left ? count : left;
        return count <= left ? S_OK : S_FALSE;
    }
    IFACEMETHODIMP Reset() override {
        next_ = 0;
        return S_OK;
    }
    IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override {
        if (clone == nullptr) return E_POINTER;
        *clone = nullptr;
        return Guarded([&] {
            auto* copy = new CommandEnumerator(commands_);
            copy->next_ = next_;
            *clone = copy;
            return S_OK;
        });
    }

private:
    volatile LONG refs_ = 1;
    std::vector<ComPtr<IExplorerCommand>> commands_;
    std::size_t next_ = 0;
};

// "빠른 이미지 포맷 변환", the entry registered with Explorer.
class RootCommand final : public CommandBase {
public:
    IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) override {
        return Guarded([&] { return DuplicateString(RootTitle(), title); });
    }

    // Flipix icon next to the menu entry (the app's first icon resource).
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override {
        return Guarded([&] { return DuplicateString(PathInModuleDirectory(kAppExeName) + L",0", icon); });
    }

    IFACEMETHODIMP GetCanonicalName(GUID* name) override {
        if (name == nullptr) return E_POINTER;
        *name = kClsid;
        return S_OK;
    }

    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override {
        if (state == nullptr) return E_POINTER;
        return Guarded([&] {
            *state = VisibleSubmenu(LoadSettings(), SelectedPaths(items)).empty() ? ECS_HIDDEN : ECS_ENABLED;
            return S_OK;
        });
    }

    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override {
        if (flags == nullptr) return E_POINTER;
        *flags = ECF_HASSUBCOMMANDS;
        return S_OK;
    }

    IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return E_NOTIMPL; }

    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override {
        if (commands == nullptr) return E_POINTER;
        *commands = nullptr;
        return Guarded([&] {
            auto settings = std::make_shared<const QuickMenuSettings>(LoadSettings());
            std::vector<ComPtr<IExplorerCommand>> items;
            for (auto& item : ConfiguredSubmenu(*settings)) {
                ComPtr<IExplorerCommand> command;
                command.Attach(new ItemCommand(settings, std::move(item)));
                items.push_back(std::move(command));
            }
            *commands = new CommandEnumerator(std::move(items));
            return S_OK;
        });
    }
};

class ClassFactory final : public IClassFactory {
public:
    ClassFactory() { AddObjectRef(); }
    ClassFactory(const ClassFactory&) = delete;
    ClassFactory& operator=(const ClassFactory&) = delete;
    ~ClassFactory() { ReleaseObjectRef(); }

    IFACEMETHODIMP QueryInterface(REFIID riid, void** result) override {
        if (result == nullptr) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IClassFactory)) {
            *result = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        *result = nullptr;
        return E_NOINTERFACE;
    }
    IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs_)); }
    IFACEMETHODIMP_(ULONG) Release() override {
        const LONG refs = InterlockedDecrement(&refs_);
        if (refs == 0) delete this;
        return static_cast<ULONG>(refs);
    }

    IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID riid, void** result) override {
        if (result == nullptr) return E_POINTER;
        *result = nullptr;
        if (outer != nullptr) return CLASS_E_NOAGGREGATION;
        return CreateRootCommand(riid, result);
    }
    IFACEMETHODIMP LockServer(BOOL lock) override {
        win::LockServer(lock != FALSE);
        return S_OK;
    }

private:
    volatile LONG refs_ = 1;
};

}  // namespace

HRESULT CreateRootCommand(REFIID riid, void** result) {
    if (result == nullptr) return E_POINTER;
    *result = nullptr;
    return Guarded([&] {
        ComPtr<IExplorerCommand> command;
        command.Attach(new RootCommand());
        return command.CopyTo(riid, result);
    });
}

HRESULT CreateClassFactory(REFIID riid, void** result) {
    if (result == nullptr) return E_POINTER;
    *result = nullptr;
    return Guarded([&] {
        ComPtr<IClassFactory> factory;
        factory.Attach(new ClassFactory());
        return factory.CopyTo(riid, result);
    });
}

}  // namespace qic::win
