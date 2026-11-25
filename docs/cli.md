# `imgconv` command line

`imgconv.exe` is the command-line front end of the conversion engine. The Explorer right-click menu
launches it hidden; scripts and the batch app may use it too (the app can also reference
`ImageConverter.Engine` directly).

## Commands

```
imgconv convert --to <format> [options] <file | @listfile>...
imgconv formats [--json]
imgconv detect [--json] <file>...
imgconv --version
```

### `convert`

| Option | Default | Meaning |
|---|---|---|
| `--to <format>` | required | Target format id: `png`, `jpg` (`jpeg`), `bmp`, `tiff` (`tif`), `webp`, `avif`. |
| `--out-dir <dir>` | next to each source | Write all outputs into this folder (created if missing). |
| `--if-exists <mode>` | `rename` | `rename` → `name (1).ext`, `overwrite`, `skip`, `fail`. The source file is never overwritten. |
| `--jpeg-quality <1-100>` | `100` | JPEG only. JPEG is always written 4:4:4 (no chroma subsampling). |
| `--no-metadata` | off | Drop EXIF/XMP/IPTC/ICC instead of copying them. |
| `--verify` | off | Re-decode every lossless output and compare all pixels with the source. |
| `--delete-source` | off | After a file converts successfully, move its source to the Recycle Bin. Turns on `--verify`, so a lossless output is pixel-checked first. Skipped, failed and cancelled files are never touched. |
| `--move-source-to <dir>` | off | After a file converts successfully, move its source into this folder (created if missing). Cannot be combined with `--delete-source`. |
| `--parallel <n>` | half the CPU cores | Files converted at the same time. |
| `--json` | off | Print one JSON object per line on stdout (see below) instead of text. |

Inputs:

* Any number of file paths.
* `@<path>` reads more paths from a response file: UTF-8 (BOM optional), one path per line,
  blank lines ignored, no quoting needed. Use this for large Explorer selections that would
  overflow the 32K command line. Response files and plain paths can be mixed.

Output naming: `<source folder or --out-dir>\<source base name>.<default extension>`, for example
`IMG_0001.HEIC` → `IMG_0001.png`. A file that is already in the target format is reported as
`SKIPPED` (no copy is made). Inputs that would get the same output name (`a.jpg` and `a.tif` to PNG)
get `a.png` and `a (1).png`, and no output ever replaces another input of the same run, even with
`--if-exists overwrite`. Default extensions: `.png`, `.jpg`, `.bmp`, `.tif`, `.webp`, `.avif`.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Every file converted or was skipped (already in the target format, or `--if-exists skip`). |
| 1 | At least one file failed. The others were still converted. |
| 2 | Usage error (unknown option, missing `--to`, no inputs, unreadable response file). Nothing converted. |
| 130 | Cancelled (Ctrl+C). |

### Text output (default)

Console output is UTF-8.

* stdout: one line per file: `OK <source> -> <output>`, `SKIPPED <source>` or
  `CANCELLED <source>`. `--verify` adds `[verified]`; lossy or partial conversions add
  `[warnings: LossyFormat, ...]`; `--delete-source` adds `[source recycled]`; `--move-source-to` adds
  `[source moved to <path>]`.
* stderr: exactly one line per failed file: `ERROR <source>: <message>`. These lines are meant to be
  shown to the user as-is.

### JSON output (`--json`)

stdout carries one JSON object per line (NDJSON), so callers can show progress as files finish:

```json
{"type":"result","input":"C:\\a\\IMG_1.HEIC","output":"C:\\a\\IMG_1.png","status":"Succeeded","sourceFormat":"heic","targetFormat":"png","lossless":true,"warnings":[],"error":null,"message":null,"verified":null,"elapsedMs":184}
{"type":"summary","total":1,"succeeded":1,"skipped":0,"failed":0,"cancelled":0,"elapsedMs":190}
```

