using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

internal static partial class Program
{
    // Deterministic CPU work, including actual routed pointer input. This is not a physical
    // display's frame rate or pen latency. Pixel hashes let the before/after runs check quality.
    private static int BrushPerformance(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        var results = new List<object>();
        static string Hash(SKBitmap bitmap) => Convert.ToHexStringLower(SHA256.HashData(bitmap.GetPixelSpan()));
        static SKPoint[] Path(int count, int width, int height) => Enumerable.Range(0, count).Select(index =>
        {
            var t = index * 18.0 / (count - 1);
            return new SKPoint((float)(width * (0.5 + 0.35 * Math.Cos(t))),
                (float)(height * (0.5 + 0.35 * Math.Sin(t * 1.31))));
        }).ToArray();
        var stroke = Path(1024, 2048, 1536);
        foreach (var diameter in new[] { 40, 200 })
        foreach (var hardness in new[] { 1.0, 0.0 })
        foreach (var erase in new[] { false, true })
        {
            using var document = new CanvasDocument(Guid.NewGuid(), 2048, 1536);
            var layer = Solid(new SKColor(80, 130, 190, 210), 0, 0, 2048, 1536);
            document.Layers.Add(layer);
            var original = layer.Asset!;
            var settings = new BrushSettings(Diameter: diameter, Hardness: hardness, Red: 0.9, Green: 0.2, Blue: 0.1,
                Opacity: 0.35, Erasing: erase);
            var samples = new List<double>(); var histories = new List<double>();
            var hash = "";
            for (var iteration = 0; iteration < 3; iteration++)
            {
                layer.Asset = original;
                var history = new DocumentHistory(); history.Begin(erase ? "Erase" : "Brush", document, layer.ID);
                var timer = Stopwatch.StartNew();
                if (!BrushEdits.Paint(document, layer.ID, stroke, settings)) throw new InvalidOperationException("Stroke did not apply.");
                samples.Add(timer.Elapsed.TotalMilliseconds); timer.Restart();
                history.End(document, layer.ID); histories.Add(timer.Elapsed.TotalMilliseconds);
                hash = Hash(layer.Asset!.Image);
                if (history.Undo()?.Document?.Layers[0].Asset != original
                    || Hash(history.Redo()!.Value.Document!.Layers[0].Asset!.Image) != hash)
                    throw new InvalidOperationException("Brush undo/redo changed pixels.");
                history.Reset(); layer.Asset.Dispose();
            }
            layer.Asset = original;
            samples.Sort(); histories.Sort();
            using var prepared = BrushEdits.Prepare(document, layer.ID, settings)!;
            var updates = new List<double>();
            foreach (var point in stroke)
            {
                var update = Stopwatch.StartNew(); prepared.Append(point); updates.Add(update.Elapsed.TotalMilliseconds);
            }
            var finish = Stopwatch.StartNew();
            var committed = prepared.TryCommit(document, layer.ID, settings);
            var finishMilliseconds = finish.Elapsed.TotalMilliseconds;
            if (!committed || Hash(layer.Asset!.Image) != hash)
                throw new InvalidOperationException("Incremental brush output does not match the complete stroke.");
            updates.Sort(); layer.Asset!.Dispose(); layer.Asset = original;
            var name = $"{(erase ? "erase" : "paint")}-{diameter}-{(hardness == 1 ? "hard" : "soft")}";
            results.Add(new { name, apply_median_ms = samples[1], apply_worst_ms = samples[^1], history_median_ms = histories[1], pixel_sha256 = hash,
                incremental_update_median_ms = updates[updates.Count / 2], incremental_update_p95_ms = updates[(int)(updates.Count * 0.95)],
                incremental_update_worst_ms = updates[^1], release_ms = finishMilliseconds });
            Console.WriteLine($"MEASURE: {name}: apply {samples[1]:0.00} ms; history {histories[1]:0.00} ms; pixels {hash}");
            Console.WriteLine($"MEASURE: {name}: incremental input median {updates[updates.Count / 2]:0.00} ms, p95 {updates[(int)(updates.Count * 0.95)]:0.00} ms; release {finishMilliseconds:0.00} ms");
        }
        using (var document = new CanvasDocument(Guid.NewGuid(), 2048, 1536))
        {
            document.Layers.Add(Solid(SKColors.White, 0, 0, 2048, 1536));
            var view = new CanvasView { Document = document, PaintEnabled = true, Brush = new BrushSettings(Diameter: 40) };
            var window = new Window { Width = 1200, Height = 760, Content = view }; window.Show();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            using var target = new RenderTargetBitmap(new PixelSize(1200, 760));
            target.Render(view);
            var path = Path(5000, 1200, 760);
            window.MouseDown(new Point(path[0].X, path[0].Y), MouseButton.Left, RawInputModifiers.LeftMouseButton);
            for (var index = 1; index < path.Length; index++)
            {
                var point = new Point(path[index].X, path[index].Y);
                window.MouseMove(point, RawInputModifiers.LeftMouseButton);
                if (index is not (256 or 1024 or 4096)) continue;
                var samples = new List<double>();
                for (var repeat = 0; repeat < 9; repeat++)
                {
                    var timer = Stopwatch.StartNew(); view.InvalidateVisual(); target.Render(view); samples.Add(timer.Elapsed.TotalMilliseconds);
                }
                samples.Sort();
                results.Add(new { name = $"draft-{index}-points", render_median_ms = samples[4], render_worst_ms = samples[^1] });
                Console.WriteLine($"MEASURE: draft-{index}-points: {samples[4]:0.00} ms");
            }
            window.MouseUp(new Point(path[^1].X, path[^1].Y), MouseButton.Left, RawInputModifiers.None);
            view.Document = null; window.Close();
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { document = "2048x1536, one raster layer, 1024-point 18-radian path",
            renderer = "Avalonia headless / real Skia CPU; no physical presentation timing", results }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
