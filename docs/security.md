# 보안: 변조·제3자 개입 방지

이 문서는 "내가 받은 프로그램이 원본 그대로인가", "설치된 뒤에 누가 끼어들 수 있는가"를 어떻게 막는지 정리합니다.

## 1. 내려받은 파일 확인하기

릴리스마다 다음이 함께 올라갑니다.

| 확인 방법 | 무엇을 보장하나 | 하는 법 |
|---|---|---|
| `SHA256SUMS.txt` | 파일이 한 바이트도 바뀌지 않았음 | PowerShell: `Get-FileHash .\Flipix-Setup-X.Y.Z.exe` 결과가 목록의 값과 같으면 정상 |
| 빌드 출처 증명(attestation) | 이 저장소의 그 커밋에서 GitHub 서버가 직접 빌드했음(개인 PC에서 만든 파일이 아님) | `gh attestation verify .\Flipix-Setup-X.Y.Z.exe --repo liberali0/Flipix` |

해시 목록만으로는 "목록까지 같이 바뀐 경우"를 막지 못하지만, 출처 증명은 GitHub가 서명하므로 누가 릴리스 파일과 목록을 함께 바꿔도 검증에 실패합니다.

이미 나간 릴리스의 파일은 워크플로가 **덮어쓰지 않습니다**(파일이 있으면 업로드를 거부). 고치려면 새 버전을 내야 합니다.

## 2. 설치할 때

- **관리자 권한으로는 고정된 명령만 실행합니다.** Win11 첫 화면 메뉴용 인증서를 신뢰 등록할 때 설치 마법사는 설치 폴더의 스크립트나 인증서 파일을 쓰지 않고, 빌드 때 인증서 내용을 넣어 둔 명령을 실행합니다. 기본 설치 폴더(`%LOCALAPPDATA%\Programs`)는 사용자 권한으로 바꿀 수 있는 곳이라, 예전 방식이면 악성 프로그램이 그 파일을 바꿔치기해서 관리자 권한을 얻거나 자기 인증서를 신뢰시킬 수 있었습니다.
- `Install.ps1`(MSIX 설치)은 `SHA256SUMS.txt`가 있으면 해시를 먼저 확인하고, MSIX가 함께 받은 인증서로 서명됐는지 확인한 다음에만 인증서를 신뢰 등록합니다.
- 서명 인증서는(비밀값을 따로 등록하지 않으면) 릴리스마다 새로 만들고 빌드가 끝나면 개인 키를 지웁니다. 그래서 신뢰 등록된 인증서로 다른 사람이 새 패키지에 서명할 수 없습니다.

## 3. 실행 중

- 우클릭 메뉴 DLL은 자기와 같은 폴더의 `imgconv.exe`/`Flipix.exe`만 따옴표로 감싼 전체 경로로 실행합니다(PATH나 현재 폴더에서 찾지 않음).
- DLL이 필요로 하는 Windows DLL은 System32에서만 불러옵니다(`/DEPENDENTLOADFLAG:0x800`, DLL 바꿔치기 방지).
- DLL은 ASLR, DEP, Control Flow Guard, CET 그림자 스택을 켜고 빌드하며 CI가 매번 켜져 있는지 확인합니다(`packaging/Test-PeHardening.ps1`).
- `settings.json`에서 읽는 값은 허용 목록(png, jpg, bmp, tiff, webp, avif, true/false)만 받습니다. 누가 파일을 고쳐도 변환 프로그램에 다른 명령줄 옵션을 끼워 넣을 수 없습니다.
- 엔진은 파일 확장자가 아니라 파일 앞부분(매직 바이트)으로 형식을 판별하고, 그 형식의 디코더만 사용합니다.

## 4. 빌드·배포 과정(공급망)

- GitHub Actions는 태그 대신 **커밋 해시로 고정**합니다. 남이 태그를 옮겨도 다른 코드가 실행되지 않습니다. Dependabot이 업데이트를 PR로 알려 줍니다.
- 워크플로 토큰은 기본 읽기 전용이고, 릴리스를 만드는 작업만 쓰기 권한을 받습니다. 저장소를 받을 때 토큰을 디스크에 남기지 않습니다(`persist-credentials: false`).
- 설치 마법사에 들어가는 한국어 언어 파일은 Inno Setup 6.7.4 태그 커밋에서만 받고 SHA-256을 확인합니다.
- NuGet 패키지는 nuget.org에서만 받습니다(`nuget.config`, 같은 이름의 가짜 패키지 방지).
- 네이티브 코드 테스트는 메모리 오류 검사기(ASan/UBSan)를 켜고 돌립니다.

## 5. 남은 한계 (무료 자체 서명 인증서)

- 자체 서명 인증서는 "이 파일을 누가 만들었는지"를 Windows에게 증명하지 못합니다. 그래서 SmartScreen의 "알 수 없는 게시자" 경고는 그대로 뜹니다. 위의 해시·출처 증명이 그 역할을 대신합니다.
- 경고 없이 "게시자: ○○"로 표시하려면 상용 코드서명 인증서(또는 Azure Trusted Signing)나 Microsoft Store 등록이 필요합니다. 인증서를 받으면 시크릿 `SIGN_PFX_BASE64`, `SIGN_PFX_PASSWORD`만 등록하면 됩니다.
- 이미 PC에 관리자 권한을 가진 악성 프로그램이 있다면 어떤 프로그램도 완전히 막을 수 없습니다. 이 작업은 "다운로드 중·배포 과정·일반 사용자 권한" 단계의 개입을 막는 것입니다.

## 6. 저장소 주인이 GitHub에서 켜 두면 좋은 설정

1. 계정 2단계 인증(Settings → Password and authentication).
2. `main` 브랜치 보호(Settings → Branches 또는 Rules): PR 없이 직접 푸시 금지, CI 통과 필수, 강제 푸시 금지.
3. 태그 보호 규칙(Settings → Rules → New tag ruleset, 대상 `v*`): 태그 삭제·이동 금지.
4. Settings → Actions → General → Workflow permissions를 "Read repository contents"로.
