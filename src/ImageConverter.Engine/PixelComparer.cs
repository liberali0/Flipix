using ImageMagick;

namespace ImageConverter.Engine;

/// <summary>Exact per-channel comparison between an in-memory image and a written file.</summary>
internal static class PixelComparer
{
    public static PixelVerification Compare(MagickImage expected, string writtenPath, MagickFormat format)
    {
        using var actual = new MagickImage();
        actual.Read(writtenPath, new MagickReadSettings { Format = format });
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            return new PixelVerification(false, (long)expected.Width * expected.Height);
        }

        var e = Rgba(expected);
        var a = Rgba(actual);
        long different = 0;
        for (var i = 0; i < e.Length; i += 4)
        {
            if (e[i] != a[i] || e[i + 1] != a[i + 1] || e[i + 2] != a[i + 2] || e[i + 3] != a[i + 3])
            {
                different++;
            }
        }

        return new PixelVerification(different == 0, different);
    }

    private static ushort[] Rgba(MagickImage image)
    {
        using var pixels = image.GetPixelsUnsafe();
        return pixels.ToShortArray(0, 0, image.Width, image.Height, "RGBA")
            ?? throw new InvalidOperationException("Could not read pixels.");
    }
}
