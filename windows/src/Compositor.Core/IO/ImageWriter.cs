using Compositor.Core.Document;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Core.IO;

/// <summary>
/// Writing the flattened document out. The extension chooses the format: PNG is streamed a band of tiles at
/// a time and works at any canvas size; JPEG has to be made whole, so it is refused for a canvas too big to
/// hold rather than quietly failing.
/// </summary>
public static class ImageWriter
{
    public const int DefaultQuality = 90;

    public static bool Write(CanvasDocument document, string path, int quality = DefaultQuality)
    {
        var extension = Path.GetExtension(path);
        if (!Knows(path)) return false;
        return Write(document, path, new ImageExportSettings(extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? ImageExportFormat.Png : ImageExportFormat.Jpeg, quality));
    }

    /// <summary>A failed or canceled export leaves an existing destination intact.</summary>
    public static bool Write(CanvasDocument document, string path, ImageExportSettings settings, CancellationToken cancellation = default)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
                if (!Write(document, file, settings, cancellation)) return false;
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool Write(CanvasDocument document, Stream output, ImageExportSettings settings, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (settings.Format == ImageExportFormat.Png)
        {
            TiledPngWriter.Write(document, output, compression: settings.Compression, cancellation: cancellation);
            return true;
        }
        return settings.Format == ImageExportFormat.Jpeg && WriteJpeg(document, output, settings.Quality, cancellation);
    }

    /// <summary>Whether the format named by the extension is one this writes.</summary>
    public static bool Knows(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WriteJpeg(CanvasDocument document, Stream output, int quality, CancellationToken cancellation)
    {
        // Encoding is done on the whole picture at once, so a canvas that will not fit cannot be written.
        if ((long)document.Width * document.Height > DocumentLimits.MaxSurfacePixels) return false;
        using var flattened = DocumentRenderer.Render(document);
        cancellation.ThrowIfCancellationRequested();
        // JPEG has no alpha. Composite against white instead of discarding transparency as black.
        using (var canvas = new SKCanvas(flattened)) canvas.DrawColor(SKColors.White, SKBlendMode.DstOver);
        using var image = SKImage.FromBitmap(flattened);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        if (encoded is null) return false;
        cancellation.ThrowIfCancellationRequested();
        encoded.SaveTo(output);
        return true;
    }
}