`status` is `Succeeded`, `Skipped`, `Failed` or `Cancelled`. `error` is a `ConversionError` name
(`InputNotFound`, `UnsupportedInputFormat`, `DecodeFailed`, `EncodeFailed`, `OutputExists`,
`OutputIsInput`, `VerificationFailed`, `IoError`) or null. `warnings` holds `ConversionWarning` names.
`verified` is `true`/`false` when `--verify` checked the file, otherwise null. `sourceDeleted` is
`true` when `--delete-source` moved the source to the Recycle Bin. `sourceMovedTo` is the
new path when `--move-source-to` moved the source, otherwise null. The summary adds
`sourceDeleteFailed` (sources that `--delete-source` or `--move-source-to` could not handle). stderr still gets the `ERROR` lines.

### `--delete-source`

The right-click menu passes this when `quickMenu.deleteOriginal` is `true` in the settings file.
The source is recycled (not permanently deleted) only when the result is `Succeeded`, the output is
a different file and it exists. If recycling fails, the output stays, the source stays, stderr gets
`ERROR <source>: Converted to <output>, but the original was kept: <reason>`, and the exit code is 1.
On drives without a Recycle Bin (some network or removable drives) Windows deletes the file
permanently. Text output adds `[source recycled]` to the `OK` line.

### `--move-source-to <dir>`

The right-click menu passes this when `quickMenu.moveOriginal` is `true` and `quickMenu.moveOriginalTo`
holds a full folder path (it then wins over `quickMenu.deleteOriginal`). The same rules as
`--delete-source` decide which sources move: only `Succeeded` results whose output is a separate file
that exists. A name already taken in the folder becomes `name (1).ext`, so nothing there is overwritten;
a source that is already in that folder stays put. If the move fails, the output and the source both
stay, stderr gets the same `ERROR ... but the original was kept: <reason>` line, and the exit code is 1.

### `formats`

Lists every format with its capabilities (`--json` prints a JSON array of `FormatInfo`).

### `detect`

Prints the format detected from each file's contents (not its extension), or `unknown`.

## Formats and losslessness

| Format | Read | Write | Lossless output | Alpha | Max bits/channel | Metadata |
|---|---|---|---|---|---|---|
| PNG | ✓ | ✓ | ✓ | ✓ | 16 | ✓ |
| TIFF | ✓ | ✓ | ✓ | ✓ | 16 | ICC, XMP, IPTC (EXIF dropped, reported) |
| WebP | ✓ | ✓ | ✓ (lossless mode) | ✓ | 8 | ✓ |
| AVIF | ✓ | ✓ | ✓ (lossless mode) | ✓ | 8 | ✓ |
| BMP | ✓ | ✓ | ✓ | ✓ | 8 | ✗ (EXIF orientation applied to pixels) |
| JPG | ✓ | ✓ | ✗ (lossy, quality 100, 4:4:4) | ✗ (filled white) | 8 | ✓ |
| HEIC/HEIF | ✓ | ✗ | n/a | | | |

"Lossless" means every decoded source pixel is stored exactly, as long as the source does not exceed
the target's bit depth or need transparency the target lacks; otherwise the result carries a
`BitDepthReduced` / `AlphaFlattened` warning and `lossless:false`. Converting a file to its own
format copies the bytes unchanged.

HEIC output is not offered (decided 2026-11-24): the bundled codec library decodes HEVC but has
no HEVC encoder, and the free one (x265) is GPL. AVIF uses the same HEIF container with the
royalty-free AV1 codec and is written truly lossless. `--to heic` is a usage error (exit 2).

`--parallel` runs several files at once; each conversion also uses several threads internally, so
more than the default rarely helps.

## Notes for callers

* Paths in results are absolute.
* Inputs are identified by content, so a PNG named `photo.jpg` is read as PNG.
* Outputs appear atomically: a temporary `.<name>.<random>.tmp` file in the output folder is renamed
  into place, and removed on failure or cancellation.
* For a fast right-click experience publish with ReadyToRun:
  `dotnet publish src/ImageConverter.Cli -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true`.
