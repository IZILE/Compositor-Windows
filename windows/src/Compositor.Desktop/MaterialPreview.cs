using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed class MaterialPreview : Control, IDisposable
{
    private Bitmap? _bitmap;
    private object? _item;
    internal object? Item => _item;
    internal int RenderCount { get; private set; }
    internal void Show(object item, int width = 240, int height = 150)
    {
        if (_bitmap is not null && (Equals(_item,item) || _item is GradientPreset old && item is GradientPreset next
            && old.Name == next.Name && old.Stops.SequenceEqual(next.Stops))) return;
        using var pixels = MaterialRendering.Render(item, width, height);
        using var data = pixels.Encode(SKEncodedImageFormat.Png, 100); using var stream = data.AsStream();
        var previous = _bitmap; _bitmap = new Bitmap(stream); _item = item; previous?.Dispose();
        RenderCount++; InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_bitmap is { } bitmap) context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(Bounds.Size));
    }
    public void Dispose() { _bitmap?.Dispose(); _bitmap = null; }
}

internal static class MaterialRendering
{
    internal static SKBitmap Render(object item, int width, int height)
    {
        if (item is BrushStyle brush)
        {
            using var document = new CanvasDocument(Guid.NewGuid(), width, height);
            var layer = new ImageLayer(Guid.NewGuid(), null, new Compositor.Core.Model.LayerTransform(0,0,width,height), "Preview");
            document.Layers.Add(layer); BrushEdits.EnsurePixels(document, layer.ID);
            var points = Enumerable.Range(0, 49).Select(i => new SKPoint(width*.12f + width*.76f*i/48,
                height*.5f + (float)Math.Sin(i*Math.PI*2/48)*height*.18f)).ToArray();
            BrushEdits.Paint(document, layer.ID, points, new BrushSettings(Diameter: height*.38, Hardness: brush.Hardness ?? 1,
                Red: 1, Green: 1, Blue: 1, Tip: brush.Tip));
            return layer.Asset!.Image.Copy();
        }
        if (item is ShapePreset shape)
            return ShapeEdits.Image(new LayerShapeStyle { Kind = shape.Kind, PathData = shape.PathData, EvenOdd = shape.EvenOdd ? true : null,
                Red = .75, Green = .84, Blue = 1, LineWidth = 3 }, width, height) ?? throw new InvalidDataException("The shape could not be rendered.");
        var image = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y=0; y<height; y++) for (var x=0; x<width; x++)
            image.SetPixel(x,y,item switch {
                GradientPreset gradient => gradient.Sample((double)x / Math.Max(1,width-1)),
                PatternPreset pattern => pattern.Sample(new SKPoint(x,y)),
                ColorPreset color => new SKColor(color.Color),
                _ => SKColors.Transparent
            });
        return image;
    }
}
