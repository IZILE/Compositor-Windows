using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

// Fixed CPU work and original pixel digests; this does not time physical pointer-to-screen latency.
internal static class StrokeToolPerformance
{
    internal static int Run(string output)
    {
        var results = new List<object>();
        var path = Enumerable.Range(0, 128).Select(i => new SKPoint(200 + i * 4,
            (float)(384 + 90 * Math.Sin(i * 0.09)))).ToArray();
        static string Hash(SKBitmap image) => Convert.ToHexStringLower(SHA256.HashData(image.GetPixelSpan()));
        var originalPixels = new Dictionary<string, string>
        {
            ["Clone"] = "ccf1f624ebaa7b8652e160cc21671e09b6fc680c80a8edaf041272a766e2190b",
            ["Blur"] = "5a497e8f0437075c800942bb3f478378ca6fd5ba68c4efa4a440403b7d0a8089",
            ["Liquify"] = "2cec7a52ef65a7b4d264794d28d42384658f0b92c6956ec1c05440faf8f06438",
            ["Smudge"] = "50909ad07b0f07cf6dea9814b1a26fd8419aaae4404839c03acf7d65133fcdb2",
        };
        foreach (var mode in new[] {"Clone", "Blur", "Liquify", "Smudge"})
        {
            var timings = new List<double>(); var hash = "";
            for (var repeat = 0; repeat < 4; repeat++)
            {
                using var document = Picture(); var layer = document.Layers[0]; var original = layer.Asset!;
                var settings = new BrushSettings(Diameter:40, Hardness:0.5, Opacity:0.5,
                    Mode:mode == "Clone" ? BrushMode.Clone : mode == "Blur" ? BrushMode.Blur : BrushMode.Paint,
                    CloneFrom:new SKPointI(20,20));
                var history = new DocumentHistory(); history.Begin(mode, document, layer.ID);
                var clock = Stopwatch.StartNew();
                var applied = mode is "Liquify" or "Smudge"
                    ? WarpEdits.Warp(document,layer.ID,path,mode == "Liquify" ? WarpMode.Liquify : WarpMode.Smudge,settings)
                    : BrushEdits.Paint(document,layer.ID,path,settings);
                clock.Stop();
                if (!applied) throw new InvalidOperationException("The diagnostic stroke did not apply.");
                if (repeat > 0) timings.Add(clock.Elapsed.TotalMilliseconds);
                var currentHash = Hash(layer.Asset!.Image);
                if (currentHash != originalPixels[mode]) throw new InvalidOperationException(mode + " differs from the 0.6.3 reference pixels.");
                if (hash.Length > 0 && currentHash != hash) throw new InvalidOperationException("Stroke pixels are not deterministic.");
                hash = currentHash; history.End(document,layer.ID);
                if (history.Undo()!.Value.Document!.Layers[0].Asset != original
                    || Hash(history.Redo()!.Value.Document!.Layers[0].Asset!.Image) != hash)
                    throw new InvalidOperationException("Stroke undo/redo did not preserve its pixels.");
                history.Reset(); original.Dispose();
            }
            timings.Sort(); results.Add(new {name=mode, median_ms=timings[1], worst_ms=timings[^1],pixel_sha256=hash});
            Console.WriteLine($"MEASURE: {mode}: median {timings[1]:0.00} ms; pixels {hash}");
            Console.WriteLine($"PASS: {mode} preserves 0.6.3 pixels and one undo/redo step");
            Write();
        }
        return 0;
        void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output,JsonSerializer.Serialize(new {
                document="1024x768, one generated raster layer, 128-point path, diameter 40, hardness/opacity 0.5",
                measurement="CPU calculation; one warm-up and three measured samples, no physical presentation timing",results
            },new JsonSerializerOptions {WriteIndented=true}));
        }
    }
    internal static CanvasDocument Picture()
    {
        const int width=1024,height=768;
        var document=new CanvasDocument(Guid.NewGuid(),width,height);
        var pixels=new SKBitmap(Bitmaps.ColorInfo(width,height));
        using(var canvas=new SKCanvas(pixels))
        {
            canvas.Clear(new SKColor(80,130,190));
            using var paint=new SKPaint {Color=new SKColor(200,160,100),IsAntialias=true};
            for(var i=0;i<12;i++) canvas.DrawCircle(width*(i+1)/14f,height/2f,12+i*3,paint);
        }
        document.Layers.Add(new ImageLayer(Guid.NewGuid(),ImportedImage.Create(pixels,"Diagnostic"),
            new LayerTransform(0,0,width,height),"Diagnostic"));
        return document;
    }
}
