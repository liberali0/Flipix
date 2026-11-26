# Flipix

Windows 탐색기에서 이미지를 우클릭해 바로 다른 포맷으로 바꾸는 변환기입니다. 가능한 경우 무손실로 변환하고, 아이폰 사진(HEIC)도 읽습니다.

```
이미지 우클릭 ▸ 빠른 이미지 포맷 변환 ▸ to PNG
                                       to JPG (손실 있음)
                                       to BMP
                                       자세히...
```

## 기능

- **우클릭 변환**: 여러 파일을 한 번에 변환합니다. 결과는 원본 옆에 저장되고 원본은 덮어쓰지 않습니다.
- **무손실**: PNG, TIFF, BMP, WebP, AVIF는 픽셀 값을 그대로 저장합니다.
- **Flipix Workshop**(앱): 폴더 단위 대량 변환, 큐, 일시정지와 재시도.
- **명령줄 도구 `imgconv`**: 스크립트와 자동화용.

| 포맷 | 읽기 | 쓰기 |
|---|---|---|
| PNG, TIFF, BMP, WebP, AVIF | ✓ | ✓ (무손실) |
| JPG | ✓ | ✓ (손실 포맷, 품질 100) |
| HEIC/HEIF | ✓ | ✗ |

## 설치

1. [Releases](../../releases/latest)에서 `Flipix-Setup-X.Y.Z.exe`를 받아 실행합니다. 관리자 권한은 필요 없습니다.
2. 이미지를 우클릭해 메뉴를 확인합니다. Windows 11에서는 "더 많은 옵션 표시" 안에 있을 수 있습니다.

Windows가 "알 수 없는 게시자" 경고를 띄우면 `추가 정보` → `실행`을 누르세요. 개인이 배포하는 무료 프로그램이라 상용 서명 인증서가 없어서 뜨는 경고입니다. 받은 파일이 원본인지 확인하는 방법은 [docs/security.md](docs/security.md)에 있습니다.

제거는 Windows 설정 → 앱에서 합니다.

## 명령줄 예시

```powershell
imgconv convert --to png IMG_0001.HEIC
imgconv convert --to avif --verify --out-dir out *.png
```

전체 옵션은 [docs/cli.md](docs/cli.md)를 보세요.

## 소스에서 빌드

.NET 10 SDK가 필요합니다. 우클릭 메뉴(C++)까지 빌드하려면 Visual Studio 2022 C++ 도구와 CMake도 필요합니다.

```powershell
dotnet test ImageConverter.sln -c Release
dotnet publish src/ImageConverter.App -c Release -r win-x64 --self-contained -o publish
```

설치 파일과 MSIX 만들기는 [packaging/README.md](packaging/README.md), 우클릭 메뉴는 [src/ShellExtension/README.md](src/ShellExtension/README.md)에 정리되어 있습니다.

## 문의

앱의 "문의하기" 버튼이나 [X @Liberali_0](https://x.com/Liberali_0)으로 연락 주세요. 버그는 [Issues](../../issues)에 남겨 주셔도 됩니다.
