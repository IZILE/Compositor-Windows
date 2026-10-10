using Avalonia;
using Avalonia.Media;
using Compositor.Core.Document;

namespace Compositor.Desktop;

/// <summary>A bounded marching-squares contour of the visible tip, in diameter-one document coordinates.</summary>
internal static class BrushTipOutline
{
    private readonly record struct Edge(int X, int Y, bool Vertical);
    internal static StreamGeometry Create(BrushTip tip)
    {
        var longest = Math.Max(tip.Width, tip.Height);
        var count = Math.Min(256, longest);
        var nx = Math.Max(1, (int)Math.Ceiling((double)tip.Width * count / longest));
        var ny = Math.Max(1, (int)Math.Ceiling((double)tip.Height * count / longest));
        var rx = (double)tip.Width / longest; var ry = (double)tip.Height / longest;
        double X(int x) => x == 0 ? -rx / 2 : x == nx + 1 ? rx / 2 : ((x - .5) / nx - .5) * rx;
        double Y(int y) => y == 0 ? -ry / 2 : y == ny + 1 ? ry / 2 : ((y - .5) / ny - .5) * ry;
        var maximum = (byte)0;
        foreach (var alpha in tip.Alpha) maximum = Math.Max(maximum, alpha);
        var level = maximum / 255d * .2;
        var values = new double[nx + 2, ny + 2];
        for (var y = 1; y <= ny; y++) for (var x = 1; x <= nx; x++) values[x, y] = tip.Sample(X(x), Y(y), 1);
        var points = new Dictionary<Edge, Point>();
        var links = new Dictionary<Edge, List<Edge>>();
        var crossings = new List<Edge>(4);
        void Link(Edge a, Edge b)
        {
            if (!links.TryGetValue(a, out var from)) links[a] = from = new List<Edge>(2);
            if (!links.TryGetValue(b, out var to)) links[b] = to = new List<Edge>(2);
            from.Add(b); to.Add(a);
        }
        for (var y = 0; y <= ny; y++) for (var x = 0; x <= nx; x++)
        {
            crossings.Clear();
            void Cross(int x1, int y1, int x2, int y2, Edge edge)
            {
                var a = values[x1, y1]; var b = values[x2, y2];
                if ((a >= level) == (b >= level)) return;
                var t = (level - a) / (b - a);
                points.TryAdd(edge, new Point(X(x1) + (X(x2) - X(x1)) * t, Y(y1) + (Y(y2) - Y(y1)) * t));
                crossings.Add(edge);
            }
            Cross(x, y, x + 1, y, new Edge(x, y, false));
            Cross(x + 1, y, x + 1, y + 1, new Edge(x + 1, y, true));
            Cross(x, y + 1, x + 1, y + 1, new Edge(x, y + 1, false));
            Cross(x, y, x, y + 1, new Edge(x, y, true));
            if (crossings.Count == 2) Link(crossings[0], crossings[1]);
            else if (crossings.Count == 4)
            {
                var center = (values[x, y] + values[x + 1, y] + values[x, y + 1] + values[x + 1, y + 1]) / 4;
                if ((values[x, y] >= level) == (center >= level))
                { Link(crossings[0], crossings[1]); Link(crossings[2], crossings[3]); }
                else { Link(crossings[0], crossings[3]); Link(crossings[1], crossings[2]); }
            }
        }
        var geometry = new StreamGeometry();
        using var path = geometry.Open();
        var visited = new HashSet<Edge>();
        foreach (var start in links.Keys)
        {
            if (!visited.Add(start)) continue;
            path.BeginFigure(points[start], isFilled: false);
            var previous = start; var current = links[start][0];
            while (current != start && visited.Add(current))
            {
                path.LineTo(points[current]);
                var next = links[current].First(edge => edge != previous);
                previous = current; current = next;
            }
            path.EndFigure(isClosed: current == start);
        }
        return geometry;
    }
}
