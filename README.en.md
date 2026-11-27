# Flipix

[한국어](README.md) | **English**

Right-click an image in Windows File Explorer to convert it to another format right away. Flipix converts losslessly where possible and also reads iPhone photos (HEIC).

```
Right-click an image ▸ Quick image format convert ▸ to PNG
                                                   to JPG (lossy)
                                                   to BMP
                                                   More...
```

## Features

- **Right-click convert**: converts many files at once. Results are saved next to the originals, and originals are never overwritten.
- **Lossless**: PNG, TIFF, BMP, WebP and AVIF keep every pixel value exactly.
- **Flipix Workshop** (app): batch conversion of whole folders, with a queue, pause and retry.
- **Command-line tool `imgconv`**: for scripts and automation.
- **Language**: Korean and English. Follows the Windows display language, and can be changed in Workshop's Advanced settings.

| Format | Read | Write |
|---|---|---|
| PNG, TIFF, BMP, WebP, AVIF | ✓ | ✓ (lossless) |
| JPG | ✓ | ✓ (lossy format, quality 100) |
| HEIC/HEIF | ✓ | ✗ |
| GIF | ✓ (first frame or all frames) | ✗ |

## Install

1. Download `Flipix-Setup-X.Y.Z.exe` from [Releases](../../releases/latest) and run it. No administrator rights are needed.
2. Right-click an image to see the menu. On Windows 11 it may be under "Show more options".

Supported: Windows 10/11 (x64), Windows 11 ARM64.

If Windows shows an "Unknown publisher" warning, click `More info` → `Run anyway`. Flipix is a free program from an individual developer without a commercial code-signing certificate, which causes this warning. To check that your download is the original file, see [docs/security.md](docs/security.md) (Korean).

To uninstall, use Windows Settings → Apps.

## Command-line examples

```powershell
imgconv convert --to png IMG_0001.HEIC
imgconv convert --to avif --verify --out-dir out *.png
```

See [docs/cli.md](docs/cli.md) for all options.

## Build from source

You need the .NET 10 SDK. To build the right-click menu (C++) too, you also need the Visual Studio 2022 C++ tools and CMake.

```powershell
dotnet test ImageConverter.sln -c Release
dotnet publish src/ImageConverter.App -c Release -r win-x64 --self-contained -o publish
```

Building the installer and MSIX is described in [packaging/README.md](packaging/README.md), and the right-click menu in [src/ShellExtension/README.md](src/ShellExtension/README.md) (both in Korean).

## Contact

Use the "Contact" button in the app or reach out on [X @Liberali_0](https://x.com/Liberali_0). You can also report bugs in [Issues](../../issues).
