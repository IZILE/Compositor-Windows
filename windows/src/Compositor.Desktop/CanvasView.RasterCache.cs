using Avalonia;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class CanvasView
{
    private readonly RasterCache _rasterCache = new();
    internal int RasterBuildCount => _rasterCache.Builds;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
    {
        _rasterCache.Clear();
        base.OnDetachedFromVisualTree(args);
    }

    // Pixels are immutable assets. Capture all render inputs by value (especially mutable mask flags),
    // so guides, selections, hover, caret and viewport changes can reuse the image without stale edits.
    private sealed record LayerPixels(Guid ID, SKBitmap? Image, LayerTransform Transform, bool Visible,
        Guid? Parent, bool Group, double Opacity, Core.Format.LayerBlendMode Blend, Guid? Clip,
        SKBitmap? Mask, bool MaskEnabled, LayerTransform? MaskPlacement, bool MaskLinked,
        Core.Format.LayerAdjustment? Adjustment, LayerShape? Shape, Core.Format.LayerEffects? Effects, LayerText? Text)
    {
        internal static LayerPixels Capture(ImageLayer layer) => new(layer.ID, layer.Asset?.Image, layer.Transform,
            layer.IsVisible, layer.ParentID, layer.IsGroup, layer.Opacity, layer.BlendMode, layer.MaskSourceID,
            layer.Mask?.Asset.Image, layer.Mask?.IsEnabled == true, layer.Mask?.Placement, layer.Mask?.IsLinked == true,
            layer.Adjustment, layer.Shape, layer.Effects, layer.Text);
    }

    private sealed class RasterCache
    {
        private WriteableBitmap? _image;
        private readonly LayerRenderCache _layerCache = new();
        private CanvasDocument? _document;
        private LayerPixels[] _layers = [];
        private Guid? _mask;
        private int _width, _height;
        internal SKRectI Region { get; private set; }
        internal int Builds { get; private set; }

        internal WriteableBitmap Get(CanvasDocument document, SKRectI wanted, Guid? mask)
        {
            var same = ReferenceEquals(_document, document) && _width == document.Width && _height == document.Height
                && _mask == mask && _layers.Length == document.Layers.Count;
            for (var index = 0; same && index < _layers.Length; index++)
                same = _layers[index] == LayerPixels.Capture(document.Layers[index]);
            if (same && _image is not null && Region.Contains(wanted)) return _image;

            var whole = SKRectI.Create(0, 0, document.Width, document.Height);
            // A modest overscan avoids recomposition on every panning sample; no unbounded whole-canvas cache.
            var grown = SKRectI.Intersect(SKRectI.Create(wanted.Left - 128, wanted.Top - 128,
                wanted.Width + 256, wanted.Height + 256), whole);
            // Even a medium document can be much larger than the visible viewport. Do not recompose
            // off-screen pixels on every layer edit merely because its total is below the cache cap.
            var wholePixels = (long)document.Width * document.Height;
            var grownPixels = (long)grown.Width * grown.Height;
            var region = wholePixels <= 4L * 1024 * 1024 && wholePixels <= grownPixels * 1.5 ? whole
                : (long)grown.Width * grown.Height <= ViewportPixelLimit ? grown : wanted;
            using var rendered = mask is { } id && document.Layers.FirstOrDefault(layer => layer.ID == id) is { Mask: not null } layer
                ? MaskPreview.RenderRegion(layer, region) : DocumentRenderer.RenderRegion(document, region, _layerCache);
            var image = ToImage(rendered);
            _image?.Dispose(); _image = image; Region = region;
            _document = document; _width = document.Width; _height = document.Height; _mask = mask;
            _layers = document.Layers.Select(LayerPixels.Capture).ToArray(); Builds++;
            return image;
        }

        internal void Clear()
        {
            _image?.Dispose(); _image = null; _document = null; _layers = []; Region = default;
            _layerCache.Clear();
        }
    }
}
