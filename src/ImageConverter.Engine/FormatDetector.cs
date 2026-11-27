using System.Text;

namespace ImageConverter.Engine;

/// <summary>Identifies image formats from their leading bytes.</summary>
internal static class FormatDetector
{
    private const int HeaderLength = 64;

    public static ImageFormat? Detect(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var header = new byte[HeaderLength];
            var read = stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false);
            return Detect(header.AsSpan(0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ImageFormat? Detect(ReadOnlySpan<byte> h)
    {
        if (h.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ImageFormat.Png;
        }

        if (h.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            return ImageFormat.Jpeg;
        }

        if (h.StartsWith("BM"u8) && h.Length >= 14)
        {
            return ImageFormat.Bmp;
        }

        if (h.StartsWith("II*\0"u8) || h.StartsWith("MM\0*"u8) || h.StartsWith("II+\0"u8) || h.StartsWith("MM\0+"u8))
        {
            return ImageFormat.Tiff;
        }

        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8))
        {
            return ImageFormat.Gif;
        }

        if (h.Length >= 12 && h.StartsWith("RIFF"u8) && h.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ImageFormat.WebP;
        }

        return DetectIsoBmff(h);
    }

    /// <summary>HEIF family: reads the ftyp box brands to tell AVIF from HEIC.</summary>
    private static ImageFormat? DetectIsoBmff(ReadOnlySpan<byte> h)
    {
        if (h.Length < 16 || !h.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return null;
        }

        var boxSize = (int)Math.Min((uint)(h[0] << 24 | h[1] << 16 | h[2] << 8 | h[3]), (uint)h.Length);
        var brands = new List<string> { Encoding.ASCII.GetString(h.Slice(8, 4)) };
        for (var offset = 16; offset + 4 <= boxSize; offset += 4)
        {
            brands.Add(Encoding.ASCII.GetString(h.Slice(offset, 4)));
        }

        string[] avif = ["avif", "avis"];
        string[] hevc = ["heic", "heix", "heim", "heis", "hevc", "hevx"];
        string[] generic = ["mif1", "msf1"];
        if (avif.Contains(brands[0]))
        {
            return ImageFormat.Avif;
        }

        if (brands.Any(hevc.Contains))
        {
            return ImageFormat.Heic;
        }

        if (brands.Any(avif.Contains))
        {
            return ImageFormat.Avif;
        }

        return brands.Any(generic.Contains) ? ImageFormat.Heic : null;
    }
}
