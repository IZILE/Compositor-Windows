using Avalonia;
using Avalonia.Media.Imaging;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal static IReadOnlyList<string> RasterCacheSelfCheck()
    {
        var report = new List<string>();
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 48);
        var pixels = new SKBitmap(Bitmaps.ColorInfo(64, 48)); pixels.Erase(SKColors.Orange);
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Layer"), new LayerTransform(0, 0, 64, 48), "Layer");
        document.Layers.Add(layer);
        var view = new CanvasView();
        var region = SKRectI.Create(0, 0, 64, 48);
        static unsafe byte[] Bytes(WriteableBitmap bitmap)
        {
            using var held = bitmap.Lock();
            var bytes = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
            for (var row = 0; row < bitmap.PixelSize.Height; row++)
                new ReadOnlySpan<byte>((void*)(held.Address + row * held.RowBytes), bitmap.PixelSize.Width * 4)
                    .CopyTo(bytes.AsSpan(row * bitmap.PixelSize.Width * 4));
            return bytes;
        }
        void Verify(string name, Guid? mask = null)
        {
            var actual = view._rasterCache.Get(document, region, mask);
            using var reference = mask is not null ? MaskPreview.RenderRegion(layer, region) : DocumentRenderer.RenderRegion(document, region);
            using var expected = ToImage(reference);
            if (!Bytes(actual).SequenceEqual(Bytes(expected))) throw new InvalidOperationException("Cached canvas differs after " + name);
            report.Add("canvas cache pixels stay correct after " + name);
        }
        try
        {
            Verify("first draw");
            var initial = view.RasterBuildCount;
            document.Selection = DocumentSelection.Rectangular(SKRectI.Create(2, 2, 5, 5), 64, 48);
            Verify("selection change");
            if (view.RasterBuildCount != initial) throw new InvalidOperationException("An overlay recomposed document pixels.");
            report.Add("selection overlays reuse composed pixels");
            layer.Opacity = 0.35; Verify("opacity change");
            layer.Transform = layer.Transform with { X = 8, Rotation = 13 }; Verify("transform change");
            layer.Mask = LayerMask.Solid(false); Verify("mask addition");
            layer.Mask!.IsEnabled = false; Verify("in-place mask disable");
            Verify("solo mask", layer.ID); Verify("return from solo mask");
            layer.IsVisible = false; Verify("visibility change"); layer.IsVisible = true;
            var old = layer.Asset; var next = new SKBitmap(Bitmaps.ColorInfo(64, 48)); next.Erase(SKColors.Blue);
            layer.Asset = ImportedImage.Create(next, "Replacement"); old!.Dispose(); Verify("pixel replacement");
            var count = view.RasterBuildCount;
            view._rasterCache.Get(document, SKRectI.Create(8, 8, 16, 12), null);
            if (view.RasterBuildCount != count) throw new InvalidOperationException("Panning inside cached bounds recomposed pixels.");
            report.Add("panning inside cached bounds reuses composed pixels");
            using var preview = document.Clone(); preview.Layers[0].Opacity = 0.9;
            view._rasterCache.Get(preview, region, null); Verify("preview return");
        }
        finally { view._rasterCache.Clear(); }
        return report;
    }
}
