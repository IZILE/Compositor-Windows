using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

// Keep the temporary stroke in a bounded viewport bitmap. Each segment is drawn once;
// a longer stroke does not submit all earlier segments again on every pointer sample.
internal sealed class StrokePreview : IDisposable
{
    private WriteableBitmap? _image;
    private Size _size;
    private double _scale, _zoom;
    private SKPoint _origin;
    private BrushSettings? _settings;
    private int _drawn;
    internal int SegmentsDrawn { get; private set; }
    internal int Rebuilds { get; private set; }

    internal WriteableBitmap? Get(Size size, double scale, double zoom, SKPoint origin,
        BrushSettings settings, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < 2 || size.Width <= 0 || size.Height <= 0) return null;
        var width = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
        // A zoomed-out or very large display must not allocate an unbounded preview.
        const long limit = 8L * 1024 * 1024;
        if ((long)width * height > limit)
        {
            scale *= Math.Sqrt((double)limit / ((long)width * height));
            width = Math.Max(1, (int)Math.Floor(size.Width * scale));
            height = Math.Max(1, (int)Math.Floor(size.Height * scale));
        }
        var rebuild = _image is null || _size != size || _scale != scale || _zoom != zoom
            || _origin != origin || _settings != settings || points.Count < _drawn;
        if (rebuild)
        {
            Dispose();
            _image = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scale, 96 * scale),
                PixelFormats.Bgra8888, AlphaFormat.Premul);
            _size = size; _scale = scale; _zoom = zoom; _origin = origin; _settings = settings;
            Rebuilds++;
        }
        if (_drawn == points.Count) return _image;
        using var buffer = _image!.Lock();
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
            buffer.Address, buffer.RowBytes);
        if (surface is null) throw new InvalidOperationException("Could not allocate a stroke preview.");
        var canvas = surface.Canvas;
        if (_drawn == 0) canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)scale);
        using var pen = new SKPaint
        {
            Color = new SKColor((byte)(settings.Red * 255), (byte)(settings.Green * 255), (byte)(settings.Blue * 255), 170),
            StrokeWidth = (float)Math.Max(1, settings.Diameter * zoom), StrokeCap = SKStrokeCap.Round,
            Style = SKPaintStyle.Stroke, IsAntialias = true,
        };
        SKPoint Screen(SKPoint point) => new((float)((point.X - origin.X) * zoom), (float)((point.Y - origin.Y) * zoom));
        for (var index = Math.Max(1, _drawn); index < points.Count; index++)
        {
            canvas.DrawLine(Screen(points[index - 1]), Screen(points[index]), pen);
            SegmentsDrawn++;
        }
        canvas.Flush(); _drawn = points.Count;
        return _image;
    }
    public void Dispose()
    {
        _image?.Dispose(); _image = null; _drawn = 0; _settings = null;
    }
}
