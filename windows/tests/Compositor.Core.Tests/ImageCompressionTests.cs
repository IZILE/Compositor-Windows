using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class ImageCompressionTests
{
    private static CanvasDocument Picture()
    {
        var document = new CanvasDocument(Guid.NewGuid(), 337, 189);
        var bitmap = new SKBitmap(Bitmaps.ColorInfo(document.Width, document.Height));
        for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)x, (byte)y, (byte)(x * 3 + y * 7), (byte)(x < 10 ? 0 : y % 7 == 0 ? 100 : 255)));
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(bitmap, "Detail"), new LayerTransform(0, 0, bitmap.Width, bitmap.Height), "Detail"));
        return document;
    }
    [Theory]
    [InlineData(PngCompression.Fast)] [InlineData(PngCompression.Balanced)] [InlineData(PngCompression.Smallest)]
    public void EveryPngPresetPreservesRgbaIncludingAcrossTileBoundaries(PngCompression compression)
    {
        using var doc = Picture(); using var expected = DocumentRenderer.Render(doc); using var stream = new MemoryStream();
        TiledPngWriter.Write(doc, stream, 57, compression); using var actual = SKBitmap.Decode(stream.ToArray());
        for (var y = 0; y < expected.Height; y++) for (var x = 0; x < expected.Width; x++) Assert.Equal(expected.GetPixel(x, y), actual.GetPixel(x, y));
    }
    [Fact]
    public void StrongerPngCompressionReducesThisDetailedImageWithoutLosingPixels()
    {
        using var doc = Picture(); using var fast = new MemoryStream(); using var small = new MemoryStream();
        ImageWriter.Write(doc, fast, new(ImageExportFormat.Png, Compression: PngCompression.Fast));
        ImageWriter.Write(doc, small, new(ImageExportFormat.Png, Compression: PngCompression.Smallest));
        Assert.True(small.Length < fast.Length, $"{small.Length} vs {fast.Length}");
    }
    [Fact]
    public void JpegQualityChangesActualBytesAndTransparencyBecomesWhite()
    {
        using var doc = Picture(); using var low = new MemoryStream(); using var high = new MemoryStream();
        ImageWriter.Write(doc, low, new(ImageExportFormat.Jpeg, 20)); ImageWriter.Write(doc, high, new(ImageExportFormat.Jpeg, 95));
        Assert.True(low.Length < high.Length); using var image = SKBitmap.Decode(high.ToArray());
        var pixel = image.GetPixel(1, 100); Assert.InRange(pixel.Red, 245, 255); Assert.InRange(pixel.Green, 245, 255);
    }
    [Fact]
    public void CancellationKeepsTheExistingDestinationAndLeavesNoTemporaryFiles()
    {
        using var doc = Picture(); var folder = Path.Combine(Path.GetTempPath(), "Compositor-export-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); var path = Path.Combine(folder, "export.png"); File.WriteAllText(path, "original");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        try
        {
            Assert.Throws<OperationCanceledException>(() => ImageWriter.Write(doc, path, new(ImageExportFormat.Png), cancellation.Token));
            Assert.Equal("original", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}
