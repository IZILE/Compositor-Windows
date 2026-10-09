using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private readonly BrushCursor _cursorLayer = new();
    private readonly Cursor _brushCrossCursor = new(StandardCursorType.Cross);
    private Point? _brushPointer;
    internal Control CursorLayer => _cursorLayer;
    internal Rect? BrushCursorCircle => _cursorLayer.Circle;
    internal Rect? BrushHardnessCircle => _cursorLayer.HardnessCircle;
    internal Point? CloneMarker => _cursorLayer.Marker;
    public SkiaSharp.SKPoint? CloneSourcePosition { get; set; }
    internal int PictureRenderCount { get; private set; }

    private void UpdateBrushCursor()
    {
        var shown = PaintEnabled && _document is not null && !EyedropperOnClick;
        _cursorLayer.Update(shown ? _brushPointer : null, Math.Max(1, Brush.Diameter * _zoom), Brush.Hardness,
            shown && SampleSourceOnClick && CloneSourcePosition is { } source ? ToScreen(source) : null);
        Cursor = shown ? _brushCrossCursor : null;
    }

    /// <summary>A separate retained visual: moving a brush tip never composites the picture again.</summary>
    private sealed class BrushCursor : Control
    {
        internal Rect? Circle { get; private set; }
        internal Rect? HardnessCircle { get; private set; }
        internal Point? Marker { get; private set; }
        private static readonly Pen OuterWhite = new(Brushes.White, 2.5), OuterBlack = new(Brushes.Black, 1);
        private static readonly Pen InnerWhite = new(Brushes.White, 2.5) { DashStyle = new DashStyle([4.0, 3.0], 0) };
        private static readonly Pen InnerBlack = new(Brushes.Black, 1) { DashStyle = new DashStyle([4.0, 3.0], 0) };
        private static readonly Pen MarkerWhite = new(Brushes.White, 3), MarkerBlack = new(Brushes.Black, 1);
        internal BrushCursor() { IsHitTestVisible = false; ClipToBounds = true; }
        internal void Update(Point? point, double diameter, double hardness, Point? marker)
        {
            Rect? next = point is { } at ? new Rect(at.X - diameter / 2, at.Y - diameter / 2, diameter, diameter) : null;
            Rect? inner = next is { } circle && hardness > 0 && hardness < 1 ? circle.Deflate(circle.Width * (1 - hardness) / 2) : null;
            if (Circle == next && HardnessCircle == inner && Marker == marker) return;
            Circle = next; HardnessCircle = inner; Marker = marker; InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            if (Circle is not { } circle) return;
            // BrushCursorOverlay.swift draws a white 2.5-point rim under a black one-point rim.
            var oval = new EllipseGeometry(circle);
            context.DrawGeometry(null, OuterWhite, oval);
            context.DrawGeometry(null, OuterBlack, oval);
            if (HardnessCircle is { } inner)
            {
                var dashed = new EllipseGeometry(inner);
                context.DrawGeometry(null, InnerWhite, dashed); context.DrawGeometry(null, InnerBlack, dashed);
            }
            if (Marker is { } marker)
                foreach (var pen in new[] { MarkerWhite, MarkerBlack })
                {
                    context.DrawLine(pen, new Point(marker.X - 7, marker.Y), new Point(marker.X + 7, marker.Y));
                    context.DrawLine(pen, new Point(marker.X, marker.Y - 7), new Point(marker.X, marker.Y + 7));
                }
        }
    }
}
