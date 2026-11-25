using ImageMagick;

namespace ImageConverter.Engine.Tests;

/// <summary>Builds deterministic test images and compares pixels exactly.</summary>
internal static class TestImages
{
    public const int Width = 61;
    public const int Height = 37;

    /// <summary>RGB(A) image with pseudo-random 8-bit values in every channel, so any change is detectable.</summary>
    public static MagickImage Noise8(bool alpha, int seed = 1)
    {
        var channels = alpha ? 4 : 3;
        var data = new byte[Width * Height * channels];
        new Random(seed).NextBytes(data);
        var settings = new PixelReadSettings((uint)Width, (uint)Height, StorageType.Char, alpha ? PixelMapping.RGBA : PixelMapping.RGB);
        var image = new MagickImage();
        image.ReadPixels(data, settings);
        image.Depth = 8;
        return image;
    }

    /// <summary>RGB image with 16-bit values that cannot be represented in 8 bits.</summary>
    public static MagickImage Noise16(int seed = 2)
    {
        var values = new ushort[Width * Height * 3];
        var random = new Random(seed);
        for (var i = 0; i < values.Length; i++)
        {
            // Odd low byte guarantees v != (v >> 8) * 257, i.e. not 8-bit representable.
            values[i] = (ushort)((random.Next(0, 256) << 8) | 0x01);
        }

        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var settings = new PixelReadSettings((uint)Width, (uint)Height, StorageType.Short, PixelMapping.RGB);
        var image = new MagickImage();
        image.ReadPixels(bytes, settings);
        image.Depth = 16;
        return image;
    }

    public static string Save(MagickImage image, string directory, string fileName, MagickFormat format)
    {
        var path = Path.Combine(directory, fileName);
        image.Write(path, format);
        return path;
    }

    /// <summary>All channels as 16-bit values, RGBA order (alpha = 65535 when absent).</summary>
    public static ushort[] Pixels(MagickImage image)
    {
        using var clone = (MagickImage)image.Clone();
        if (clone.ColorSpace != ColorSpace.sRGB && clone.ColorSpace != ColorSpace.RGB)
        {
            clone.ColorSpace = ColorSpace.sRGB;
        }

        using var pixels = clone.GetPixelsUnsafe();
        return pixels.ToShortArray(0, 0, clone.Width, clone.Height, "RGBA")!;
    }

    public static ushort[] Pixels(string path)
    {
        using var image = new MagickImage(path);
        return Pixels(image);
    }

    public static void AssertSamePixels(MagickImage expected, string actualPath)
    {
        using var actual = new MagickImage(actualPath);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        var e = Pixels(expected);
        var a = Pixels(actual);
        Assert.Equal(e.Length, a.Length);
        var differing = 0;
        var first = -1;
        for (var i = 0; i < e.Length; i++)
        {
            if (e[i] != a[i])
            {
                differing++;
                first = first < 0 ? i : first;
            }
        }

        Assert.True(differing == 0, $"{differing} channel values differ, first at index {first}: expected {(first < 0 ? 0 : e[first])}, got {(first < 0 ? 0 : a[first])}");
    }
}

/// <summary>Temporary directory deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "imgconv-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Fixtures
{
    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
