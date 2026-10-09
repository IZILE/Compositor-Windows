using System.Security.Cryptography;
using SkiaSharp;

namespace Compositor.Core.Document;

/// <summary>An immutable sampled brush mask. White is full coverage; zero is transparent.</summary>
public sealed class BrushTip
{
    public const int MaxSide = 4096;
    private readonly byte[] _alpha;
    public string Name { get; }
    public int Width { get; }
    public int Height { get; }
    public double Spacing { get; }
    public string ID { get; }
    public ReadOnlySpan<byte> Alpha => _alpha;

    public BrushTip(string name, int width, int height, ReadOnlySpan<byte> alpha, double spacing = 0.25)
    {
        if (width is < 1 or > MaxSide || height is < 1 or > MaxSide || (long)width * height != alpha.Length)
            throw new InvalidDataException("Brush dimensions must be between 1 and 4096 pixels.");
        if (!alpha.ContainsAnyExcept((byte)0)) throw new InvalidDataException("The brush tip is completely transparent.");
        Name = string.IsNullOrWhiteSpace(name) ? "Imported brush" : name.Trim();
        Width = width; Height = height; _alpha = alpha.ToArray();
        Spacing = double.IsFinite(spacing) ? Math.Clamp(spacing, 0.01, 2) : 0.25;
        ID = $"{width}x{height}-" + Convert.ToHexStringLower(SHA256.HashData(_alpha));
    }

    public double Sample(double x, double y, double diameter)
    {
        var scale = diameter / Math.Max(Width, Height);
        if (scale <= 0 || !double.IsFinite(scale)) return 0;
        var px = x / scale + Width / 2.0 - 0.5;
        var py = y / scale + Height / 2.0 - 0.5;
        if (px < -0.5 || py < -0.5 || px >= Width - 0.5 || py >= Height - 0.5) return 0;
        var left = (int)Math.Floor(px); var top = (int)Math.Floor(py);
        var tx = px - left; var ty = py - top;
        double At(int column, int row) => column < 0 || row < 0 || column >= Width || row >= Height ? 0 : _alpha[row * Width + column] / 255.0;
        return (At(left, top) * (1 - tx) + At(left + 1, top) * tx) * (1 - ty)
            + (At(left, top + 1) * (1 - tx) + At(left + 1, top + 1) * tx) * ty;
    }

    public SKBitmap Image()
    {
        var image = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var pixels = image.GetPixelSpan();
        for (var i = 0; i < _alpha.Length; i++)
        { pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = 255; pixels[i * 4 + 3] = _alpha[i]; }
        return image;
    }
}
