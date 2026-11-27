# 우클릭 빠른 변환 메뉴 (ImageConverter.ShellExtension)

탐색기에서 이미지 파일을 우클릭하면 **빠른 이미지 포맷 변환** 하위 메뉴를 보여 주는 네이티브 셸 확장입니다.

```
빠른 이미지 포맷 변환 ▸  to PNG
                        to JPG (손실 있음)
                        to BMP
                        ──────────
                        자세히...
```

- 하위 메뉴에 뜨는 포맷과 순서, `자세히...` 표시 여부는 앱의 설정 화면에서 고릅니다.
- 선택한 파일이 모두 이미 그 포맷이면 그 항목은 숨겨집니다(PNG만 골랐으면 `to PNG` 없음). 이미지가 아닌 파일만 고르면 메뉴 전체가 숨겨집니다.
- `to …`는 엔진 CLI(`imgconv.exe`)를 창 없이 실행하고, 결과 파일은 원본 옆에 생깁니다. 실패한 파일이 있으면 CLI의 `ERROR` 줄을 메시지 창으로 보여 줍니다.
- `자세히...`는 Flipix Workshop 앱(`ImageConverter.exe`)을 선택한 파일과 함께 엽니다.

## 구조

| 폴더 | 내용 |
|---|---|
| `core/` | Windows 헤더 없는 C++17. 포맷 표, 설정 읽기, 메뉴 구성, 명령줄 만들기, 등록 키 목록. 모든 판단 로직이 여기 있고 Linux·Windows 양쪽에서 단위 테스트합니다. |
| `win/` | `IExplorerCommand` COM 클래스, 프로세스 실행, DLL 진입점. |
| `tests/` | `core` 단위 테스트와 Windows 스모크 테스트(실제 DLL을 실제 셸 항목으로 호출). |

C#이 아니라 C++인 이유: 탐색기(explorer.exe) 안에 .NET 런타임을 올리는 셸 확장은 Microsoft가 권하지 않습니다. DLL은 메뉴만 그리고 변환은 별도 프로세스(엔진 CLI)가 하므로, 변환 중 문제가 생겨도 탐색기는 영향을 받지 않습니다.

## 빌드와 테스트

```powershell
# Windows (Visual Studio 2022)
cmake -S src/ShellExtension -B build/shell -A x64
cmake --build build/shell --config Release
ctest --test-dir build/shell -C Release --output-on-failure
# 결과: build/shell/Release/ImageConverter.ShellExtension.dll
# ARM64 PC용: -A ARM64 (ARM64 탐색기는 ARM64 DLL만 불러옵니다. 앱과 CLI는 x64 에뮬레이션으로 실행)
```

```bash
# Linux: core 단위 테스트만
cmake -S src/ShellExtension -B build/shell && cmake --build build/shell && ctest --test-dir build/shell
# Linux에서 Windows용 교차 빌드 확인 (MinGW-w64)
cmake -S src/ShellExtension -B build/shell-mingw -DCMAKE_TOOLCHAIN_FILE=src/ShellExtension/cmake/mingw-w64-x86_64.cmake
cmake --build build/shell-mingw
```

CI: `.github/workflows/shell-extension.yml` (Linux 단위 테스트 + Windows MSVC 빌드·스모크 테스트, DLL을 아티팩트로 업로드).

## 등록

DLL은 `imgconv.exe`, `ImageConverter.exe`와 **같은 폴더**에 설치해야 합니다(자기 폴더에서 두 exe를 찾습니다).

| 설치 방식 | 메뉴 위치 | 등록 방법 |
|---|---|---|
| MSIX (Win11 첫 화면 메뉴용 선택 설치) | Windows 11 첫 화면 메뉴 + 추가 옵션 표시 | `packaging/shell-extension/AppxManifest.fragment.xml` 블록을 패키지 매니페스트에 넣습니다. **regsvr32는 쓰지 않습니다**(항목이 두 번 보임). |
| 설치 마법사(기본), 압축 풀기/개발용 | Windows 10 메뉴, Windows 11 "추가 옵션 표시" | `regsvr32 ImageConverter.ShellExtension.dll` (관리자 권한 불필요, HKCU에만 기록). 제거: `regsvr32 /u ImageConverter.ShellExtension.dll`. |

Windows 11 첫 화면 메뉴는 패키지 신원이 있는 앱만 쓸 수 있어서 MSIX 설치에서만 나타납니다.

## 다른 구성 요소와의 계약

| 항목 | 값 |
|---|---|
| CLSID | `{233119CD-5628-4066-AB18-D528BC05C51F}` (`core/registration.h`, `win/module.h`) |
| 동사 Id | `QuickImageConvert` |
| 메뉴가 뜨는 확장자 | `.png .jpg .jpeg .jpe .jfif .bmp .dib .tif .tiff .webp .avif .heic .heif` (`core/formats.cpp`) |
| 변환 | `imgconv.exe convert --to <png\|jpg\|bmp\|tiff\|webp\|avif> <파일...>` 또는 명령줄이 32,000자를 넘으면 `@<목록 파일>`(UTF-8, CRLF, 한 줄 한 경로). 종료 코드 0이 아니면 stderr를 보여 줌. 사양: `docs/cli.md`. |
| 자세히 | `ImageConverter.exe <파일...>` 또는 `@<목록 파일>` |
| 설정 | `%LOCALAPPDATA%\QuickImageConverter\settings.json`의 `quickMenu.formats`(순서 = 메뉴 순서), `quickMenu.showDetails`, `quickMenu.deleteOriginal`(true일 때만 `--delete-source`를 붙여 성공한 원본을 휴지통으로). MSIX에서는 `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local\QuickImageConverter\settings.json`을 먼저 읽습니다. 파일이 없거나 깨지면 기본값(PNG, JPG, BMP, 자세히). |

HEIC는 엔진이 읽을 수만 있어서 메뉴에 `to HEIC`는 없습니다. 설정에 `heic`가 있어도 무시합니다.

## 실제 PC에서 확인할 것

CI 스모크 테스트는 탐색기와 같은 COM 호출을 하지만 탐색기 화면 자체는 띄우지 않습니다. 설치 후 다음을 눈으로 확인해 주세요.

1. Windows 11에서 PNG 파일 우클릭 → 첫 화면에 "빠른 이미지 포맷 변환" ▸ `to JPG (손실 있음)`, `to BMP`, 구분선, `자세히...`.
2. 파일 600개 이상 선택 후 `to PNG` → 모두 변환됨.
3. 앱 설정에서 포맷을 바꾸면 다음 우클릭부터 바로 반영됨.
4. 앱 설정에서 원본을 "휴지통으로 보내기"로 바꾼 뒤 `to PNG` → 새 PNG가 생기고 원본은 휴지통에 있음. "그대로 남기기"면 원본이 남음.
