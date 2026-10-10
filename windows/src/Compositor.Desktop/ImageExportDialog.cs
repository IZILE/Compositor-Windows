using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>The preview and byte count belong to the very file that is eventually exported.</summary>
internal sealed class ImageExportDialog : DialogWindow
{
    private readonly CanvasDocument _snapshot;
    private readonly ImageExportFormat _format;
    private readonly InlineNumber _quality = new("Quality", 1, 100, slider: true, fieldWidth: 44);
    private readonly ComboBox _compression = new() { ItemsSource = new[] { "Fast", "Balanced", "Smallest" }, SelectedIndex = 1, Width = 180 };
    private readonly SegmentedChoice _view = new("Original", "Compressed");
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Height = 270 };
    private readonly TextBlock _size = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _export = new() { [!ContentControl.ContentProperty] = UiText.Bind("Export"), Classes = { "accent" }, IsDefault = true, IsEnabled = false };
    private readonly Button _cancel = new() { [!ContentControl.ContentProperty] = UiText.Bind("Cancel"), IsCancel = true };
    private readonly SemaphoreSlim _encoder = new(1);
    private readonly List<Task> _pending = [];
    private CancellationTokenSource? _request;
    private Bitmap? _original, _compressed;
    private byte[]? _originalBytes;
    private PreparedImageExport? _ready, _result;
    private bool _closing, _mayClose;
    private Task? _closeTask;
    internal InlineNumber QualityControl => _quality;
    internal ComboBox CompressionControl => _compression;
    internal Button ExportButton => _export;
    internal Button CancelButton => _cancel;
    internal SegmentedChoice ViewControl => _view;
    internal PreparedImageExport? Prepared => _ready;
    internal Task WhenReady => Task.WhenAll(_pending.ToArray());
    internal string SizeText => _size.Text ?? "";

    internal ImageExportDialog(CanvasDocument document, ImageExportFormat format)
    {
        _snapshot = document.Clone(); _format = format;
        UiText.Set(this, TitleProperty, format == ImageExportFormat.Png ? "Export PNG" : "Export JPEG");
        Width = 620; SizeToContent = SizeToContent.Height; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _quality.Value = ImageWriter.DefaultQuality; _quality.IsVisible = format == ImageExportFormat.Jpeg;
        _quality.Changed += _ => QueuePreview();
        _compression.SelectionChanged += (_, _) => QueuePreview();
        var compressionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = format == ImageExportFormat.Png,
            Children = { new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Compression"), VerticalAlignment = VerticalAlignment.Center }, _compression } };
        _view.SelectedIndex = 1; _view.IsVisible = format == ImageExportFormat.Jpeg;
        _view.Changed += _ => ShowImage();
        _export.Click += async (_, _) => await StopAndClose(accept: true);
        _cancel.Click += async (_, _) => await StopAndClose();
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        footer.Children.Add(_size);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _cancel, _export } };
        Grid.SetColumn(actions, 1); footer.Children.Add(actions);
        Content = new StackPanel { Margin = new Thickness(18), Spacing = 14, Children = {
            new TextBlock { Text = $"{document.Width} × {document.Height} px · " + UiText.Get(format == ImageExportFormat.Png
                ? "PNG · Lossless · Transparency" : "JPEG · White background"), Foreground = Skin.SecondaryBrush },
            new Border { Background = Skin.Checker, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _image },
            _view, _quality, compressionRow, footer } };
        Opened += (_, _) => QueuePreview();
        Closing += (_, e) => { if (!_mayClose) { e.Cancel = true; _ = StopAndClose(); } };
    }

    private ImageExportSettings Settings => new(_format, (int)_quality.Value, (PngCompression)Math.Clamp(_compression.SelectedIndex, 0, 2));

    private void QueuePreview()
    {
        if (!IsVisible || _closing) return;
        _request?.Cancel(); _request = new CancellationTokenSource();
        _export.IsEnabled = false; _size.Text = UiText.Get("Compressing…");
        _pending.RemoveAll(task => task.IsCompleted);
        _pending.Add(UpdatePreview(Settings, _request));
    }

    private async Task UpdatePreview(ImageExportSettings settings, CancellationTokenSource request)
    {
        var cancellation = request.Token;
        PreparedImageExport? prepared = null;
        try
        {
            await Task.Delay(180, cancellation);
            await _encoder.WaitAsync(cancellation);
            try
            {
                var originalBytes = _originalBytes;
                var result = await Task.Run(() =>
                {
                    var file = PreparedImageExport.Create(_snapshot, settings, cancellation);
                    try
                    {
                        var original = originalBytes ?? OriginalPreview(_snapshot, cancellation);
                        var compressed = settings.Format == ImageExportFormat.Png ? original : JpegPreview(file.Path);
                        cancellation.ThrowIfCancellationRequested();
                        return (File: file, Original: original, Compressed: compressed);
                    }
                    catch { file.Dispose(); throw; }
                }, cancellation);
                prepared = result.File;
                cancellation.ThrowIfCancellationRequested();
                if (_closing) return;
                _originalBytes = result.Original;
                if (_original is null) { using var source = new MemoryStream(result.Original); _original = new Bitmap(source); }
                using var stream = new MemoryStream(result.Compressed);
                var next = new Bitmap(stream); var old = _compressed; _compressed = next;
                ShowImage(); old?.Dispose();
                _ready?.Dispose(); _ready = prepared; prepared = null;
                _size.Text = FormatBytes(_ready.Length); _export.IsEnabled = true;
            }
            finally { _encoder.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        { if (!_closing && !cancellation.IsCancellationRequested) _size.Text = UiText.Format("Could not export: {0}", UiText.Get(error.Message)); }
        finally
        {
            prepared?.Dispose();
            if (ReferenceEquals(_request, request)) _request = null;
            request.Dispose();
        }
    }

    private void ShowImage() => _image.Source = _view.SelectedIndex == 0 ? _original : _compressed;

    internal Task CancelExport() => StopAndClose();
    private Task StopAndClose(bool accept = false)
    {
        if (accept && !_export.IsEnabled) return Task.CompletedTask;
        return _closeTask ??= CloseAfterWorkers(accept);
    }

    private async Task CloseAfterWorkers(bool accept)
    {
        _closing = true; _request?.Cancel(); _export.IsEnabled = _cancel.IsEnabled = false;
        // Finish workers before the owning document can close and release its pixel assets.
        await Task.WhenAll(_pending.ToArray());
        if (accept) { _result = _ready; _ready = null; }
        _ready?.Dispose(); _image.Source = null; _original?.Dispose(); _compressed?.Dispose();
        _snapshot.Dispose(); _encoder.Dispose();
        _mayClose = true; Close();
    }

    internal static async Task<PreparedImageExport?> Ask(Window owner, CanvasDocument document, ImageExportFormat format)
    {
        var dialog = new ImageExportDialog(document, format);
        await dialog.ShowDialog(owner);
        return dialog._result;
    }

    internal static string FormatBytes(long length) => length < 1024 ? $"{length} B"
        : length < 1024 * 1024 ? $"{length / 1024d:0.0} KB" : $"{length / (1024d * 1024):0.00} MB";

    private static byte[] OriginalPreview(CanvasDocument document, CancellationToken cancellation)
    {
        var scale = Math.Min(1, Math.Min(580d / document.Width, 270d / document.Height));
        using var bitmap = new SKBitmap(new SKImageInfo(Math.Max(1, (int)Math.Ceiling(document.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(document.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bitmap);
        for (var y = 0; y < document.Height; y += 1024)
        for (var x = 0; x < document.Width; x += 1024)
        {
            cancellation.ThrowIfCancellationRequested();
            using var tile = DocumentRenderer.RenderRegion(document, SKRectI.Create(x, y, Math.Min(1024, document.Width - x), Math.Min(1024, document.Height - y)));
            canvas.DrawBitmap(tile, new SKRect((float)(x * scale), (float)(y * scale), (float)((x + tile.Width) * scale), (float)((y + tile.Height) * scale)),
                new SKSamplingOptions(SKFilterMode.Linear));
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] JpegPreview(string path)
    {
        using var stream = File.OpenRead(path); using var codec = SKCodec.Create(stream);
        var scale = Math.Min(1, Math.Min(580f / codec.Info.Width, 270f / codec.Info.Height));
        var size = codec.GetScaledDimensions(scale);
        using var pixels = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(pixels.Info, pixels.GetPixels()) != SKCodecResult.Success) throw new IOException("That image could not be decoded.");
        using var data = pixels.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
}

internal sealed class PreparedImageExport : IDisposable
{
    internal string Path { get; }
    internal long Length { get; }
    internal ImageExportSettings Settings { get; }
    private PreparedImageExport(string path, ImageExportSettings settings) { Path = path; Length = new FileInfo(path).Length; Settings = settings; }
    internal static PreparedImageExport Create(CanvasDocument document, ImageExportSettings settings, CancellationToken cancellation)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Compositor-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!ImageWriter.Write(document, path, settings, cancellation)) throw new IOException("That canvas is too large to write as one JPEG.");
            return new PreparedImageExport(path, settings);
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }
    internal Task SaveTo(string destination) => Task.Run(() =>
    {
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(Path, temporary); File.Move(temporary, destination, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    });
    public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
}
