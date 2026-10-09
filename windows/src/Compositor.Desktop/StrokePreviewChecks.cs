using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

internal static class StrokePreviewChecks
{
    internal static void Run(List<string> report, string output)
    {
        var points = Enumerable.Range(0, 1024).Select(index => new SKPoint(
            (float)(192 + 150 * Math.Cos(index * 0.027)), (float)(128 + 100 * Math.Sin(index * 0.037)))).ToList();
        foreach (var scale in new[] { 1.0, 1.5, 2.0 })
        {
            using var preview = new StrokePreview(); var size = new Size(384, 256);
            var settings = new BrushSettings(Diameter: 19.5, Red: 0.2, Green: 0.6, Blue: 0.9);
            foreach (var count in new[] { 2, 17, 256, 1024 })
            {
                var samples = points.Take(count).ToArray();
                var cached = new Cached { Preview = preview, Points = samples, Settings = settings, Scale = scale };
                var reference = new Reference { Points = samples, Settings = settings };
                Control Scaled(Control control)
                {
                    control.Width = size.Width; control.Height = size.Height;
                    control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                    control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
                    control.RenderTransformOrigin = RelativePoint.TopLeft;
                    control.RenderTransform = new ScaleTransform(scale, scale);
                    var host = new Border { Child = control };
                    var physical = new Size(size.Width * scale, size.Height * scale);
                    host.Measure(physical); host.Arrange(new Rect(physical)); return host;
                }
                var cachedHost = Scaled(cached); var referenceHost = Scaled(reference);
                using var actual = new RenderTargetBitmap(new PixelSize((int)(384 * scale), (int)(256 * scale)), new Vector(96, 96));
                using var expected = new RenderTargetBitmap(actual.PixelSize, actual.Dpi);
                actual.Render(cachedHost); expected.Render(referenceHost);
                using var left = new MemoryStream(); using var right = new MemoryStream(); actual.Save(left, new PngBitmapEncoderOptions()); expected.Save(right, new PngBitmapEncoderOptions());
                using var a = SKBitmap.Decode(left.ToArray()); using var b = SKBitmap.Decode(right.ToArray());
                var difference = 0; long total = 0; var aa = a.GetPixelSpan(); var bb = b.GetPixelSpan();
                for (var index = 0; index < aa.Length; index++) { var delta = Math.Abs(aa[index] - bb[index]); difference = Math.Max(difference, delta); total += delta; }
                var mean = (double)total / aa.Length;
                if (difference > 4)
                {
                    actual.Save(System.IO.Path.Combine(output, "preview-mismatch.png"), new PngBitmapEncoderOptions());
                    expected.Save(System.IO.Path.Combine(output, "preview-reference.png"), new PngBitmapEncoderOptions());
                }
                // Compositing a transparent 8-bit preview over the picture introduces rounding compared
                // with blending every segment straight into an opaque target. Bound both local and
                // whole-image error; committed document pixels are checked separately for exact equality.
                if (difference > 4 || mean > 0.25) throw new InvalidOperationException($"STROKE PREVIEW FAILED: scale {scale}, {count} points, maximum {difference}, mean {mean}");
                report.Add($"PASS: incremental preview scale {scale}, {count} points preserves antialiased shape and color within 8-bit compositing rounding (maximum {difference}, mean {mean:0.000})");
                if (preview.SegmentsDrawn != count - 1 || preview.Rebuilds != 1) throw new InvalidOperationException("The preview redrew historical segments.");
                report.Add($"PASS: scale {scale}, {count} input points draw each segment once");
                if (count == 1024) actual.Save(System.IO.Path.Combine(output, $"stroke-preview-{scale:0.0}.png"), new PngBitmapEncoderOptions());
            }
        }
    }
    private sealed class Reference : Control
    {
        internal IReadOnlyList<SKPoint> Points = [];
        internal BrushSettings Settings = new();
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, (byte)(Settings.Red * 255), (byte)(Settings.Green * 255), (byte)(Settings.Blue * 255))),
                Settings.Diameter, lineCap: PenLineCap.Round);
            for (var index = 1; index < Points.Count; index++) context.DrawLine(pen, new Point(Points[index - 1].X, Points[index - 1].Y), new Point(Points[index].X, Points[index].Y));
        }
    }
    private sealed class Cached : Control
    {
        internal StrokePreview Preview = null!;
        internal IReadOnlyList<SKPoint> Points = [];
        internal BrushSettings Settings = new();
        internal double Scale;
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size));
            var image = Preview.Get(Bounds.Size, Scale, 1, new SKPoint(), Settings, Points)!;
            context.DrawImage(image, new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height), new Rect(Bounds.Size));
        }
    }
}
