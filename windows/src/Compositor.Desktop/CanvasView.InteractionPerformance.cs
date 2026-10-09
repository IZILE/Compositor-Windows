using System.Diagnostics;
using Avalonia.Media.Imaging;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    internal void MeasureDraftPerformance(RenderTargetBitmap target, List<object> results)
    {
        void Measure(string name, Action<int> update)
        {
            var samples = new List<double>();
            for (var index = 0; index < 17; index++)
            {
                var timer = Stopwatch.StartNew(); update(index); InvalidateVisual(); target.Render(this);
                if (index > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort(); results.Add(new { name, median_ms = samples[8], p95_ms = samples[^1] });
            Console.WriteLine($"MEASURE: {name}: median {samples[8]:0.00} ms, worst {samples[^1]:0.00} ms");
        }
        var oldSelection = Selection;
        try
        {
            Selection = SelectionTool.Rectangle; _selecting = true;
            Measure("marquee-draft", index => _selectionBox = SKRectI.Create(200, 200, 700 + index * 2, 500));
            _lasso.Clear(); Selection = SelectionTool.Lasso;
            foreach (var index in Enumerable.Range(0, 4096))
                _lasso.Add(new SKPoint((float)(1000 + 700 * Math.Cos(index * 0.004)), (float)(750 + 500 * Math.Sin(index * 0.005))));
            Measure("lasso-draft-4096", _ => { });
            _selecting = false; _lasso.Clear();
            PreviewGradient(new SKPoint(200, 300), new SKPoint(1200, 700));
            Measure("gradient-line", index => _gradientEnd = new SKPoint(1200 + index * 2, 700));
            _gradientDrag = false;
            ShapePreviewFor = box => (new LayerShapeStyle { Kind = ShapeKind.Rectangle, Red = 0.3, Green = 0.6, Blue = 0.9, CornerRadius = 20 }, box);
            PreviewShape(SKRectI.Create(200, 200, 1600, 1000));
            Measure("shape-preview-repeated", _ => { });
            Measure("shape-preview-resize", index => _shapeBox = SKRectI.Create(200, 200, 1600 + index, 1000 + index));
        }
        finally
        {
            Selection = oldSelection; _selecting = false; _selectionBox = null; _lasso.Clear();
            _gradientDrag = false; _shaping = false; ShapePreviewFor = null;
        }
    }
}
