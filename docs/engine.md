# Conversion engine (`src/ImageConverter.Engine`)

Library used by `imgconv.exe` and the batch app. Backed by Magick.NET Q16 (`Magick.NET-Q16-x64`),
which ships the native codecs (libheif + libde265 for HEIC, aom for AVIF, libwebp, libpng, libtiff,
libjpeg-turbo) for win-x64.

```csharp
IImageConverter engine = new ConversionEngine();   // thread-safe, reuse one instance

ImageFormat? kind = engine.DetectFormat(@"C:\photos\IMG_0001.HEIC");     // by content
string output = OutputPaths.ForTarget(input, ImageFormat.Png);           // C:\photos\IMG_0001.png
ConversionResult r = await engine.ConvertAsync(input, output, ImageFormat.Png,
    new ConversionOptions { Verify = true }, progress, cancellationToken);
```

* `ConvertAsync` never throws for per-file problems; check `Status`, `Error`, `ErrorMessage`.
* `IsLossless` is true when every decoded pixel value is stored exactly. `Warnings` lists anything that
  was not kept (`LossyFormat`, `BitDepthReduced`, `AlphaFlattened`, `MetadataDropped`,
  `ExtraFramesDropped`, `ColorSpaceConverted`).
* `ImageFormats.All` is the shared format table (ids, extensions, read/write, lossless, alpha, bit depth).
* `OutputPaths.NextFree` gives Explorer-style `name (1).ext` names.

## How losslessness is achieved

| Target | Encoder settings |
|---|---|
| PNG | zlib level 3 (about 3x faster than the default for ~6% larger files), 16-bit kept |
| TIFF | Deflate (ZIP) compression, 16-bit kept |
| WebP | `webp:lossless=true`, `webp:exact=true` (keeps RGB under transparent pixels) |
| AVIF | quality 100, `heic:chroma=444`, `heic:cicp=1/13/0/1` (identity matrix = RGB, no YCbCr rounding), `heic:speed=8` |
| BMP | default; EXIF orientation is applied to the pixels because BMP has no orientation tag |
| JPG | quality 100 (configurable), `jpeg:sampling-factor=4:4:4`, alpha flattened onto the background colour |

Same-format conversions copy the file bytes. Reads always pass the detected format to the decoder,
so no other ImageMagick coder is ever invoked on user files.

## Tests

`tests/ImageConverter.Engine.Tests` writes deterministic noise images (every channel random, so any
rounding shows up), converts them, decodes the output and compares every 16-bit channel value.
Mutating any lossless encoder setting makes these tests fail.

## Speed (12 MP photo-like image, 4 cores, Linux)

| Target | Time |
|---|---|
| PNG | 1.6 s |
| TIFF | ~1 s |
| WebP lossless | 8 s |
| AVIF lossless | 3.4 s |

Lossless WebP is the slowest target; JPG and same-format copies are near-instant.
