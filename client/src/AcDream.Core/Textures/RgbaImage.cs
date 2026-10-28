using System.Numerics;
using StbImageSharp;
using StbImageWriteSharp;

namespace AcDream.Core.Textures;

/// <summary>One straight-alpha, eight-bit RGBA pixel.</summary>
public readonly record struct RgbaPixel(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// CPU image storage for screenshots and image tools. Rows run from top to bottom;
/// decoding and PNG encoding use managed stb ports without native dependencies.
/// </summary>
public sealed class RgbaImage : IDisposable
{
    private byte[] _pixels;
    private bool _disposed;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public RgbaImage(int width, int height, RgbaPixel fill = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _pixels = new byte[checked(width * height * 4)];
        if (fill != default)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    this[x, y] = fill;
    }

    private RgbaImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        _pixels = pixels;
    }

    public RgbaPixel this[int x, int y]
    {
        get
        {
            int i = Offset(x, y);
            return new(_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
        }
        set
        {
            int i = Offset(x, y);
            _pixels[i] = value.R;
            _pixels[i + 1] = value.G;
            _pixels[i + 2] = value.B;
            _pixels[i + 3] = value.A;
        }
    }

    private int Offset(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel lies outside the image.");
        return (y * Width + x) * 4;
    }

    public static RgbaImage Load(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static RgbaImage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ImageResult image = ImageResult.FromStream(stream, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return new(image.Width, image.Height, image.Data);
    }

    public static RgbaImage FromPixels(ReadOnlySpan<byte> pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Expected four bytes per pixel.", nameof(pixels));
        return new(width, height, pixels.ToArray());
    }

    public void CopyPixelDataTo(Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pixels.CopyTo(destination);
    }

    public void CopyPixelDataTo(Span<RgbaPixel> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (destination.Length < Width * Height)
            throw new ArgumentException("Destination is too small.", nameof(destination));
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                destination[y * Width + x] = this[x, y];
    }

    public void SaveAsPng(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using Stream stream = File.Create(path);
        SaveAsPng(stream);
    }

    public void SaveAsPng(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        new ImageWriter().WritePng(_pixels, Width, Height,
            StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream);
    }

    public RgbaImage Clone()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new(Width, Height, (byte[])_pixels.Clone());
    }

    public RgbaImage Crop(int x, int y, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || width > Width - x || height > Height - y)
            throw new ArgumentOutOfRangeException(nameof(width), "Crop lies outside the image.");
        byte[] cropped = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
            _pixels.AsSpan(((y + row) * Width + x) * 4, width * 4)
                .CopyTo(cropped.AsSpan(row * width * 4));
        _pixels = cropped;
        Width = width;
        Height = height;
        return this;
    }

    /// <summary>Resizes in place. Pixel-art zooms use nearest; other tools use bicubic filtering.</summary>
    public RgbaImage Resize(int width, int height, bool nearest = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (width == Width && height == Height) return this;
        byte[] resized = new byte[checked(width * height * 4)];
        if (nearest)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _pixels.AsSpan(((int)((long)y * Height / height) * Width + (int)((long)x * Width / width)) * 4, 4)
                        .CopyTo(resized.AsSpan((y * width + x) * 4, 4));
        }
        else
        {
            // Filter premultiplied channels, then restore straight alpha so transparent
            // edges do not bleed their hidden RGB into visible pixels. Downsampling
            // widens the filter to cover the source footprint of each output pixel.
            var horizontal = new Vector4[checked(width * Height)];
            Weight[][] xs = Weights(Width, width);
            Weight[][] ys = Weights(Height, height);
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector4 sum = default;
                    foreach (Weight weight in xs[x])
                    {
                        RgbaPixel p = this[weight.Index, y];
                        float a = p.A / 255f;
                        sum += new Vector4(p.R / 255f * a, p.G / 255f * a, p.B / 255f * a, a) * weight.Value;
                    }
                    horizontal[y * width + x] = sum;
                }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector4 sum = default;
                    foreach (Weight weight in ys[y]) sum += horizontal[weight.Index * width + x] * weight.Value;
                    if (sum.W != 0) { sum.X /= sum.W; sum.Y /= sum.W; sum.Z /= sum.W; }
                    else sum = default;
                    int i = (y * width + x) * 4;
                    resized[i] = Channel(sum.X);
                    resized[i + 1] = Channel(sum.Y);
                    resized[i + 2] = Channel(sum.Z);
                    resized[i + 3] = Channel(sum.W);
                }
        }
        _pixels = resized;
        Width = width;
        Height = height;
        return this;
    }

    private readonly record struct Weight(int Index, float Value);

    private static Weight[][] Weights(int source, int destination)
    {
        double ratio = (double)source / destination;
        double scale = Math.Max(1, ratio);
        var weights = new Weight[destination][];
        for (int i = 0; i < destination; i++)
        {
            double center = (i + 0.5) * ratio - 0.5;
            int first = Math.Max(0, (int)Math.Ceiling(center - 2 * scale));
            int last = Math.Min(source - 1, (int)Math.Floor(center + 2 * scale));
            var row = new Weight[last - first + 1];
            float total = 0;
            for (int j = first; j <= last; j++)
            {
                float x = (float)Math.Abs((j - center) / scale);
                float value = x <= 1 ? ((1.5f * x - 2.5f) * x * x) + 1
                    : x < 2 ? ((-0.5f * x + 2.5f) * x - 4) * x + 2 : 0;
                row[j - first] = new(j, value);
                total += value;
            }
            for (int j = 0; j < row.Length; j++) row[j] = row[j] with { Value = row[j].Value / total };
            weights[i] = row;
        }
        return weights;
    }

    /// <summary>Composites an image over this image, clipping at the destination edges.</summary>
    public void DrawImage(RgbaImage source, int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source._disposed, source);
        for (int sy = Math.Max(0, -y); sy < source.Height && sy + y < Height; sy++)
            for (int sx = Math.Max(0, -x); sx < source.Width && sx + x < Width; sx++)
            {
                RgbaPixel front = source[sx, sy];
                if (front.A == 0) continue;
                if (front.A == 255) { this[sx + x, sy + y] = front; continue; }
                RgbaPixel back = this[sx + x, sy + y];
                float fa = front.A / 255f, ba = back.A / 255f * (1 - fa), a = fa + ba;
                this[sx + x, sy + y] = new(
                    Channel((front.R * fa + back.R * ba) / (255f * a)),
                    Channel((front.G * fa + back.G * ba) / (255f * a)),
                    Channel((front.B * fa + back.B * ba) / (255f * a)), Channel(a));
            }
    }

    private static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);

    public void Dispose()
    {
        _pixels = [];
        _disposed = true;
    }
}
