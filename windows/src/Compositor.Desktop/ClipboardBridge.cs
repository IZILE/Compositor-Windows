using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Windows bitmap interchange, with PNG for alpha and an optional original selection position.</summary>
internal static class ClipboardBridge
{
    // Avalonia maps the native PNG format to Bitmap. A distinct application format retains our exact RGBA bytes.
    internal static readonly DataFormat<byte[]> Png = DataFormat.CreateBytesApplicationFormat("Compositor.RgbaPng.v1");
    private static readonly DataFormat<string> Placement = DataFormat.CreateStringApplicationFormat("Compositor.Selection.v1");

    internal static async Task WriteAsync(IClipboard clipboard, ClipboardImage image, Guid documentID)
    {
        var payload = new BitmapTransfer(PngCodec.Encode(image.Image), image.Region, documentID);
        try { await clipboard.SetDataAsync(payload); }
        catch { payload.Dispose(); throw; }
        // The clipboard owns the transfer now, including its bitmap. Flushing retains it after app exit.
        await clipboard.FlushAsync();
    }

    internal static async Task<ClipboardImage?> ReadAsync(IAsyncDataTransfer data, CanvasDocument? document)
    {
        var budget = document?.RemainingImagePixels ?? DocumentLimits.DocumentPixelBudget;
        SKBitmap? pixels = null;
        try
        {
            if (await data.TryGetValueAsync(Png) is { } png)
            {
                if (png.Length > DocumentLimits.MaxAssetBytes) throw new ImportException(ImportError.TooLarge);
                var header = PngCodec.ReadHeader(png);
                CheckSize(header.Width, header.Height, budget);
                pixels = PngCodec.DecodeImage(header, png);
            }
            else if (data.Contains(DataFormat.Bitmap))
            {
                // Windows creates a bitmap for this request; release it after copying the pixels.
                using var bitmap = await data.TryGetBitmapAsync();
                if (bitmap is null) return null;
                CheckSize(bitmap.PixelSize.Width, bitmap.PixelSize.Height, budget);
                using var stream = new MemoryStream();
                bitmap.Save(stream, new PngBitmapEncoderOptions());
                var bytes = stream.ToArray();
                if (bytes.Length > DocumentLimits.MaxAssetBytes) throw new ImportException(ImportError.TooLarge);
                pixels = PngCodec.DecodeImage(PngCodec.ReadHeader(bytes), bytes);
            }
            if (pixels is null) return null;
            var region = Centered(document, pixels.Width, pixels.Height);
            if (document is not null && await data.TryGetValueAsync(Placement) is { } position)
                region = OriginalPosition(position, document.ID, pixels.Width, pixels.Height) ?? region;
            var result = new ClipboardImage(pixels, region);
            pixels = null;
            return result;
        }
        finally { pixels?.Dispose(); }
    }

    private static void CheckSize(int width, int height, int budget)
    {
        if (width is < 1 or > DocumentLimits.MaxSide || height is < 1 or > DocumentLimits.MaxSide
            || (long)width * height > Math.Min(DocumentLimits.MaxSurfacePixels, budget))
            throw new ImportException(ImportError.TooLarge);
    }

    private static SKRectI Centered(CanvasDocument? document, int width, int height) =>
        SKRectI.Create(document is null ? 0 : (document.Width - width) / 2,
            document is null ? 0 : (document.Height - height) / 2, width, height);

    private static SKRectI? OriginalPosition(string text, Guid documentID, int width, int height)
    {
        var values = text.Split(';');
        if (values.Length != 5 || !Guid.TryParse(values[0], out var source) || source != documentID) return null;
        var numbers = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(values[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i])) return null;
        if (numbers[0] is < 0 or > DocumentLimits.MaxSide || numbers[1] is < 0 or > DocumentLimits.MaxSide
            || numbers[2] != width || numbers[3] != height) return null;
        return SKRectI.Create(numbers[0], numbers[1], width, height);
    }

    /// <summary>Tests the pinned Avalonia Win32 serializer without reading or changing the system clipboard.</summary>
    internal static void VerifyWindowsSerialization(ClipboardImage image, Guid documentID)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This check requires Windows.");
        using var payload = new BitmapTransfer(PngCodec.Encode(image.Image), image.Region, documentID);
        var assembly = Assembly.Load("Avalonia.Win32");
        var registry = assembly.GetType("Avalonia.Win32.ClipboardFormatRegistry", throwOnError: true)!;
        var helper = assembly.GetType("Avalonia.Win32.OleDataObjectHelper", throwOnError: true)!;
        var write = helper.GetMethod("WriteDataToHGlobal", BindingFlags.Public | BindingFlags.Static)!;
        var read = helper.GetMethod("ReadBytesFromHGlobal", BindingFlags.NonPublic | BindingFlags.Static)!;
        var nativePng = (DataFormat)registry.GetField("PngSystemDataFormat", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        byte[] RoundTrip(DataFormat format)
        {
            object?[] arguments = [payload, format, IntPtr.Zero];
            try
            {
                var code = (uint)write.Invoke(null, arguments)!;
                if (code != 0) throw new InvalidOperationException($"Win32 serialization failed: {code:X8}");
                return (byte[])read.Invoke(null, [arguments[2]])!;
            }
            finally
            {
                if (arguments[2] is IntPtr handle && handle != IntPtr.Zero) GlobalFree(handle);
            }
        }
        var exact = RoundTrip(Png);
        using var rgba = PngCodec.DecodeImage(PngCodec.ReadHeader(exact), exact);
        if (!PngCodec.Encode(rgba).SequenceEqual(PngCodec.Encode(image.Image)))
            throw new InvalidOperationException("The native application PNG format changed the pixels.");
        var exported = RoundTrip(nativePng);
        using var decoded = PngCodec.DecodeImage(PngCodec.ReadHeader(exported), exported);
        if (decoded.Width != image.Image.Width || decoded.Height != image.Image.Height)
            throw new InvalidOperationException("The native PNG dimensions changed.");
        var sample = image.Image.GetPixel(decoded.Width / 2, decoded.Height / 2);
        var actual = decoded.GetPixel(decoded.Width / 2, decoded.Height / 2);
        if (sample.Alpha != actual.Alpha || Math.Abs(sample.Red - actual.Red) > 1
            || Math.Abs(sample.Green - actual.Green) > 1 || Math.Abs(sample.Blue - actual.Blue) > 1)
            throw new InvalidOperationException("The native PNG changed the sample color or alpha.");
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr GlobalFree(IntPtr handle);

    /// <summary>Retains the bitmap while OLE owns it and disposes it when ownership is released.</summary>
    private sealed class BitmapTransfer : IDataTransfer, IAsyncDataTransfer
    {
        private readonly DataTransfer _data = new();
        private readonly Bitmap _bitmap;

        internal BitmapTransfer(byte[] png, SKRectI region, Guid documentID)
        {
            using var stream = new MemoryStream(png, writable: false);
            _bitmap = new Bitmap(stream);
            var item = DataTransferItem.Create(Png, png);
            item.SetBitmap(_bitmap);
            item.Set(Placement, FormattableString.Invariant($"{documentID};{region.Left};{region.Top};{region.Width};{region.Height}"));
            _data.Add(item);
        }

        public IReadOnlyList<DataFormat> Formats => _data.Formats;
        IReadOnlyList<IDataTransferItem> IDataTransfer.Items => _data.Items;
        IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => _data.Items;
        public void Dispose() => _bitmap.Dispose();
    }
}
