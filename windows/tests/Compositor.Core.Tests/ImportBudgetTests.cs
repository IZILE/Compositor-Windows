using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class ImportBudgetTests
{
    [Fact]
    public void RemainingPixelsCountEveryImageAndMask()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 20);
        var image = ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(7, 5)), "Image");
        var layer = new ImageLayer(Guid.NewGuid(), image, new LayerTransform(0, 0, 7, 5), "Image")
        {
            Mask = LayerMask.Solid(true),
        };
        document.Layers.Add(layer);
        Assert.Equal(DocumentLimits.DocumentPixelBudget - 36, document.RemainingImagePixels);
        document.Layers.Add(new ImageLayer(Guid.NewGuid(), image, layer.Transform, "Duplicate"));
        Assert.Equal(DocumentLimits.DocumentPixelBudget - 71, document.RemainingImagePixels);
    }

    [Fact]
    public void PasteRefusesAnExhaustedDocumentWithoutChangingEitherImage()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 20);
        var asset = ImportedImage.Create(new SKBitmap(Bitmaps.ColorInfo(1000, 100)), "Shared image");
        var count = (DocumentLimits.DocumentPixelBudget + 99_999) / 100_000;
        for (var i = 0; i < count; i++)
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), asset, new LayerTransform(0, 0, 1000, 100), "Image"));
        Assert.True(count < LayerPlacement.MaxLayers);
        Assert.Equal(0, document.RemainingImagePixels);
        using var clipboard = new ClipboardImage(new SKBitmap(Bitmaps.ColorInfo(1, 1)), SKRectI.Create(0, 0, 1, 1));
        clipboard.Image.Erase(SKColors.Blue);
        Assert.Null(SelectionClipboard.Paste(document, clipboard, null));
        Assert.Equal(count, document.Layers.Count);
        Assert.Equal(SKColors.Blue, clipboard.Image.GetPixel(0, 0));
        Assert.Equal(1000, asset.Width);
    }

    [Fact]
    public void PasteChecksOversizePlacementBeforeAllocating()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 20, 20);
        using var clipboard = new ClipboardImage(new SKBitmap(Bitmaps.ColorInfo(1, 1)),
            SKRectI.Create(0, 0, DocumentLimits.MaxSide + 1, 2));
        Assert.Null(SelectionClipboard.Paste(document, clipboard, null));
        Assert.Empty(document.Layers);
    }
}
