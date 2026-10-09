using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>Original vector geometry for the built-in, editable general-purpose shapes.</summary>
public static class ShapePaths
{
    public static SKPath Create(ShapeKind kind, SKRect bounds)
    {
        using var path = new SKPathBuilder();
        SKPoint P(double x, double y) => new(bounds.Left + (float)x * bounds.Width, bounds.Top + (float)y * bounds.Height);
        void Polygon(params (double X, double Y)[] points)
        {
            path.MoveTo(P(points[0].X, points[0].Y));
            foreach (var point in points.Skip(1)) path.LineTo(P(point.X, point.Y));
            path.Close();
        }
        void Regular(int sides, bool star = false)
        {
            var count = star ? sides * 2 : sides;
            var points = Enumerable.Range(0, count).Select(i =>
            {
                var angle = -Math.PI / 2 + i * Math.PI * 2 / count;
                var radius = star && i % 2 != 0 ? 0.43 : 1;
                return (X: Math.Cos(angle) * radius, Y: Math.Sin(angle) * radius);
            }).ToArray();
            var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
            Polygon(points.Select(p => ((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray());
        }
        switch (kind)
        {
            case ShapeKind.Triangle: Polygon((0.5, 0), (1, 1), (0, 1)); break;
            case ShapeKind.RightTriangle: Polygon((0, 0), (1, 1), (0, 1)); break;
            case ShapeKind.Diamond: Polygon((0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)); break;
            case ShapeKind.Pentagon: Regular(5); break;
            case ShapeKind.Hexagon: Regular(6); break;
            case ShapeKind.Octagon: Regular(8); break;
            case ShapeKind.Star: Regular(5, true); break;
            case ShapeKind.Heart:
                path.MoveTo(P(0.5, 1));
                path.CubicTo(P(0.34, 0.83), P(0, 0.56), P(0, 0.26));
                path.CubicTo(P(0, 0), P(0.34, -0.1), P(0.5, 0.18));
                path.CubicTo(P(0.66, -0.1), P(1, 0), P(1, 0.26));
                path.CubicTo(P(1, 0.56), P(0.66, 0.83), P(0.5, 1)); path.Close(); break;
            case ShapeKind.ArrowRight: Arrow(false, false); break;
            case ShapeKind.ArrowLeft: Arrow(true, false); break;
            case ShapeKind.ArrowUp: Arrow(true, true); break;
            case ShapeKind.ArrowDown: Arrow(false, true); break;
            case ShapeKind.DoubleArrow:
                Polygon((0, 0.5), (0.28, 0), (0.28, 0.32), (0.72, 0.32), (0.72, 0),
                    (1, 0.5), (0.72, 1), (0.72, 0.68), (0.28, 0.68), (0.28, 1)); break;
            case ShapeKind.SpeechBubble:
                path.MoveTo(P(0.12, 0)); path.LineTo(P(0.88, 0)); path.QuadTo(P(1, 0), P(1, 0.12));
                path.LineTo(P(1, 0.63)); path.QuadTo(P(1, 0.75), P(0.88, 0.75));
                path.LineTo(P(0.42, 0.75)); path.LineTo(P(0.18, 1)); path.LineTo(P(0.23, 0.75));
                path.LineTo(P(0.12, 0.75)); path.QuadTo(P(0, 0.75), P(0, 0.63));
                path.LineTo(P(0, 0.12)); path.QuadTo(P(0, 0), P(0.12, 0)); path.Close(); break;
            case ShapeKind.Plus:
                Polygon((0.33, 0), (0.67, 0), (0.67, 0.33), (1, 0.33), (1, 0.67), (0.67, 0.67),
                    (0.67, 1), (0.33, 1), (0.33, 0.67), (0, 0.67), (0, 0.33), (0.33, 0.33)); break;
            default: path.AddRect(bounds); break;
        }
        return path.Detach();

        void Arrow(bool reverse, bool vertical)
        {
            (double X, double Y)[] points = [(0, 0.3), (0.6, 0.3), (0.6, 0), (1, 0.5), (0.6, 1), (0.6, 0.7), (0, 0.7)];
            Polygon(points.Select(p =>
            {
                var x = reverse ? 1 - p.X : p.X;
                return vertical ? (p.Y, x) : (x, p.Y);
            }).ToArray());
        }
    }
}
