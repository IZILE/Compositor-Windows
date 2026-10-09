using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

internal static class SampleProject
{
    public static void Create(string path)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 1280, 800);
        var background = new SKBitmap(Bitmaps.ColorInfo(1280, 800));
        using (var canvas = new SKCanvas(background))
        using (var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1280, 800),
                   [SKColor.Parse("#182538"), SKColor.Parse("#0d1321")], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            canvas.DrawRect(0, 0, 1280, 800, paint);
        document.Layers.Add(ImageImporter.Layer(ImportedImage.Create(background, "Background gradient")));

        AddShape(document, "Blue circle", ShapeKind.Ellipse, new SKRectI(790, 115, 1140, 465), 0.35, 0.62, 0.96);
        AddShape(document, "Coral circle", ShapeKind.Ellipse, new SKRectI(980, 375, 1180, 575), 0.99, 0.58, 0.45);
        AddShape(document, "Violet card", ShapeKind.Rectangle, new SKRectI(670, 385, 960, 595), 0.66, 0.60, 0.93, 26);
        AddShape(document, "Accent bar", ShapeKind.Rectangle, new SKRectI(90, 177, 143, 185), 0.35, 0.62, 0.96, 3);
        AddText(document, "COMPOSITOR", 66, 206, 75, SKColor.Parse("#ffffff"));
        AddText(document, "WINDOWS COMMUNITY", 25, 220, 205, SKColor.Parse("#9eb6d1"));
        AddText(document, "Create. Layer. Compose.", 42, 78, 410, SKColor.Parse("#e5eaf2"));
        AddText(document, "Editable shapes, text and layers.", 23, 80, 480, SKColor.Parse("#97a8bd"));
        AddText(document, "Made for Windows / English + Chinese", 19, 80, 676, SKColor.Parse("#7990aa"));

        using var preview = DocumentRenderer.Render(document);
        var png = PngCodec.Encode(preview);
        using var previewImage = SKImage.FromBitmap(preview);
        using var jpeg = previewImage.Encode(SKEncodedImageFormat.Jpeg, 85);
        ProjectStore.Save(ProjectSnapshot.FromDocument(document), path, jpeg.ToArray());
        File.WriteAllBytes(Path.ChangeExtension(path, ".png"), png);
    }

    private static void AddShape(CanvasDocument document, string name, ShapeKind kind, SKRectI box,
        double red, double green, double blue, double radius = 0)
    {
        var id = ShapeEdits.Add(document, new LayerShapeStyle
        {
            Kind = kind, Red = red, Green = green, Blue = blue, CornerRadius = radius,
        }, box, document.Layers.LastOrDefault()?.ID);
        if (id is { } found) document.Layers.First(layer => layer.ID == found).Name = name;
    }

    private static void AddText(CanvasDocument document, string content, double size, float x, float y, SKColor color)
    {
        TextEdits.Add(document, new LayerTextStyle
        {
            Content = content, FontName = "Arial", FontSize = size,
            Red = color.Red / 255.0, Green = color.Green / 255.0, Blue = color.Blue / 255.0,
        }, new SKPoint(x, y));
    }
}
