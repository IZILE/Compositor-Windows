using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class PreparedBrushTests
{
    private static CanvasDocument Picture(bool blank = false, bool mask = false, bool transformed = false)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 128, 96);
        var image = new SKBitmap(new SKImageInfo(128, 96, SKColorType.Bgra8888, SKAlphaType.Premul));
        image.Erase(new SKColor(70, 120, 180, 190));
        var layer = new ImageLayer(Guid.NewGuid(), blank ? null : ImportedImage.Create(image, "Pixels"),
            new LayerTransform(0, 0, transformed ? 108 : 128, 96, transformed ? 23 : 0, transformed, false), "Pixels");
        if (blank) image.Dispose();
        if (mask) layer.Mask = LayerMask.Solid(true);
        document.Layers.Add(layer);
        document.Selection = DocumentSelection.Rectangular(SKRectI.Create(14, 9, 100, 75), 128, 96).WithFeather(3);
        return document;
    }
    private static SKPoint[] Stroke => [new(20.2f, 30.6f), new(80.1f, 55.7f), new(43.3f, 60.2f), new(43.3f, 60.2f), new(111.8f, 20.4f)];
    private static byte[] Pixels(CanvasDocument document, bool mask) =>
        (mask ? document.Layers[0].Mask!.Asset.Image : document.Layers[0].Asset!.Image).GetPixelSpan().ToArray();

    [Theory]
    [InlineData(false, false, false, 1.0, false)]
    [InlineData(false, false, false, 0.0, true)]
    [InlineData(false, false, true, 1.0, true)]
    [InlineData(false, false, true, 0.35, false)]
    [InlineData(true, false, false, 0.0, false)]
    [InlineData(true, false, true, 1.0, false)]
    [InlineData(false, true, false, 0.0, false)]
    [InlineData(false, true, true, 1.0, false)]
    public void IncrementalInputMatchesTheCompleteStrokeAndOneUndoStep(bool blank, bool mask, bool transformed, double hardness, bool erase)
    {
        using var complete = Picture(blank, mask, transformed); using var incremental = Picture(blank, mask, transformed);
        var settings = new BrushSettings(Diameter: 21.5, Hardness: hardness, Opacity: 0.37, Red: 0.91, Green: 0.18, Blue: 0.28, Erasing: erase);
        var layer = incremental.Layers[0]; var original = layer.Asset; var originalMask = layer.Mask;
        using var prepared = BrushEdits.Prepare(incremental, layer.ID, settings, mask)!;
        Assert.NotNull(prepared);
        foreach (var point in Stroke) prepared.Append(point);
        Assert.Same(original, layer.Asset); Assert.Same(originalMask, layer.Mask);
        var history = new DocumentHistory(); history.Begin("Stroke", incremental, layer.ID);
        Assert.True(prepared.TryCommit(incremental, layer.ID, settings, mask)); history.End(incremental, layer.ID);
        if (mask) Assert.True(BrushEdits.PaintMask(complete, complete.Layers[0].ID, Stroke, settings));
        else { BrushEdits.EnsurePixels(complete, complete.Layers[0].ID); Assert.True(BrushEdits.Paint(complete, complete.Layers[0].ID, Stroke, settings)); }
        Assert.Equal(Pixels(complete, mask), Pixels(incremental, mask)); Assert.Equal(1, history.UndoCount);
        var undo = history.Undo()!.Value.Document!.Layers[0]; Assert.Same(original, undo.Asset); Assert.Same(originalMask, undo.Mask);
        Assert.Equal(Pixels(incremental, mask), Pixels(history.Redo()!.Value.Document!, mask));
        Assert.False(prepared.TryCommit(incremental, layer.ID, settings, mask));
    }

    [Theory]
    [InlineData("selection")]
    [InlineData("transform")]
    [InlineData("pixels")]
    [InlineData("settings")]
    [InlineData("size")]
    public void APreparedStrokeRefusesAnObsoleteTarget(string change)
    {
        using var document = Picture(); var layer = document.Layers[0]; var settings = new BrushSettings();
        using var prepared = BrushEdits.Prepare(document, layer.ID, settings)!; prepared.Append(new SKPoint(50, 40));
        switch (change)
        {
            case "selection": document.Selection = DocumentSelection.All; break;
            case "transform": layer.Transform = layer.Transform with { X = 1 }; break;
            case "pixels": layer.Asset = ImportedImage.Create(layer.Asset!.Image.Copy(), "Replacement"); break;
            case "settings": settings = settings with { Erasing = true }; break;
            case "size": document.Width++; break;
        }
        var before = Pixels(document, false); Assert.False(prepared.TryCommit(document, layer.ID, settings)); Assert.Equal(before, Pixels(document, false));
    }
}
