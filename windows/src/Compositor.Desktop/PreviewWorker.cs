using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

// Each session takes one independent pixel snapshot. Jobs and displayed frames retain it, so closing a
// tab or changing a filter cannot free pixels still read on a worker. Only one job runs at a time.
internal sealed class PreviewWorker : IDisposable
{
    private readonly CanvasDocument _source;
    private readonly List<ImportedImage> _pixels = [];
    private int _references = 1;
    private bool _ownerReleased;

    internal PreviewWorker(CanvasDocument document)
    {
        _source = document.Clone();
        var copied = new Dictionary<ImportedImage, ImportedImage>();
        ImportedImage Copy(ImportedImage image)
        {
            if (copied.TryGetValue(image, out var existing)) return existing;
            var pixels = new ImportedImage(image.Image.Copy(), image.Thumbnail.Copy(), image.Name);
            copied[image] = pixels; _pixels.Add(pixels);
            return pixels;
        }
        try
        {
            foreach (var layer in _source.Layers)
            {
                if (layer.Asset is { } image) layer.Asset = Copy(image);
                if (layer.Mask is { } mask) layer.Mask = new LayerMask(Copy(mask.Asset), mask.IsEnabled, mask.Placement, mask.IsLinked);
            }
        }
        catch { Dispose(); throw; }
    }

    internal Task<Frame?> Run(Func<CanvasDocument, bool> apply)
    {
        Interlocked.Increment(ref _references);
        return Task.Run(() =>
        {
            FilterPreview? preview = null;
            try
            {
                preview = FilterPreview.Begin(_source, _source.Layers.Select(layer => layer.ID).ToArray());
                if (preview is not null && preview.Show(apply)) return new Frame(this, preview);
                preview?.Dispose(); Release(); return null;
            }
            catch { preview?.Dispose(); Release(); throw; }
        });
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        foreach (var pixels in _pixels) pixels.Dispose();
        _pixels.Clear(); _source.Dispose();
    }
    public void Dispose()
    {
        if (_ownerReleased) return;
        _ownerReleased = true; Release();
    }

    internal sealed class Frame(PreviewWorker owner, FilterPreview preview) : IDisposable
    {
        private bool _disposed;
        internal FilterPreview Preview { get; } = preview;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; Preview.Dispose(); owner.Release();
        }
    }
}
