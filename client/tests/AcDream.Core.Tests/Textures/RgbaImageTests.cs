using AcDream.Core.Textures;

namespace AcDream.Core.Tests.Textures;

public sealed class RgbaImageTests
{
    private static readonly byte[] Pixels = [255,0,0,255, 0,255,0,128, 255,255,255,0, 3,70,130,90, 45,170,12,255, 250,5,40,16];

    // Pixel fixtures captured from the previous image pipeline.
    [Theory]
    [InlineData(7, 5, false, "/wAA//sKAP+gZgDAAP8AcwD/ADYA/wAIGe0DAP8AA//qEAPrjW0CwQb1AYgA/wFGAP8EDQD/AADOACWrqyYfsFuBEsMexgjAFdMGcFSZEBkA/wAALRuHVyxTV3QrlCHGK60M9zSrCZp1dBQmAP8AAAA8+jcAc4BeGZsnxy+mDf88oQmqfWsUKwD/AAA=")]
    [InlineData(2, 1, false, "fVoYux/KCEk=")]
    [InlineData(9, 6, true, "/wAA//8AAP//AAD/AP8AgAD/AIAA/wCA////AP///wD///8A/wAA//8AAP//AAD/AP8AgAD/AIAA/wCA////AP///wD///8A/wAA//8AAP//AAD/AP8AgAD/AIAA/wCA////AP///wD///8AA0aCWgNGgloDRoJaLaoM/y2qDP8tqgz/+gUoEPoFKBD6BSgQA0aCWgNGgloDRoJaLaoM/y2qDP8tqgz/+gUoEPoFKBD6BSgQA0aCWgNGgloDRoJaLaoM/y2qDP8tqgz/+gUoEPoFKBD6BSgQ")]
    [InlineData(1, 8, false, "qF8Af6FiAH+RaAOAc3MNgVKAGYM1jCKEJpEnhCCUKoU=")]
    public void Resize_PreservesEstablishedPixels(int width, int height, bool nearest, string expectedBase64)
    {
        using var image = RgbaImage.FromPixels(Pixels, 3, 2);
        image.Resize(width, height, nearest);
        byte[] actual = new byte[width * height * 4];
        image.CopyPixelDataTo(actual);
        Assert.Equal(Convert.FromBase64String(expectedBase64), actual);
    }

    [Fact]
    public void DrawImage_ClipsAndPreservesSourceOverAlpha()
    {
        using var image = new RgbaImage(4, 3, new RgbaPixel(30, 50, 90, 110));
        using var source = RgbaImage.FromPixels(Pixels, 3, 2);
        image.DrawImage(source, -1, 1);
        byte[] actual = new byte[4 * 3 * 4];
        image.CopyPixelDataTo(actual);
        Assert.Equal(Convert.FromBase64String("HjJabh4yWm4eMlpuHjJabgnCG7ceMlpuHjJabh4yWm4tqgz/PCxTdx4yWm4eMlpu"), actual);
    }

    [Fact]
    public void Png_RoundTripPreservesAllChannelsIncludingTransparentRgb_AndLeavesStreamOpen()
    {
        using var image = RgbaImage.FromPixels(Pixels, 3, 2);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        Assert.True(stream.CanWrite);
        Assert.Equal(new byte[] {137,80,78,71,13,10,26,10}, stream.ToArray()[..8]);
        stream.Position = 0;
        using var decoded = RgbaImage.Load(stream);
        byte[] actual = new byte[Pixels.Length];
        decoded.CopyPixelDataTo(actual);
        Assert.Equal(Pixels, actual);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void CropThenZoom_PreservesPixelOrder_AndDoesNotModifySource()
    {
        using var image = RgbaImage.FromPixels(Pixels, 3, 2);
        using var crop = image.Clone().Crop(1, 0, 2, 2).Resize(4, 4, nearest: true);
        Assert.Equal(new RgbaPixel(0,255,0,128), crop[0,0]);
        Assert.Equal(new RgbaPixel(255,255,255,0), crop[3,1]);
        Assert.Equal(new RgbaPixel(250,5,40,16), crop[3,3]);
        Assert.Equal(3, image.Width);
        Assert.Equal(new RgbaPixel(255,0,0,255), image[0,0]);
    }

    [Fact]
    public void InvalidDimensionsBuffersAndDisposedImagesFailClearly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbaImage(0, 1));
        Assert.Throws<OverflowException>(() => new RgbaImage(int.MaxValue, 2));
        Assert.Throws<ArgumentException>(() => RgbaImage.FromPixels([1,2,3], 1, 1));
        using var image = new RgbaImage(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Crop(1, 0, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.Resize(1, 0));
        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => image.Clone());
        Assert.Throws<ObjectDisposedException>(() => image.SaveAsPng(new MemoryStream()));
    }
}
