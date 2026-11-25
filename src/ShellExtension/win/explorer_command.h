// The IExplorerCommand objects Explorer sees: the "빠른 이미지 포맷 변환" root and its items.
#pragma once

#include <windows.h>

namespace qic::win {

// Creates the root command (the class registered under kClsid) and queries riid on it.
HRESULT CreateRootCommand(REFIID riid, void** result);

// Class factory for kClsid.
HRESULT CreateClassFactory(REFIID riid, void** result);

}  // namespace qic::win
