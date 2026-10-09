using Avalonia;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

internal static class MaterialPreviewChecks
{
    internal static void Run(List<string> report)
    {
        void Check(bool good,string name)
        { if (!good) throw new InvalidOperationException("MATERIAL PREVIEW FAILED: "+name); report.Add("PASS: "+name); }
        SKRectI Ink(SKBitmap image)
        {
            var left=image.Width; var top=image.Height; var right=0; var bottom=0;
            for(var y=0;y<image.Height;y++) for(var x=0;x<image.Width;x++) if(image.GetPixel(x,y).Alpha>128)
            { left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x+1);bottom=Math.Max(bottom,y+1); }
            return new SKRectI(left,top,right,bottom);
        }
        foreach(var preset in MaterialPresets.Shapes)
        {
            using var pixels=MaterialRendering.Render(preset,72,40); var ink=Ink(pixels);
            Check(Math.Abs(ink.Width-ink.Height)<=2,$"{preset.Name}: wide list rows preserve square shape proportions");
        }
        using(var preview=new MaterialPreview { Width=220,Height=150 })
        {
            preview.Show(new ShapePreset("Circle",ShapeKind.Ellipse),240,150);
            preview.Measure(new Size(220,150)); preview.Arrange(new Rect(0,0,220,150));
            using var target=new RenderTargetBitmap(new PixelSize(220,150)); target.Render(preview);
            using var stream=new MemoryStream(); target.Save(stream,new PngBitmapEncoderOptions()); stream.Position=0;
            using var image=SKBitmap.Decode(stream); var ink=Ink(image);
            Check(Math.Abs(ink.Width-ink.Height)<=2 && Math.Abs((ink.Left+ink.Right)/2d-110)<=1 && Math.Abs((ink.Top+ink.Bottom)/2d-75)<=1,
                "large previews keep a circle round and centered when the actual layout differs from its cached image");
        }
        using(var pixels=MaterialRendering.Render(new ShapePreset("Wide SVG",ShapeKind.Custom,"M0 0H1V1H0Z",AspectRatio:4),72,40))
        { var ink=Ink(pixels); Check(Math.Abs((double)ink.Width/ink.Height-4)<.1,"imported silhouettes keep their intrinsic proportions in thumbnails"); }
        using(var preview=new MaterialPreview { Width=62,Height=20,CornerRadius=10 })
        {
            preview.Show(MaterialPresets.Gradients[0],62,20); preview.Measure(new Size(62,20)); preview.Arrange(new Rect(0,0,62,20));
            using var target=new RenderTargetBitmap(new PixelSize(62,20)); target.Render(preview);
            using var stream=new MemoryStream(); target.Save(stream,new PngBitmapEncoderOptions()); stream.Position=0; using var image=SKBitmap.Decode(stream);
            Check(image.GetPixel(0,0).Alpha==0 && image.GetPixel(61,0).Alpha==0 && image.GetPixel(31,0).Alpha>200,
                "gradient fills its swatch and both upper corners follow the rounded outline");
            preview.Show(MaterialPresets.Gradients[^1],62,20);
            using var transparentTarget=new RenderTargetBitmap(new PixelSize(62,20)); transparentTarget.Render(preview);
            using var transparentStream=new MemoryStream(); transparentTarget.Save(transparentStream,new PngBitmapEncoderOptions()); transparentStream.Position=0;
            using var transparent=SKBitmap.Decode(transparentStream);
            Check(transparent.GetPixel(55,10).Alpha==255,"transparent gradients expose an opaque checkerboard instead of disappearing into the window");
            var count=preview.RenderCount; preview.Show(MaterialPresets.Gradients[^1],124,40);
            Check(preview.RenderCount==count+1,"a preview regenerated at a new size cannot reuse an incorrectly sized cached bitmap");
        }
    }
}
