using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Compositor.Core.Document;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private readonly BrushCursor _cursorLayer = new();
    private readonly Cursor _brushCrossCursor = new(StandardCursorType.Cross);
    private readonly Cursor _textCursor = new(StandardCursorType.Ibeam);
    private Point? _brushPointer;
    internal Control CursorLayer => _cursorLayer;
    internal Rect? BrushCursorCircle => _cursorLayer.Circle;
    internal Rect? BrushHardnessCircle => _cursorLayer.HardnessCircle;
    internal Point? CloneMarker => _cursorLayer.Marker;
    internal Geometry? BrushCursorOutline => _cursorLayer.Outline;
    internal int BrushContourBuilds => _cursorLayer.ContourBuilds;
    internal void RefreshToolCursor() => UpdateBrushCursor();
    public SkiaSharp.SKPoint? CloneSourcePosition { get; set; }
    internal int PictureRenderCount { get; private set; }

    private void UpdateBrushCursor()
    {
        var shown = PaintEnabled && _document is not null && !EyedropperOnClick;
        var tip = Brush.Tip;
        _cursorLayer.Update(shown ? _brushPointer : null, Math.Max(1, Brush.Diameter * _zoom), tip is null ? Brush.Hardness : 1,
            shown && SampleSourceOnClick && CloneSourcePosition is { } source ? ToScreen(source) : null, tip);
        Cursor = shown || EyedropperOnClick || Selection != SelectionTool.None || ShapeEnabled || GradientEnabled || CropEnabled || UprightDrawing
            ? _brushCrossCursor : TypeOnClick ? _textCursor : null;
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
        private readonly Dictionary<string, Geometry> _contours = [];
        private string? _tipID;
        private double _diameter;
        internal Geometry? Outline { get; private set; }
        internal int ContourBuilds { get; private set; }
        internal BrushCursor() { IsHitTestVisible = false; ClipToBounds = true; }
        internal void Update(Point? point, double diameter, double hardness, Point? marker, BrushTip? tip)
        {
            var ratioX = tip is null ? 1 : (double)tip.Width / Math.Max(tip.Width, tip.Height);
            var ratioY = tip is null ? 1 : (double)tip.Height / Math.Max(tip.Width, tip.Height);
            Rect? next = point is { } at ? new Rect(at.X - diameter * ratioX / 2, at.Y - diameter * ratioY / 2, diameter * ratioX, diameter * ratioY) : null;
            Rect? inner = next is { } circle && hardness > 0 && hardness < 1 ? circle.Deflate(circle.Width * (1 - hardness) / 2) : null;
            if (Circle == next && HardnessCircle == inner && Marker == marker && _tipID == tip?.ID && _diameter == diameter) return;
            var tipChanged = _tipID != tip?.ID;
            if (tipChanged)
            {
                Outline = null;
                if (tip is not null)
                {
                    if (!_contours.TryGetValue(tip.ID, out var contour))
                    {
                        contour = BrushTipOutline.Create(tip); ContourBuilds++;
                        if (_contours.Count >= 16) _contours.Remove(_contours.Keys.First());
                        _contours[tip.ID] = contour;
                    }
                    Outline = contour;
                }
                _tipID = tip?.ID;
            }
            if (Outline is not null && (_diameter != diameter || tipChanged || Outline.Transform is null))
                Outline.Transform = new MatrixTransform(Matrix.CreateScale(diameter, diameter));
            _diameter = diameter;
            Circle = next; HardnessCircle = inner; Marker = marker; InvalidateVisual();
        }
        public override void Render(DrawingContext context)
        {
            if (Circle is not { } circle) return;
            // BrushCursorOverlay.swift draws a white 2.5-point rim under a black one-point rim.
            if (Outline is { } contour)
            {
                using (context.PushTransform(Matrix.CreateTranslation(circle.Center.X, circle.Center.Y)))
                { context.DrawGeometry(null, OuterWhite, contour); context.DrawGeometry(null, OuterBlack, contour); }
            }
            else
            {
                var oval = new EllipseGeometry(circle);
                context.DrawGeometry(null, OuterWhite, oval); context.DrawGeometry(null, OuterBlack, oval);
            }
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
