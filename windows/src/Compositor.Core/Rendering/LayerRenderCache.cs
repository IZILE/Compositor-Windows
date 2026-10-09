using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Rendering;

/// <summary>Bounded, view-owned layer rasters. Editing one layer need not resample every unchanged layer.</summary>
public sealed class LayerRenderCache(long byteLimit = 64L * 1024 * 1024) : IDisposable
{
    internal sealed record MaskInput(SKBitmap Image, LayerTransform Transform);
    internal sealed record Input(SKBitmap Image, LayerTransform Transform, Format.LayerEffects? Effects,
        SKRectI Region, MaskInput[] Masks)
    {
        internal bool Matches(Input other) => ReferenceEquals(Image, other.Image) && Transform == other.Transform
            && ReferenceEquals(Effects, other.Effects) && Region == other.Region && Masks.SequenceEqual(other.Masks);
    }
    private sealed record Entry(Input Input, SKBitmap Image, SKRectI Bounds, long Bytes, long Used,
        SKBitmap? Scaled = null, double Opacity = 1);
    private readonly Dictionary<Guid, Entry> _entries = [];
    private long _sequence;
    public long RetainedBytes { get; private set; }
    public int Builds { get; private set; }
    public int Reuses { get; private set; }

    internal bool TryGet(Guid id, Input input, out SKBitmap? image, out SKRectI bounds)
    {
        if (_entries.TryGetValue(id, out var entry) && entry.Input.Matches(input))
        {
            // Composite applies opacity in place, so its working bitmap cannot be the retained original.
            image = entry.Image.Copy(); bounds = entry.Bounds;
            _entries[id] = entry with { Used = ++_sequence }; Reuses++;
            return true;
        }
        image = null; bounds = default;
        return false;
    }

    internal void Store(Guid id, Input input, SKBitmap image, SKRectI bounds)
    {
        Remove(id);
        var bytes = (long)image.RowBytes * image.Height;
        if (bytes <= 0 || bytes > byteLimit) return;
        while (RetainedBytes + bytes > byteLimit && _entries.Count > 0)
            Remove(_entries.MinBy(pair => pair.Value.Used).Key);
        _entries[id] = new Entry(input, image.Copy(), bounds, bytes, ++_sequence);
        RetainedBytes += bytes; Builds++;
    }

    internal SKBitmap? Scaled(Guid id, double opacity, Action<SKBitmap, double> scale)
    {
        if (!_entries.TryGetValue(id, out var entry)) return null;
        if (entry.Scaled is not null && entry.Opacity == opacity) return entry.Scaled;
        if (entry.Scaled is { } previous)
        {
            var removed = (long)previous.RowBytes * previous.Height;
            previous.Dispose(); RetainedBytes -= removed;
            entry = entry with { Scaled = null, Bytes = entry.Bytes - removed };
            _entries[id] = entry;
        }
        var bytes = (long)entry.Image.RowBytes * entry.Image.Height;
        if (entry.Bytes + bytes > byteLimit) return null;
        while (RetainedBytes + bytes > byteLimit)
        {
            var other = _entries.Where(pair => pair.Key != id).MinBy(pair => pair.Value.Used);
            if (other.Value is null) return null;
            Remove(other.Key);
        }
        var image = entry.Image.Copy(); scale(image, opacity);
        _entries[id] = entry with { Scaled = image, Opacity = opacity, Bytes = entry.Bytes + bytes, Used = ++_sequence };
        RetainedBytes += bytes;
        return image;
    }

    private void Remove(Guid id)
    {
        if (!_entries.Remove(id, out var entry)) return;
        RetainedBytes -= entry.Bytes; entry.Image.Dispose(); entry.Scaled?.Dispose();
    }

    public void Clear()
    {
        foreach (var entry in _entries.Values) { entry.Image.Dispose(); entry.Scaled?.Dispose(); }
        _entries.Clear(); RetainedBytes = 0;
    }
    public void Dispose() => Clear();
}
