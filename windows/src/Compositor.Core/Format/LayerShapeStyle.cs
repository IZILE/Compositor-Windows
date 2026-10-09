namespace Compositor.Core.Format;

/// <summary>
/// What a shape layer draws, kept so the shape can be drawn again at a new size. Its PNG is still an
/// ordinary raster; once anything else changes those pixels the metadata is dropped.
/// </summary>
public sealed class LayerShapeStyle
{
    public ShapeKind Kind { get; set; }
    public double Red { get; set; }
    public double Green { get; set; }
    public double Blue { get; set; }

    /// <summary>Document pixels, whatever size the shape is scaled to.</summary>
    public double CornerRadius { get; set; }

    /// <summary>A line's thickness, and its two ends as fractions of the layer's box. Nil on other shapes.</summary>
    public double? LineWidth { get; set; }

    public JsonPoint? Start { get; set; }
    public JsonPoint? End { get; set; }

    /// <summary>Normalized, single-fill SVG path; available from the Windows format extension 12.</summary>
    public string? PathData { get; set; }
    public bool? EvenOdd { get; set; }

    public bool IsValid
    {
        get
        {
            if (!Enum.IsDefined(Kind) || new[] {Red,Green,Blue}.Any(v=>!double.IsFinite(v) || v is < 0 or > 1)
                || !double.IsFinite(CornerRadius) || CornerRadius < 0 || LineWidth is { } width && (!double.IsFinite(width) || width <= 0)) return false;
            if (Kind != ShapeKind.Custom) return PathData is null && EvenOdd is null;
            if (PathData is not { Length: > 0 and <= 2 * 1024 * 1024 }) return false;
            using var path = SkiaSharp.SKPath.ParseSvgPathData(PathData);
            return path is {IsEmpty:false} && path.Points.All(p=>float.IsFinite(p.X) && float.IsFinite(p.Y) && Math.Abs(p.X)<=2 && Math.Abs(p.Y)<=2);
        }
    }
}
