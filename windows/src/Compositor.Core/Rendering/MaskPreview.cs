using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Rendering;

/// <summary>The raw grayscale mask, regardless of its visibility, opacity, clipping or enabled state.</summary>
public static class MaskPreview
{
    public static SKBitmap RenderRegion(ImageLayer layer, SKRectI region)
    {
        var result = DocumentRenderer.Allocate(region.Width, region.Height);
        if (layer.Mask is not { } mask) { result.Erase(SKColors.Black); return result; }
        var image = mask.Asset.Image;
        var edge = Background(mask.Asset.Thumbnail);
        result.Erase(new SKColor(edge, edge, edge));
        if (image.Width == 1 && image.Height == 1) { result.Erase(image.GetPixel(0, 0)); return result; }
        using var canvas = new SKCanvas(result);
        canvas.Translate(-region.Left, -region.Top);
        canvas.Concat(BrushEditsMapping(layer));
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
        return result;
    }

    private static SKMatrix BrushEditsMapping(ImageLayer layer) =>
        Document.BrushEdits.PixelToDocument(layer.MaskTransform, layer.Mask!.Asset.Width, layer.Mask.Asset.Height);

    public static byte Background(SKBitmap image)
    {
        long sum = 0, count = 0;
        for (var x = 0; x < image.Width; x++) { sum += image.GetPixel(x, 0).Red; sum += image.GetPixel(x, image.Height - 1).Red; count += 2; }
        for (var y = 1; y < image.Height - 1; y++) { sum += image.GetPixel(0, y).Red; sum += image.GetPixel(image.Width - 1, y).Red; count += 2; }
        return count == 0 || sum >= count * 127.5 ? (byte)255 : (byte)0;
    }
}
