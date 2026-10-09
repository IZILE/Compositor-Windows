using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.IO;

/// <summary>Interoperable, bounded imports; unsupported rendering rules are reported instead of silently discarded.</summary>
public static class MaterialImport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static IReadOnlyList<GradientPreset> Gradients(string path)
    {
        var lines = ReadText(path).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToArray();
        if (lines.Length < 3 || lines[0] != "GIMP Gradient") throw new InvalidDataException("Choose a GIMP GGR gradient file.");
        var name = lines[1].StartsWith("Name:") ? lines[1][5..].Trim() : Path.GetFileNameWithoutExtension(path);
        var countAt = lines[1].StartsWith("Name:") ? 2 : 1;
        if (!int.TryParse(lines[countAt], out var count) || count is < 1 or > 256 || lines.Length != countAt + 1 + count)
            throw new InvalidDataException("The gradient segment count is invalid (maximum 256).");
        var stops = new List<GradientColorStop>(); var previous = 0d;
        for (var i = 0; i < count; i++)
        {
            var values = Numbers(lines[countAt + 1 + i]);
            if (values.Length is not (13 or 15) || values.Any(v => !double.IsFinite(v))) throw new InvalidDataException("The GGR gradient is incomplete.");
            if (values[11] != 0 || values[12] != 0 || values.Length == 15 && (values[13] != 0 || values[14] != 0))
                throw new InvalidDataException("This gradient uses nonlinear, HSV or dynamic colors; only fixed linear RGB GGR segments are supported.");
            var left = values[0]; var middle = values[1]; var right = values[2];
            if (Math.Abs(left - previous) > .000001 || left < 0 || right > 1 || middle <= left || middle >= right || right <= left
                || values[3..11].Any(v => v < 0 || v > 1)) throw new InvalidDataException("The gradient stops must be contiguous and between zero and one.");
            SKColor Color(int at) => new((byte)Math.Round(values[at]*255), (byte)Math.Round(values[at+1]*255),
                (byte)Math.Round(values[at+2]*255), (byte)Math.Round(values[at+3]*255));
            var from = Color(3); var to = Color(7);
            stops.Add(new(left, (uint)from)); stops.Add(new(middle, (uint)GradientEdits.Blend(from, to, .5))); stops.Add(new(right, (uint)to));
            previous = right;
        }
        if (Math.Abs(previous - 1) > .000001) throw new InvalidDataException("The gradient must end at one.");
        return [new GradientPreset(name, stops.ToArray())];
    }

    public static IReadOnlyList<ColorPreset> Colors(string path)
    {
        var lines = ReadText(path).Split('\n').Select(line => line.Trim()).ToArray();
        if (lines[0] != "GIMP Palette") throw new InvalidDataException("Choose a GIMP GPL palette file.");
        var colors = new List<ColorPreset>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("Name:") || line.StartsWith("Columns:")) continue;
            var fields = Regex.Split(line, @"\s+", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (fields.Length < 3 || !byte.TryParse(fields[0], out var r) || !byte.TryParse(fields[1], out var g) || !byte.TryParse(fields[2], out var b))
                throw new InvalidDataException("The palette contains an invalid RGB color.");
            var color = new SKColor(r,g,b); var name = fields.Length > 3 ? string.Join(' ', fields.Skip(3)) : $"#{r:X2}{g:X2}{b:X2}";
            colors.Add(new(name, (uint)color));
            if (colors.Count > 2048) throw new InvalidDataException("A palette may contain at most 2048 colors.");
        }
        if (colors.Count == 0) throw new InvalidDataException("The palette has no colors.");
        return colors;
    }

    public static IReadOnlyList<PatternPreset> Patterns(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("A pattern image may be at most 16 MB.");
        using var stream = File.OpenRead(path); using var codec = SKCodec.Create(stream);
        if (codec is null || codec.Info.Width is < 1 or > 2048 || codec.Info.Height is < 1 or > 2048)
            throw new InvalidDataException("Choose a PNG, JPEG or WebP pattern up to 2048 × 2048 pixels.");
        using var pixels = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (pixels is null) throw new InvalidDataException("The pattern image could not be decoded.");
        return [new PatternPreset(Path.GetFileNameWithoutExtension(path), pixels.Width, pixels.Height,
            pixels.Pixels.Select(color => (uint)color).ToArray())];
    }

    public static IReadOnlyList<ShapePreset> Shapes(string path)
    {
        var text = ReadText(path);
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name.LocalName != "svg") throw new InvalidDataException("Choose an SVG shape file.");
        if (root.Descendants().Count() > 2048) throw new InvalidDataException("The SVG contains too many elements.");
        using var result = new SKPath();
        Visit(root, SKMatrix.CreateIdentity(), "black", "nonzero");
        if (result.IsEmpty || result.TightBounds is not { Width: > 0, Height: > 0 } bounds)
            throw new InvalidDataException("The SVG has no filled shape.");
        if (!Finite(bounds.Left) || !Finite(bounds.Top) || !Finite(bounds.Right) || !Finite(bounds.Bottom))
            throw new InvalidDataException("The SVG coordinates exceed the supported range.");
        result.Transform(SKMatrix.CreateTranslation(-bounds.Left, -bounds.Top));
        result.Transform(SKMatrix.CreateScale(1 / bounds.Width, 1 / bounds.Height));
        var aspect = (double)bounds.Width / bounds.Height;
        if (!double.IsFinite(aspect) || aspect is < .001 or > 1000)
            throw new InvalidDataException("The SVG shape's proportions exceed the supported range.");
        return [new ShapePreset(Path.GetFileNameWithoutExtension(path), ShapeKind.Custom, result.ToSvgPathData(), result.FillType == SKPathFillType.EvenOdd,aspect)];

        void Visit(XElement element, SKMatrix parent, string inheritedFill, string inheritedRule)
        {
            var name = element.Name.LocalName;
            if (name is "title" or "desc" or "metadata") return;
            if (name is not ("svg" or "g" or "path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline"))
                throw new InvalidDataException("Only filled SVG paths and basic shapes are supported; text, images, gradients, strokes and references must be converted to paths first.");
            if (element.Attributes().Any(a => a.Name.LocalName is "style" or "class" or "clip-path" or "mask" or "filter"
                || a.Name.LocalName.Contains("href") || a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase))
                || element.Attribute("stroke") is { Value: not "none" })
                throw new InvalidDataException("Convert SVG styles, strokes and effects to filled paths before importing.");
            foreach (var opacity in new[] { "opacity", "fill-opacity" })
                if (element.Attribute(opacity) is { } a && Number(a.Value) != 1)
                    throw new InvalidDataException("SVG shapes use one solid fill; partial SVG opacity is not supported.");
            var rule = (string?)element.Attribute("fill-rule") ?? inheritedRule;
            if (rule is not ("evenodd" or "nonzero")) throw new InvalidDataException("The SVG fill rule is invalid.");
            var matrix = SKMatrix.Concat(parent, Transform((string?)element.Attribute("transform")));
            var fill = (string?)element.Attribute("fill") ?? inheritedFill;
            if (element != root && name == "svg") throw new InvalidDataException("Nested SVG viewports must be flattened before importing.");
            if (element.Attribute("display")?.Value == "none" || element.Attribute("visibility")?.Value is "hidden" or "collapse") return;
            if (fill != "none" && name is not ("svg" or "g"))
            {
                using var part = Geometry(element);
                part.FillType = rule == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
                part.Transform(matrix);
                if (!result.Op(part,SKPathOp.Union,result)) throw new InvalidDataException("The SVG paths could not be combined.");
                if (result.Points.Any(point => !Finite(point.X) || !Finite(point.Y))) throw new InvalidDataException("The SVG coordinates exceed the supported range.");
            }
            foreach (var child in element.Elements()) Visit(child, matrix, fill, rule);
        }
    }
    private static SKPath Geometry(XElement element)
    {
        float V(string key, double fallback = 0) => (float)(element.Attribute(key) is { } a ? Number(a.Value) : fallback);
        if (element.Name.LocalName == "path") return SKPath.ParseSvgPathData((string?)element.Attribute("d") ?? "") ?? throw new InvalidDataException("The SVG path is invalid.");
        using var path = new SKPathBuilder();
        try
        {
            switch (element.Name.LocalName)
            {
                case "rect":
                    var rect = SKRect.Create(V("x"), V("y"), V("width"), V("height"));
                    if (rect.Width <= 0 || rect.Height <= 0) throw new InvalidDataException("SVG dimensions must be positive.");
                    path.AddRoundRect(rect, V("rx", V("ry")), V("ry", V("rx"))); break;
                case "circle": case "ellipse":
                    var rx = element.Name.LocalName == "circle" ? V("r") : V("rx");
                    var ry = element.Name.LocalName == "circle" ? rx : V("ry");
                    if (rx <= 0 || ry <= 0) throw new InvalidDataException("SVG dimensions must be positive.");
                    path.AddOval(SKRect.Create(V("cx")-rx, V("cy")-ry, rx*2, ry*2)); break;
                default:
                    var points = Numbers((string?)element.Attribute("points") ?? "");
                    if (points.Length < 6 || points.Length % 2 != 0) throw new InvalidDataException("The SVG polygon is invalid.");
                    path.MoveTo((float)points[0], (float)points[1]);
                    for (var i=2; i<points.Length; i+=2) path.LineTo((float)points[i], (float)points[i+1]);
                    path.Close(); break;
            }
            return path.Detach();
        }
        catch { throw; }
    }
    private static SKMatrix Transform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SKMatrix.CreateIdentity();
        var result = SKMatrix.CreateIdentity(); var consumed = 0;
        foreach (Match match in Regex.Matches(text, @"([A-Za-z]+)\s*\(([^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            if (text[consumed..match.Index].Trim(' ', '\r', '\n', '\t', ',').Length != 0) throw new InvalidDataException("The SVG transform is invalid.");
            var p = Numbers(match.Groups[2].Value); var name = match.Groups[1].Value;
            var next = name switch {
                "matrix" when p.Length == 6 => new SKMatrix((float)p[0], (float)p[2], (float)p[4], (float)p[1], (float)p[3], (float)p[5], 0,0,1),
                "translate" when p.Length is 1 or 2 => SKMatrix.CreateTranslation((float)p[0], p.Length == 2 ? (float)p[1] : 0),
                "scale" when p.Length is 1 or 2 => SKMatrix.CreateScale((float)p[0], (float)p[^1]),
                "rotate" when p.Length == 1 => SKMatrix.CreateRotationDegrees((float)p[0]),
                "rotate" when p.Length == 3 => SKMatrix.CreateRotationDegrees((float)p[0], (float)p[1], (float)p[2]),
                "skewX" when p.Length == 1 => SKMatrix.CreateSkew((float)Math.Tan(p[0]*Math.PI/180),0),
                "skewY" when p.Length == 1 => SKMatrix.CreateSkew(0,(float)Math.Tan(p[0]*Math.PI/180)),
                _ => throw new InvalidDataException("The SVG transform is not supported.")
            };
            result = SKMatrix.Concat(result, next); consumed = match.Index + match.Length;
        }
        if (text[consumed..].Trim().Length != 0 || consumed == 0) throw new InvalidDataException("The SVG transform is invalid.");
        return result;
    }
    private static bool Finite(float value) => float.IsFinite(value) && Math.Abs(value) <= 1_000_000;
    private static double Number(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, Invariant, out var value) || !double.IsFinite(value) || Math.Abs(value) > 1_000_000)
            throw new InvalidDataException("The material contains an invalid number.");
        return value;
    }
    private static double[] Numbers(string text) => Regex.Split(text.Trim(), @"[\s,]+", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(Number).ToArray();
    private static string ReadText(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("A preset file may be at most 2 MB.");
        return File.ReadAllText(path).TrimStart('\uFEFF');
    }
}
