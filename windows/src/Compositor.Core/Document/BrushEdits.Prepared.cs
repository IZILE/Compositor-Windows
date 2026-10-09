using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Document;

public static partial class BrushEdits
{
    // Accumulate the same evenly spaced dabs as a complete stroke, once per new input segment.
    // The document and its immutable asset remain unchanged until the mouse is released.
    private sealed class CoverageAccumulator : IDisposable
    {
        private readonly BrushSettings _settings;
        private readonly SKMatrix _toDocument, _toPixel;
        private readonly int _width, _height;
        private readonly SKBitmap? _clip;
        private readonly SKRectI _region;
        private SKPoint? _previous;
        private double _next;
        internal float[] Coverage { get; }

        internal CoverageAccumulator(CanvasDocument document, BrushSettings settings, SKMatrix toDocument,
            SKMatrix toPixel, int width, int height)
        {
            _settings = settings; _toDocument = toDocument; _toPixel = toPixel; _width = width; _height = height;
            _region = document.Selection.CoverageRect(document.Width, document.Height);
            _clip = document.Selection.Coverage(_region);
            Coverage = new float[checked(width * height)];
            _next = Spacing(settings.Diameter, settings.Hardness);
        }
        internal void Append(SKPoint point)
        {
            var selection = new Clip(_clip is null ? default : _clip.GetPixelSpan(), _clip?.RowBytes ?? 0, _region);
            var radius = _settings.Diameter / 2;
            var hard = _settings.Hardness >= 1;
            if (_previous is not { } from)
                Stamp(Coverage, _width, _height, point, radius, _toDocument, _toPixel, hard, _settings.Hardness, selection);
            else
            {
                var dx = point.X - from.X; var dy = point.Y - from.Y;
                var length = Math.Sqrt((double)dx * dx + (double)dy * dy);
                if (length <= 0) return;
                var spacing = Spacing(_settings.Diameter, _settings.Hardness);
                while (_next <= length)
                {
                    var at = new SKPoint((float)(from.X + dx * _next / length), (float)(from.Y + dy * _next / length));
                    Stamp(Coverage, _width, _height, at, radius, _toDocument, _toPixel, hard, _settings.Hardness, selection);
                    _next += spacing;
                }
                _next -= length;
            }
            _previous = point;
        }
        public void Dispose() => _clip?.Dispose();
    }

    /// <summary>Prepares a Paint/Erase stroke without changing pixels or creating an undo step.</summary>
    public static PreparedStroke? Prepare(CanvasDocument document, Guid layerID, BrushSettings settings, bool mask = false)
    {
        if (settings.Mode != BrushMode.Paint || settings.Diameter <= 0 || settings.Opacity <= 0) return null;
        var layer = document.Layers.FirstOrDefault(item => item.ID == layerID);
        if (layer is null || (mask ? layer.Mask is null : layer.IsGroup)) return null;
        try { return new PreparedStroke(document, layer, settings, mask); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>An incremental stroke; commit still makes one immutable asset for one undo step.</summary>
    public sealed class PreparedStroke : IDisposable
    {
        private readonly CanvasDocument _document;
        private readonly Guid _layerID;
        private readonly int _documentWidth, _documentHeight;
        private readonly DocumentSelection _selection;
        private readonly ImportedImage? _asset;
        private readonly LayerMask? _mask;
        private readonly bool _onMask, _ownsSource;
        private readonly SKBitmap _source;
        private readonly LayerTransform _placement;
        private readonly BrushSettings _settings;
        private readonly CoverageAccumulator _coverage;
        private bool _disposed, _hasPoint;

        internal PreparedStroke(CanvasDocument document, ImageLayer layer, BrushSettings settings, bool mask)
        {
            _document = document; _layerID = layer.ID; _selection = document.Selection;
            _documentWidth = document.Width; _documentHeight = document.Height;
            _asset = layer.Asset; _mask = layer.Mask; _onMask = mask; _settings = settings;
            _placement = mask ? layer.MaskTransform : layer.Transform;
            var source = mask ? layer.Mask!.Asset.Image : layer.Asset?.Image;
            var grows = source is null || (mask && source.Width == 1 && source.Height == 1);
            if (grows)
            {
                var width = Math.Max(1, (int)Math.Round(layer.Transform.Width));
                var height = Math.Max(1, (int)Math.Round(layer.Transform.Height));
                if (width > DocumentLimits.MaxSide || height > DocumentLimits.MaxSide
                    || (long)width * height > DocumentLimits.MaxSurfacePixels)
                    throw new InvalidOperationException("The brush target is too large.");
                _source = Bitmaps.Allocate(mask ? Bitmaps.MaskInfo(width, height) : Bitmaps.ColorInfo(width, height));
                _source.Erase(mask ? source!.GetPixel(0, 0) : SKColors.Transparent); _ownsSource = true;
            }
            else _source = source!;
            try
            {
                var toDocument = PixelToDocument(_placement, _source.Width, _source.Height);
                if (!toDocument.TryInvert(out var toPixel)) throw new InvalidOperationException("The brush target has no inverse transform.");
                _coverage = new CoverageAccumulator(document, settings, toDocument, toPixel, _source.Width, _source.Height);
            }
            catch { if (_ownsSource) _source.Dispose(); throw; }
        }

        public void Append(SKPoint point)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _coverage.Append(point); _hasPoint = true;
        }

        /// <summary>Returns false if the target, selection or settings changed; the caller can repaint safely.</summary>
        public bool TryCommit(CanvasDocument document, Guid layerID, BrushSettings settings, bool mask = false)
        {
            if (_disposed || !_hasPoint || !ReferenceEquals(document, _document) || layerID != _layerID
                || settings != _settings || mask != _onMask || document.Width != _documentWidth || document.Height != _documentHeight
                || !ReferenceEquals(document.Selection, _selection)) return false;
            var layer = document.Layers.FirstOrDefault(item => item.ID == layerID);
            if (layer is null || !ReferenceEquals(layer.Asset, _asset) || !ReferenceEquals(layer.Mask, _mask)
                || (mask ? layer.MaskTransform : layer.Transform) != _placement) return false;
            var painted = Bitmaps.Allocate(mask ? Bitmaps.MaskInfo(_source.Width, _source.Height) : Bitmaps.ColorInfo(_source.Width, _source.Height));
            using (var canvas = new SKCanvas(painted))
            {
                using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
                using var source = SKImage.FromBitmap(_source);
                canvas.DrawImage(source, SKRect.Create(0, 0, painted.Width, painted.Height), new SKSamplingOptions(SKFilterMode.Nearest), paint);
            }
            if (mask)
            {
                ApplyToMask(painted.GetPixelSpan(), _coverage.Coverage, settings);
                layer.Mask = layer.Mask!.Replacing(ImportedImage.Create(painted, layer.Mask.Asset.Name));
            }
            else
            {
                Apply(painted, _coverage.Coverage, settings);
                layer.Asset = ImportedImage.Create(painted, layer.Asset?.Name ?? layer.Name);
            }
            Dispose(); return true;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _coverage.Dispose(); if (_ownsSource) _source.Dispose();
        }
    }
}
