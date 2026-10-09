using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class LayerRenderCacheTests
{
    private static ImageLayer Layer(SKColor color, double x = 0)
    {
        var pixels = new SKBitmap(Bitmaps.ColorInfo(24, 20)); pixels.Erase(color);
        return new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Layer"),
            new Model.LayerTransform(x, 0, 24, 20), "Layer");
    }
    private static void EqualRender(CanvasDocument document, LayerRenderCache cache, SKRectI? wanted = null)
    {
        var region = wanted ?? SKRectI.Create(0, 0, document.Width, document.Height);
        using var expected = DocumentRenderer.RenderRegion(document, region);
        using var actual = DocumentRenderer.RenderRegion(document, region, cache);
        Assert.Equal(expected.GetPixelSpan().ToArray(), actual.GetPixelSpan().ToArray());
    }

    [Theory]
    [InlineData("opacity")]
    [InlineData("transform")]
    [InlineData("rotate-flip")]
    [InlineData("mask")]
    [InlineData("mask-disable")]
    [InlineData("mask-placement")]
    [InlineData("pixels")]
    [InlineData("blend")]
    [InlineData("order")]
    [InlineData("visibility")]
    [InlineData("clip")]
    [InlineData("adjustment")]
    [InlineData("resize")]
    [InlineData("undo")]
    public void CachedPixelsStayIdenticalAfterEdits(string edit)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 28);
        var back = Layer(new SKColor(70, 100, 150));
        var front = Layer(new SKColor(220, 90, 60, 150), 5); front.Opacity = 0.73;
        front.Mask = Model.LayerMask.Solid(true);
        document.Layers.AddRange([back, front]);
        using var cache = new LayerRenderCache();
        EqualRender(document, cache); EqualRender(document, cache);
        Assert.True(cache.Reuses >= 2);
        using var original = document.Clone();
        switch (edit)
        {
            case "opacity": front.Opacity = 0.18; break;
            case "transform": front.Transform = front.Transform with { X = -3, Y = 4 }; break;
            case "rotate-flip": front.Transform = front.Transform with { Rotation = 23, FlipX = true }; break;
            case "mask": front.Mask!.Dispose(); front.Mask = Model.LayerMask.Solid(false); break;
            case "mask-disable": front.Mask!.IsEnabled = false; break;
            case "mask-placement": front.Mask!.Placement = front.Transform with { X = 11, Width = 12 }; break;
            case "pixels": front.Asset!.Dispose(); front.Asset = Layer(SKColors.Green).Asset; break;
            case "blend": front.BlendMode = LayerBlendMode.Multiply; break;
            case "order": document.Layers.Reverse(); break;
            case "visibility": front.IsVisible = false; break;
            case "clip": front.MaskSourceID = back.ID; break;
            case "adjustment":
                var adjustment = new ImageLayer(Guid.NewGuid(), null, front.Transform, "Blur")
                    { Adjustment = new LayerAdjustment { Kind = AdjustmentKind.GaussianBlur, BlurRadius = 2 } };
                document.Layers.Add(adjustment); break;
            case "resize": document.Width = 27; document.Height = 26; break;
            case "undo": front.Opacity = 0.4; EqualRender(document, cache); document.Adopt(original); break;
        }
        EqualRender(document, cache); EqualRender(document, cache);
        EqualRender(document, cache, SKRectI.Create(4, 5, 11, 9));
    }

    [Fact]
    public void BudgetEvictionAndOpacityCopiesKeepPixelsCorrect()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 28);
        document.Layers.AddRange([Layer(SKColors.Blue), Layer(new SKColor(200, 50, 90, 120), 3)]);
        using var cache = new LayerRenderCache(2400);
        for (var step = 0; step < 8; step++)
        {
            document.Layers[1].Opacity = 0.2 + step * 0.1;
            EqualRender(document, cache);
            Assert.InRange(cache.RetainedBytes, 0, 2400);
        }
        cache.Clear(); Assert.Equal(0, cache.RetainedBytes);
    }

    [Fact]
    public void ParentMaskChangesInvalidateDescendantContent()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 32, 28);
        var folder = new ImageLayer(Guid.NewGuid(), null, new Model.LayerTransform(0, 0, 32, 28), "Folder")
            { IsGroup = true, Mask = Model.LayerMask.Solid(true) };
        var child = Layer(SKColors.Orange); child.ParentID = folder.ID;
        document.Layers.AddRange([folder, child]);
        using var cache = new LayerRenderCache(); EqualRender(document, cache);
        folder.Mask!.IsEnabled = false; EqualRender(document, cache);
        folder.Mask!.Dispose(); folder.Mask = Model.LayerMask.Solid(false); EqualRender(document, cache);
        folder.Mask!.Placement = folder.Transform with { X = 10, Width = 7 }; EqualRender(document, cache);
    }
}
