using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Document;

public sealed record ShapePreset(string Name, ShapeKind Kind, string? PathData = null, bool EvenOdd = false, double AspectRatio = 1);
public sealed record GradientColorStop(double Offset, uint Color);
public sealed record GradientPreset(string Name, GradientColorStop[] Stops)
{
    public SKColor Sample(double position)
    {
        var at = Math.Clamp(position, 0, 1);
        if (at < Stops[0].Offset) return new SKColor(Stops[0].Color);
        for (var i = 1; i < Stops.Length; i++)
        {
            if (at >= Stops[i].Offset) continue;
            var left = Stops[i - 1]; var right = Stops[i];
            return GradientEdits.Blend(new SKColor(left.Color), new SKColor(right.Color),
                (at - left.Offset) / (right.Offset - left.Offset));
        }
        return new SKColor(Stops[^1].Color);
    }
    public GradientPreset Reversed() => new(Name, Stops.Reverse().Select(stop => new GradientColorStop(1 - stop.Offset, stop.Color)).ToArray());
}
public sealed record PatternPreset(string Name, int Width, int Height, uint[] Pixels)
{
    public SKColor Sample(SKPoint point)
    {
        var x = ((long)Math.Floor(point.X) % Width + Width) % Width;
        var y = ((long)Math.Floor(point.Y) % Height + Height) % Height;
        return new SKColor(Pixels[y * Width + x]);
    }
}
public sealed record ColorPreset(string Name, uint Color);
public sealed record BrushStyle(string Name, BrushTip? Tip, double? Hardness);

