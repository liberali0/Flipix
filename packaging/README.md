# 패키징 / 배포

## 기본 설치 방법: 설치 마법사 (`Flipix-Setup-X.Y.Z.exe`)
파일 하나를 실행하면 설치 경로와 옵션을 고르는 마법사가 뜹니다. 기본은 현재 사용자 전용 설치(관리자 권한 불필요)이고, 마법사에서 모든 사용자 설치도 고를 수 있습니다.

옵션: 우클릭 메뉴 추가(기본 켜짐), 바탕 화면 바로 가기, 명령줄 도구를 PATH에 추가. 제거는 Windows 설정의 앱 목록에서 하며 우클릭 메뉴 등록도 함께 해제됩니다.

**Windows 11 우클릭 메뉴 위치:** 마법사는 설치 폴더를 외부 위치로 하는 작은 메뉴 패키지(`ImageConverter.ContextMenu.msix`, sparse package)를 등록해서 Win11 첫 화면에 메뉴를 띄웁니다. 그 패키지의 서명 인증서를 이 PC가 신뢰해야 해서 설치 중 관리자 확인(UAC)이 한 번 뜹니다. 거절하거나 Windows 10이면 기존 방식(regsvr32)으로 등록되고, Win11에선 "더 많은 옵션 표시" 안에 나타납니다. 두 방식 중 하나만 등록해서 메뉴가 두 번 보이지 않게 합니다. 자체 서명 인증서는 릴리스마다 새로 만들어지므로 업데이트할 때마다 관리자 확인이 다시 뜹니다(시크릿 `SIGN_PFX_BASE64`로 고정 인증서를 쓰면 처음 한 번만). 마법사 설치와 전체 MSIX를 함께 설치하면 메뉴가 두 번 보입니다.

- `ContextMenuManifest.template.xml` – 메뉴 패키지 매니페스트. `build-msix.ps1 -ContextMenuOnly`로 빌드.
- `installer/ContextMenu.ps1` – 설치 폴더에 함께 설치되는 등록 도우미(Trust / IsTrusted / Register / Unregister).

- `installer/ImageConverter.iss` – Inno Setup 스크립트. `build-installer.ps1` – 컴파일(상용 인증서가 있으면 서명).
- 서명하지 않은 설치 파일은 SmartScreen "알 수 없는 게시자" 경고가 뜹니다. 경고를 없애려면 상용 코드서명 인증서(시크릿 `SIGN_PFX_BASE64`/`SIGN_PFX_PASSWORD`)가 필요합니다.

## 선택 설치 방법: MSIX (Windows 11 첫 화면 메뉴)
Win11 최상위 우클릭 메뉴(IExplorerCommand)는 **패키지 신원(MSIX)** 이 있어야 합니다.

### MSIX 구성
- `AppxManifest.template.xml` – 매니페스트 틀. 자리표시자는 빌드 스크립트가 채우고, 우클릭 메뉴 블록은 `shell-extension/AppxManifest.fragment.xml`을 그대로 삽입합니다.
- `build-msix.ps1` – publish → 레이아웃 조립 → makeappx → signtool 서명.
- `New-DevCertificate.ps1` – 자체 서명 인증서 생성(개발/테스트·초기 배포용).
- `Install.ps1` – 릴리스에 함께 올라가는 설치 도우미(인증서 신뢰 등록 + Add-AppxPackage, 관리자 권한 필요).
- `.github/workflows/release.yml` – `v*` 태그 푸시 시 빌드·서명·GitHub Release 업로드.

## 서명 방식 (선택지)
1. **자체 서명(기본값)**: 무료, 사용자가 `Install.ps1`로 인증서를 신뢰 등록해야 설치됨. 지금 바로 가능.
2. **Azure Trusted Signing / 상용 코드서명 인증서**: SmartScreen 경고 감소. 시크릿 `SIGN_PFX_BASE64`, `SIGN_PFX_PASSWORD` 를 등록하면 워크플로가 자동으로 사용(Publisher는 PFX에서 자동 추출). 비밀값이 없으면 실행마다 새 자체 서명 인증서를 만들어, 업데이트 때마다 `Install.ps1`이 필요합니다.
3. **Microsoft Store 제출**: 서명 불필요(스토어가 서명), 가장 매끄러운 설치. 개발자 계정 필요.

Publisher 값은 서명 인증서의 Subject와 **정확히 일치**해야 합니다.

## 로컬에서 빌드
```powershell
dotnet publish src/ImageConverter.App -c Release -r win-x64 --self-contained -o publish
dotnet publish src/ImageConverter.Cli -c Release -r win-x64 --self-contained -o publish
cmake -S src/ShellExtension -B build/shell -A x64; cmake --build build/shell --config Release
Copy-Item build/shell/Release/ImageConverter.ShellExtension.dll publish/
./packaging/New-DevCertificate.ps1 -Password pw   # 처음 한 번
./packaging/build-msix.ps1 -Version 1.0.0.0 -PublishDir publish -PfxPath packaging/out/ImageConverter.pfx -PfxPassword pw
```
메뉴 DLL이 `ImageConverter.exe`/`imgconv.exe`를 패키지 폴더 경로로 직접 실행하므로 패키지 신원 없이 실행됩니다(변환에는 문제 없음).
