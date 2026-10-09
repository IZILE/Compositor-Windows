using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class WarpCoverageTests
{
    private static bool Original(SKPoint point, IEnumerable<SKPoint> dabs, double radius) =>
        dabs.Any(dab => Math.Sqrt(Math.Pow(point.X - dab.X,2) + Math.Pow(point.Y - dab.Y,2)) <= radius);

    [Theory]
    [InlineData(3.0)]
    [InlineData(8.55)]
    [InlineData(22.0)]
    [InlineData(1002.0)]
    public void IndexedCandidatesMatchTheOriginalSearchAtCellEdgesAndRandomPoints(double radius)
    {
        var random = new Random(7912);
        var dabs = Enumerable.Range(0,257).Select(_ => new SKPoint(
            (float)((random.NextDouble()-0.5)*radius*40), (float)((random.NextDouble()-0.5)*radius*40))).ToArray();
        var index = new WarpCoverageIndex(radius);
        foreach (var dab in dabs) index.Add(dab);
        foreach (var dab in dabs)
        foreach (var direction in new[] {-1f, 1f})
        {
            var edge = dab.X + direction*(float)radius;
            foreach (var x in new[] {MathF.BitDecrement(edge),edge,MathF.BitIncrement(edge)})
            {
                var query = new SKPoint(x,dab.Y);
                Assert.Equal(Original(query,dabs,radius),index.Covers(query));
            }
        }
        for (var sample=0;sample<4096;sample++)
        {
            var point = new SKPoint((float)((random.NextDouble()-0.5)*radius*50),
                (float)((random.NextDouble()-0.5)*radius*50));
            Assert.Equal(Original(point,dabs,radius),index.Covers(point));
        }
    }
    [Theory]
    [InlineData(-1e20f)]
    [InlineData(-100000000f)]
    [InlineData(0f)]
    [InlineData(100000000f)]
    [InlineData(1e20f)]
    public void HugeCoordinatesAndDuplicateDabsPreserveMembership(float origin)
    {
        var radius = 22.0;
        var dabs = new[] {new SKPoint(origin,origin),new SKPoint(origin,origin),new SKPoint(-origin,origin)};
        var index = new WarpCoverageIndex(radius);
        foreach (var dab in dabs) index.Add(dab);
        foreach (var point in new[] {dabs[0],dabs[2],new SKPoint(MathF.BitIncrement(origin),origin),
            new SKPoint(origin+(float)radius,origin),new SKPoint(origin+44,origin+44)})
            Assert.Equal(Original(point,dabs,radius),index.Covers(point));
    }
}