/// <summary>Small, original presets. Large tip catalogs are built only when a picker is first opened.</summary>
public static class MaterialPresets
{
    public static IReadOnlyList<ShapePreset> Shapes { get; } = Enum.GetValues<ShapeKind>().Where(kind => kind != ShapeKind.Custom).Select(kind => new ShapePreset(kind.ToString(), kind)).ToArray();
    public static IReadOnlyList<GradientPreset> Gradients { get; } = [
        Gradient("Black and White", 0xff000000, 0xffffffff), Gradient("Warm Sunset", 0xff392f5a, 0xffd95d39, 0xffffc857),
        Gradient("Ocean", 0xff102a43, 0xff157a9a, 0xff81f4e1), Gradient("Forest", 0xff12372a, 0xff436850, 0xffadbc9f),
        Gradient("Rose", 0xff591c39, 0xffd94f8a, 0xffffc2df), Gradient("Violet", 0xff28205b, 0xff8a4fff, 0xffdbb6ff),
        Gradient("Fire", 0xff1c0905, 0xffc1270e, 0xffff9f1c, 0xfffff0b3), Gradient("Rainbow", 0xffff3838, 0xffffc800, 0xff42c961, 0xff27b5e6, 0xff6547e8, 0xffe74da7),
        Gradient("Gold", 0xff5b3812, 0xffc18c31, 0xffffe5a2, 0xffa66b1a), Gradient("Silver", 0xff343b43, 0xffbbc5d0, 0xfff9fbff, 0xff78828c),
        Gradient("Sepia", 0xff241b14, 0xff8a6746, 0xfff2dfc2), Gradient("Transparent Fade", 0xff4a92ff, 0x004a92ff)
    ];
    public static IReadOnlyList<ColorPreset> Colors { get; } = new uint[] {
        0xff000000,0xff333333,0xff666666,0xff999999,0xffcccccc,0xffffffff,
        0xff8c1c13,0xffef4444,0xfff97316,0xfff59e0b,0xffeab308,0xfffef08a,
        0xff365314,0xff65a30d,0xff22c55e,0xff059669,0xff0d9488,0xff67e8f9,
        0xff0c4a6e,0xff0284c7,0xff3b82f6,0xff1d4ed8,0xff4338ca,0xffa5b4fc,
        0xff581c87,0xff9333ea,0xffd946ef,0xffdb2777,0xfff9a8d4,0xfffecdd3,
        0xff451a03,0xff92400e,0xffc4a484,0xffe7d3b4,0xffffe4c4,0xfffff7ed
    }.Select(color => new ColorPreset($"#{color & 0xffffff:X6}", color)).ToArray();
    private static readonly Lazy<IReadOnlyList<PatternPreset>> PatternList = new(BuildPatterns);
    public static IReadOnlyList<PatternPreset> Patterns => PatternList.Value;
    private static readonly Lazy<IReadOnlyList<BrushStyle>> BrushList = new(BuildBrushes);
    public static IReadOnlyList<BrushStyle> Brushes => BrushList.Value;
    private static GradientPreset Gradient(string name, params uint[] colors) => new(name,
        colors.Select((color, i) => new GradientColorStop((double)i / (colors.Length - 1), color)).ToArray());
    private static IReadOnlyList<PatternPreset> BuildPatterns()
    {
        var list = new List<PatternPreset>();
        void Tile(string name, Func<int, int, bool> ink, uint dark = 0xff59616c, uint light = 0xffe6e8ec) =>
            list.Add(new PatternPreset(name, 32, 32, Enumerable.Range(0, 1024).Select(i => ink(i % 32, i / 32) ? dark : light).ToArray()));
        Tile("Checkerboard", (x,y) => (x / 8 + y / 8) % 2 == 0);
        Tile("Fine Checker", (x,y) => (x / 2 + y / 2) % 2 == 0);
        Tile("Dots", (x,y) => Math.Pow(x % 16 - 7.5, 2) + Math.Pow(y % 16 - 7.5, 2) < 9);
        Tile("Diagonal Stripes", (x,y) => (x + y) % 16 < 4);
        Tile("Crosshatch", (x,y) => (x + y) % 16 < 2 || (x - y + 32) % 16 < 2);
        Tile("Grid", (x,y) => x % 16 == 0 || y % 16 == 0);
        Tile("Horizontal Lines", (x,y) => y % 8 < 2);
        Tile("Vertical Lines", (x,y) => x % 8 < 2);
        Tile("Brick", (x,y) => y % 8 == 0 || (x + (y / 8 % 2) * 8) % 16 == 0, 0xffa09b95, 0xffa95c48);
        Tile("Woven", (x,y) => (x / 4 + y / 4) % 2 == 0 ? x % 4 == 0 : y % 4 == 0);
        return list;
    }
    private static IReadOnlyList<BrushStyle> BuildBrushes()
    {
        var list = new List<BrushStyle> { new("Hard Round", null, 1), new("Soft Round", null, 0) };
        void Tip(string name, Func<double,double,double> coverage, double spacing = .2)
        {
            var pixels = Enumerable.Range(0, 64 * 64).Select(i => (byte)Math.Round(Math.Clamp(
                coverage((i % 64 - 31.5) / 32, (i / 64 - 31.5) / 32), 0, 1) * 255)).ToArray();
            list.Add(new(name, new BrushTip(name, 64, 64, pixels, spacing), null));
        }
        double Noise(double x, double y) { var n = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453; return n - Math.Floor(n); }
        Tip("Pencil", (x,y) => x*x+y*y < .2 ? .55 + Noise(x,y)*.45 : 0, .12);
        Tip("Marker", (x,y) => Math.Abs(x) < .72 && Math.Abs(y) < .45 ? .85 : 0, .12);
        Tip("Flat Brush", (x,y) => Math.Abs(x) < .9 && Math.Abs(y) < .23 ? .55 + .45*Noise(x,y) : 0, .1);
        Tip("Calligraphy", (x,y) => Math.Abs(x+y) < .22 && Math.Abs(x-y) < 1.2 ? 1 : 0, .12);
        Tip("Airbrush", (x,y) => Math.Pow(Math.Max(0, 1-Math.Sqrt(x*x+y*y)), 2), .1);
        Tip("Chalk", (x,y) => x*x+y*y < .75 && Noise(x,y) > .35 ? .55 : 0, .1);
        Tip("Stipple", (x,y) => x*x+y*y < .85 && Noise(x,y) > .86 ? 1 : 0, .4);
        Tip("Spray", (x,y) => x*x+y*y < .9 && Noise(x,y) > .91 + .07*(x*x+y*y) ? 1 : 0, .35);
        Tip("Square", (x,y) => Math.Abs(x) < .8 && Math.Abs(y) < .8 ? 1 : 0, .2);
        Tip("Leaf Stamp", (x,y) => Math.Pow(x*1.6,2) + y*y < .85 && Math.Abs(x-y*.25) < .55 ? 1 : 0, 1);
        Tip("Crosshatch Brush", (x,y) => x*x+y*y < .85 && (Math.Abs((x+y)*8 % 2) < .3 || Math.Abs((x-y)*8 % 2) < .3) ? .8 : 0, .3);
        using var star = ShapePaths.Create(ShapeKind.Star, SKRect.Create(0,0,64,64));
        Tip("Star Stamp", (x,y) => star.Contains((float)((x+1)*32), (float)((y+1)*32)) ? 1 : 0, 1);
        return list;
    }
}
