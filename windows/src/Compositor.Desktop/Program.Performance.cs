using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    // Real CPU composition and drawing, with no controlled animation clock. This measures work in a
    // headless renderer, not input latency, display refresh or GPU presentation on a physical desktop.
    private static int Performance(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        using var document = new CanvasDocument(Guid.NewGuid(), 2048, 1536);
        document.Layers.Add(Solid(new SKColor(35, 60, 95), 0, 0, 2048, 1536));
        for (var index = 0; index < 5; index++)
            document.Layers.Add(Solid(new SKColor((byte)(50 + index * 30), 100, 180, 210),
                index * 140, index * 90, 1100, 900, opacity: 0.75));
        var view = new CanvasView();
        view.Measure(new Size(1200, 760)); view.Arrange(new Rect(0, 0, 1200, 760));
        view.Document = document;
        using var target = new RenderTargetBitmap(new PixelSize(1200, 760));
        target.Render(view);
        foreach (var message in CanvasView.RasterCacheSelfCheck()) Console.WriteLine("PASS: " + message);
        var results = new List<object>();
        void Measure(string name, Action<int> change)
        {
            var samples = new List<double>();
            for (var index = 0; index < 16; index++)
            {
                var timer = Stopwatch.StartNew(); change(index); target.Render(view); timer.Stop();
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            results.Add(new { name, median_ms = samples[samples.Count / 2], p95_ms = samples[^1] });
            Console.WriteLine($"MEASURE: {name}: median {samples[samples.Count / 2]:0.00} ms, worst {samples[^1]:0.00} ms");
        }
        Measure("overlay-redraw", index => { view.ShowsGuides = index % 2 == 0; view.InvalidateVisual(); });
        var initial = view.Viewport;
        Measure("pan", index => view.RestoreViewport((initial.Zoom, initial.OriginX + index * 2, initial.OriginY + index)));
        Measure("zoom", index => view.RestoreViewport((initial.Zoom * (1 + index * 0.012), initial.OriginX, initial.OriginY)));
        Measure("layer-opacity", index => { document.Layers[^1].Opacity = 0.2 + index * 0.04; view.InvalidateVisual(); });
        Measure("layer-transform", index => { var layer = document.Layers[^1]; layer.Transform = layer.Transform with { X = 80 + index * 2 }; view.InvalidateVisual(); });
        view.MeasureDraftPerformance(target, results);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { document = "2048x1536, 6 raster layers", renderer = "Avalonia headless / real Skia CPU", results },
            new JsonSerializerOptions { WriteIndented = true }));
        view.Document = null;
        return 0;
    }
}
