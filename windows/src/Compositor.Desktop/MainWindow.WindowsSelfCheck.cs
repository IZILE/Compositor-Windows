using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;
using ImageMagick;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    /// <summary>Exercises clipboard and routed file drops on the isolated headless platform.</summary>
    internal async Task<string> WindowsSelfCheck(string folder)
    {
        var report = new List<string>();
        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message + ": " + _message);
            report.Add(message);
        }
        Show();
        Dispatcher.UIThread.RunJobs();
        var firstPath = Path.Combine(folder, "工程.comp");
        using (var document = new CanvasDocument(Guid.NewGuid(), 40, 30))
        {
            var pixels = new SKBitmap(Bitmaps.ColorInfo(12, 8));
            pixels.Erase(new SKColor(40, 160, 220, 128));
            pixels.SetPixel(0, 0, SKColors.Transparent);
            document.Layers.Add(new ImageLayer(Guid.NewGuid(), ImportedImage.Create(pixels, "Original"),
                new Core.Model.LayerTransform(5, 6, 12, 8), "Original"));
            ProjectStore.Save(ProjectSnapshot.FromDocument(document), firstPath);
        }
        Open(firstPath);
        var first = _open;
        var original = _document!;
        _history.Begin("Rename Layer", original, Selected);
        original.Layers[0].Name = "Unsaved edit";
        _history.End(original, Selected);
        Open(firstPath.ToUpperInvariant() + Path.DirectorySeparatorChar);
        Require(_tabs.Count == 1 && ReferenceEquals(_document, original) && first.HasUnsavedChanges,
            "reopening the same project preserves its unsaved document and history");

        var second = new Tab { Document = LayerPlacement.NewDocument(60, 50)! };
        _tabs.Add(second);
        TypeHere(new SKPoint(1, 1));
        TypedText("切换时保留文字");
        Bring(second);
        Require(original.Layers.Any(layer => layer.LiveText?.Content == "切换时保留文字"),
            "switching tabs commits text still being typed instead of discarding it");
        _pickSavePath = _ => Task.FromResult<string?>(firstPath);
        Require(!await SaveTabAsync(second, saveAs: true), "Save As refuses a project already open in another tab");
        using (var saved = ProjectStore.Load(firstPath))
            Require(saved.Manifest.Width == 40 && saved.Manifest.Layers[0].Name == "Original",
                "refused Save As preserves the other project's files");
        Require(second.Path is null && second.HasUnsavedChanges, "refused Save As leaves the source tab unsaved");

        Bring(first);
        Undo();
        Require(original.Layers.Count == 1, "text committed by a tab switch remains one undo step");
        SelectionEdits.Select(original, SKRectI.Create(7, 8, 6, 4));
        await CopyToSystemAsync(merged: true);
        Require(_clipboard is not null && _clipboard.Image.GetPixel(_clipboard.Region.Width / 2,
            _clipboard.Region.Height / 2).Alpha is > 0 and < 255, "Copy Merged exports the selected flattened image with alpha");
        await CopyToSystemAsync();
        var clipboard = Clipboard ?? throw new InvalidOperationException("The headless clipboard is missing.");
        var formats = await clipboard.GetDataFormatsAsync();
        Require(formats.Contains(ClipboardBridge.Png) && formats.Contains(DataFormat.Bitmap),
            "Copy publishes both PNG and bitmap formats for other Windows apps");
        var region = _clipboard!.Region;
        var copiedPixel = _clipboard.Image.GetPixel(region.Width / 2, region.Height / 2);
        Require(copiedPixel.Alpha is > 0 and < 255, "Copy retains partial transparency");
        ClipboardBridge.VerifyWindowsSerialization(_clipboard, original.ID);
        Require(true, "the actual Win32 serializer retains exact application PNG bytes and native PNG alpha without changing the system clipboard");
        await PasteFromSystemAsync();
        var pasted = original.Layers[^1];
        Require(original.Layers.Count == 2 && pasted.Transform.X == region.Left && pasted.Transform.Y == region.Top,
            "pasting into the source document retains the selection position");
        Require(pasted.Asset!.Image.GetPixel(region.Width / 2, region.Height / 2) == copiedPixel,
            "PNG clipboard round trip preserves the copied RGBA pixel");
        Undo();
        Require(original.Layers.Count == 1, "one Undo removes the pasted layer");
        Redo();
        Require(original.Layers.Count == 2, "one Redo restores the pasted layer");
        Undo();

        await CopyToSystemAsync(cut: true);
        using (var rendered = DocumentRenderer.Render(original))
            Require(rendered.GetPixel(9, 9).Alpha == 0, "Cut clears the selected pixels and publishes them to the clipboard");
        Undo();
        Require(original.Layers[0].Asset!.Image.GetPixel(4, 3).Alpha == 128,
            "one Undo restores the pixels cleared by Cut");

        Bring(second);
        await PasteFromSystemAsync();
        pasted = second.Document!.Layers[^1];
        Require(second.Document.Layers.Count == 2 && pasted.Transform.X == (60 - region.Width) / 2
            && pasted.Transform.Y == (50 - region.Height) / 2, "pasting into another document centers the image");
        Undo();
        Require(second.Document.Layers.Count == 1, "cross-document paste remains one undo step");
        await clipboard.SetTextAsync("A newer text-only clipboard");
        await PasteFromSystemAsync();
        Require(second.Document.Layers.Count == 1, "a newer text clipboard does not paste a stale internal image");
        var empty = new Tab();
        _tabs.Add(empty);
        Bring(empty);
        await clipboard.ClearAsync();
        await PasteFromSystemAsync();
        Require(empty.Document is null, "an empty clipboard leaves a blank tab empty");

        var imagePath = Path.Combine(folder, "透明图片.png");
        byte[] png;
        using (var pixels = new SKBitmap(Bitmaps.ColorInfo(8, 6)))
        {
            pixels.Erase(new SKColor(220, 60, 100, 96));
            png = PngCodec.Encode(pixels);
            File.WriteAllBytes(imagePath, png);
        }
        var externalData = new DataTransfer();
        externalData.Add(DataTransferItem.Create(DataFormat.Bitmap, () =>
        {
            using var stream = new MemoryStream(png, writable: false);
            return new Bitmap(stream);
        }));
        await clipboard.SetDataAsync(externalData);
        await PasteFromSystemAsync();
        Require(empty.Document is { Width: 8, Height: 6 } && empty.HasUnsavedChanges,
            "a bitmap-only external clipboard creates a matching unsaved document");
        Require(empty.Document!.Layers[0].Asset!.Image.GetPixel(4, 3).Alpha == 96,
            "bitmap clipboard import retains alpha");
        await PasteFromSystemAsync();
        Require(empty.Document.Layers.Count == 2, "the same external bitmap can be pasted repeatedly");
        var oversized = (byte[])png.Clone();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(oversized.AsSpan(16, 4), DocumentLimits.MaxSide + 1);
        await clipboard.SetValueAsync(ClipboardBridge.Png, oversized);
        await PasteFromSystemAsync();
        Require(empty.Document.Layers.Count == 2 && _message.Contains(UiText.Format(
            "The image exceeds the {0:N0}-pixel side limit or the document's remaining pixel budget.", DocumentLimits.MaxSide)),
            "an oversized clipboard header is rejected before decoding or editing the document");

        var file = await StorageProvider.TryGetFileFromPathAsync(imagePath)
            ?? throw new InvalidOperationException("Headless file storage is unavailable.");
        var fileData = new DataTransfer();
        fileData.Add(DataTransferItem.CreateFile(file));
        var before = empty.Document.Layers.Count;
        var over = new DragEventArgs(DragDrop.DragOverEvent, fileData, _canvas, new Point(10, 10), KeyModifiers.None)
            { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };
        _canvas.RaiseEvent(over);
        Require(over.Handled && over.DragEffects == DragDropEffects.Copy, "routed image drag advertises Copy without moving the source");
        var drop = new DragEventArgs(DragDrop.DropEvent, fileData, _canvas, new Point(10, 10), KeyModifiers.None);
        _canvas.RaiseEvent(drop);
        Require(drop.Handled && drop.DragEffects == DragDropEffects.Copy && empty.Document.Layers.Count == before + 1,
            "a routed file Drop imports the image into the current document");
        Require(File.Exists(imagePath), "dropping an image preserves its source file");
        Undo();
        Require(empty.Document.Layers.Count == before, "a dropped image is removed by one Undo");
        await clipboard.SetFileAsync(file);
        await PasteFromSystemAsync();
        Require(empty.Document.Layers.Count == before + 1, "pasting an Explorer-style file clipboard imports the image");
        var corrupt = Path.Combine(folder, "损坏.png");
        File.WriteAllText(corrupt, "This is not a PNG");
        var made = OpenInputs([imagePath, corrupt, imagePath, firstPath]);
        Require(made == 2 && ReferenceEquals(_open, first), "batch inputs skip duplicate paths and continue after a damaged image");
        Require(_message.Contains(UiText.Format("Opened {0}; failed {1}: {2}", 2, 1, "")),
            "a later success does not hide a batch import failure");
        var projectFolder = await StorageProvider.TryGetFolderFromPathAsync(firstPath)
            ?? throw new InvalidOperationException("Headless folder storage is unavailable.");
        var folderData = new DataTransfer();
        folderData.Add(DataTransferItem.CreateFile(projectFolder));
        var tabCount = _tabs.Count;
        var folderDrop = new DragEventArgs(DragDrop.DropEvent, folderData, _canvas, new Point(10, 10), KeyModifiers.None);
        _canvas.RaiseEvent(folderDrop);
        Require(_tabs.Count == tabCount && ReferenceEquals(_open, first),
            "dropping an already open project folder brings its original tab forward");
        var textData = new DataTransfer();
        textData.Add(DataTransferItem.CreateText("Unsupported dropped text"));
        var textOver = new DragEventArgs(DragDrop.DragOverEvent, textData, _canvas, new Point(10, 10), KeyModifiers.None)
            { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };
        _canvas.RaiseEvent(textOver);
        Require(textOver.DragEffects == DragDropEffects.None, "an unsupported text drag is refused");

        var psdPath = Path.Combine(folder, "分层导入.psd");
        using (var images = new MagickImageCollection())
        {
            images.Add(new MagickImage(MagickColors.Blue, 8, 6) { Depth = 8, Label = "Merged", ColorType = ColorType.TrueColor });
            images.Add(new MagickImage(MagickColors.Red, 8, 6) { Depth = 8, Label = "Red layer", ColorType = ColorType.TrueColor });
            images.Add(new MagickImage(MagickColors.Blue, 8, 6) { Depth = 8, Label = "Blue layer", ColorType = ColorType.TrueColor });
            images.Write(psdPath, MagickFormat.Psd);
        }
        TypeHere(new SKPoint(2, 2));
        TypedText("打开新工程前的文字");
        Require(OpenInput(psdPath), "the desktop opens a generated layered Photoshop file");
        var photoshop = _open;
        Require(photoshop.Document is { Width: 8, Height: 6 } && photoshop.Path is null && photoshop.HasUnsavedChanges,
            "Photoshop import opens an independent unsaved document");
        Require(photoshop.Document!.Layers.Count >= 2
            && photoshop.Document.Layers.Any(layer => layer.Name == "Red layer")
            && photoshop.Document.Layers.Any(layer => layer.Name == "Blue layer"), "Photoshop import retains separate named layers");
        Require(ReferenceEquals(first.Document, original) && first.HasUnsavedChanges,
            "Photoshop import preserves the previous project's unsaved edits");
        Require(original.Layers.Any(layer => layer.LiveText?.Content == "打开新工程前的文字"),
            "opening a new project commits the previous tab's active text edit");
        _pickSavePath = _ => Task.FromResult<string?>(Path.Combine(folder, "Converted.comp"));
        Require(await SaveTabAsync(photoshop), "the imported Photoshop document saves as a native project");
        using (var saved = ProjectStore.Load(photoshop.Path!))
            Require(saved.Manifest.Layers.Count == photoshop.Document.Layers.Count, "saved Photoshop conversion reloads with the same layer count");
        _pickSavePath = null;
        ChangeLanguage("zh-CN");
        Require(UiText.Get("There is no image or supported file to paste.") == "剪贴板中没有图片或支持的文件",
            "Windows integration messages have Chinese translations");
        await clipboard.ClearAsync();
        return string.Join(Environment.NewLine, report);
    }
}
