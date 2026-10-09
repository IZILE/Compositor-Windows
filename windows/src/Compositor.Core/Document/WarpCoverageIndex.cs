using SkiaSharp;

namespace Compositor.Core.Document;

// The exact circular membership test is unchanged. Cells only narrow the list of candidate dabs.
internal sealed class WarpCoverageIndex(double radius)
{
    private readonly double _radius = radius;
    private readonly double _cellSize = Math.Max(1, radius);
    private readonly bool _indexed = radius > 0 && double.IsFinite(radius) && float.IsFinite((float)radius);
    private readonly List<SKPoint> _dabs = [];
    private readonly Dictionary<(int X, int Y), List<SKPoint>> _cells = [];
    private float _left = float.PositiveInfinity, _top = float.PositiveInfinity;
    private float _right = float.NegativeInfinity, _bottom = float.NegativeInfinity;
    internal int Count => _dabs.Count;

    internal void Add(SKPoint point)
    {
        _dabs.Add(point);
        if (!_indexed || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return;
        _left = Math.Min(_left, point.X); _right = Math.Max(_right, point.X);
        _top = Math.Min(_top, point.Y); _bottom = Math.Max(_bottom, point.Y);
        var cell = (Cell(point.X), Cell(point.Y));
        if (!_cells.TryGetValue(cell, out var points)) _cells.Add(cell, points = []);
        points.Add(point);
    }
    internal bool Covers(SKPoint point)
    {
        if (!_indexed) return _dabs.Any(dab => Contains(point, dab));
        if (_cells.Count == 0 || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return false;
        // Subtract in float, just as the original test does, so this rejection remains conservative.
        if (point.X < _left && _left - point.X > _radius || point.X > _right && point.X - _right > _radius
            || point.Y < _top && _top - point.Y > _radius || point.Y > _bottom && point.Y - _bottom > _radius) return false;
        // A float subtraction can round a point just outside the mathematical circle onto its rim.
        // One float ULP of padding keeps those candidates; the original distance test still decides.
        var reach = (double)MathF.BitIncrement((float)_radius);
        var x0 = Cell((double)point.X - reach); var x1 = Cell((double)point.X + reach);
        var y0 = Cell((double)point.Y - reach); var y1 = Cell((double)point.Y + reach);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            if (!_cells.TryGetValue((x, y), out var candidates)) continue;
            foreach (var dab in candidates) if (Contains(point, dab)) return true;
        }
        return false;
    }
    private int Cell(double value) => (int)Math.Clamp(Math.Floor(value / _cellSize), int.MinValue + 1d, int.MaxValue - 1d);
    private bool Contains(SKPoint point, SKPoint dab) =>
        Math.Sqrt(Math.Pow(point.X - dab.X, 2) + Math.Pow(point.Y - dab.Y, 2)) <= _radius;
}
