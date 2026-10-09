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
    private SKBitmap? _tipImage;
    private double _nextDab;
    internal int SegmentsDrawn { get; private set; }
    internal int Rebuilds { get; private set; }

    internal WriteableBitmap? Get(Size size, double scale, double zoom, SKPoint origin,
        BrushSettings settings, IReadOnlyList<SKPoint> points)
    {
        if (points.Count < (settings.Tip is null ? 2 : 1) || size.Width <= 0 || size.Height <= 0) return null;
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
            if (settings.Tip is not null) { _tipImage = settings.Tip.Image(); _nextDab = BrushEdits.Spacing(settings); }
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
        if (settings.Tip is { } tip)
        {
            using var filter = SKColorFilter.CreateBlendMode(pen.Color, SKBlendMode.SrcIn);
            using var stamp = new SKPaint { ColorFilter = filter, IsAntialias = true };
            var ratio = settings.Diameter * zoom / Math.Max(tip.Width, tip.Height);
            void Dab(SKPoint point)
            {
                var at = Screen(point); var w = (float)(tip.Width * ratio); var h = (float)(tip.Height * ratio);
                canvas.DrawBitmap(_tipImage!, new SKRect(at.X - w / 2, at.Y - h / 2, at.X + w / 2, at.Y + h / 2),
                    new SKSamplingOptions(SKFilterMode.Linear), stamp);
            }
            if (_drawn == 0) Dab(points[0]);
            for (var index = Math.Max(1, _drawn); index < points.Count; index++)
            {
                var from = points[index - 1]; var to = points[index];
                var dx = to.X - from.X; var dy = to.Y - from.Y;
                var length = Math.Sqrt((double)dx * dx + (double)dy * dy);
                if (length > 0)
                {
                    while (_nextDab <= length)
                    {
                        Dab(new SKPoint((float)(from.X + dx * _nextDab / length), (float)(from.Y + dy * _nextDab / length)));
                        _nextDab += BrushEdits.Spacing(settings);
                    }
                    _nextDab -= length;
                }
                SegmentsDrawn++;
            }
            canvas.Flush(); _drawn = points.Count; return _image;
        }
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
        _tipImage?.Dispose(); _tipImage = null;
    }
}
