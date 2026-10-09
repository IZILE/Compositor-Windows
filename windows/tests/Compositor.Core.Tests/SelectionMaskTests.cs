using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class SelectionMaskTests
{
    [Theory]
    [InlineData(true, 255, 0)]
    [InlineData(false, 0, 255)]
    public void SelectionBecomesMaskInLayerPixelsAndOneUndoRestoresTheSelection(bool reveal, int inside, int outside)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 32);
        var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(8, 6, 16, 16), "Layer");
        document.Layers.Add(layer);
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(12, 10, 8, 8), 32, 32);
        var selection = document.Selection;
        var history = new DocumentHistory(); history.Begin("Mask", document, layer.ID);
        Assert.True(LayerMaskEdits.AddFromSelection(document, layer.ID, reveal));
        history.End(document, layer.ID);
        Assert.Equal(16, layer.Mask!.Asset.Width); Assert.Equal(16, layer.Mask.Asset.Height);
        Assert.Equal(inside, layer.Mask.Asset.Image.GetPixel(8, 8).Red);
        Assert.Equal(outside, layer.Mask.Asset.Image.GetPixel(1, 1).Red);
        Assert.Null(document.Selection.Path);
        document.Adopt(history.Undo()!.Value.Document!);
        Assert.Null(document.Layers[0].Mask); Assert.Same(selection, document.Selection);
        document.Adopt(history.Redo()!.Value.Document!); Assert.NotNull(document.Layers[0].Mask); Assert.Null(document.Selection.Path);
    }

    [Fact]
    public void MirroredAndRotatedLayersMapTheSelectionIntoTheirOwnPixelGrid()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 32);
        var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(8, 8, 16, 16, 90, FlipX: true), "Turned");
        document.Layers.Add(layer);
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(8, 8, 8, 16), 32, 32);
        Assert.True(LayerMaskEdits.AddFromSelection(document, layer.ID, true));
        Assert.Equal(255, layer.Mask!.Asset.Image.GetPixel(8, 12).Red);
        Assert.Equal(0, layer.Mask.Asset.Image.GetPixel(8, 3).Red);
    }

    [Fact]
    public void FeatheredSelectionKeepsIntermediateGrayLevelsInTheMask()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 32);
        var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 32, 32), "Soft");
        document.Layers.Add(layer);
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(8, 8, 16, 16), 32, 32).WithFeather(4);
        Assert.True(LayerMaskEdits.AddFromSelection(document, layer.ID, true));
        Assert.InRange(layer.Mask!.Asset.Image.GetPixel(8, 16).Red, (byte)1, (byte)254);
        Assert.True(layer.Mask.Asset.Image.GetPixel(16, 16).Red > 240);
    }

    [Fact]
    public void RefusedMaskDoesNotConsumeTheSelectionOrReplaceAnExistingMask()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 32);
        var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 32, 32), "Existing") { Mask = LayerMask.Solid(true) };
        document.Layers.Add(layer); document.Selection = DocumentSelection.Rectangular(SKRectI.Create(8, 8, 8, 8), 32, 32);
        var selection = document.Selection; var mask = layer.Mask;
        Assert.False(LayerMaskEdits.AddFromSelection(document, layer.ID, true));
        Assert.Same(selection, document.Selection); Assert.Same(mask, layer.Mask);
    }

    [Fact]
    public void SoloMaskIgnoresVisibilityOpacityAndDisabledStateAndRespectsItsPlacement()
    {
        using var image = Bitmaps.Allocate(Bitmaps.MaskInfo(4, 4)); image.Erase(SKColors.Black);
        image.SetPixel(2, 2, SKColors.White);
        var mask = LayerMask.AssetFrom(image.Copy()); mask.IsEnabled = false; mask.Placement = new LayerTransform(10, 8, 4, 4);
        using var layer = new ImageLayer(Guid.NewGuid(), null, new LayerTransform(0, 0, 4, 4), "Mask") { Mask = mask, IsVisible = false, Opacity = 0 };
        using var view = MaskPreview.RenderRegion(layer, SKRectI.Create(8, 6, 8, 8));
        Assert.Equal(255, view.GetPixel(4, 4).Red);
        Assert.Equal(0, view.GetPixel(0, 0).Red);
        Assert.Equal(255, view.GetPixel(0, 0).Alpha);
    }
}
