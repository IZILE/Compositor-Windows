using Avalonia.Input;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.IO.PSD;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private bool _clipboardBusy;

    private static bool SameProjectPath(string? first, string second) => first is not null
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeInput(string path) => Directory.Exists(path) || ImageImporter.LooksImportable(path);

    private void DragOverFiles(object? sender, DragEventArgs args)
    {
        try
        {
            var accepted = args.DataTransfer.TryGetFiles()?.Any(item =>
                item.TryGetLocalPath() is { } path && LooksLikeInput(path)) == true;
            args.DragEffects &= accepted ? DragDropEffects.Copy : DragDropEffects.None;
        }
        catch { args.DragEffects = DragDropEffects.None; }
        args.Handled = true;
    }

    private void DropFiles(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        try
        {
            var opened = OpenInputs(args.DataTransfer.TryGetFiles()?.Select(item => item.TryGetLocalPath()).OfType<string>() ?? []);
            args.DragEffects = opened > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        catch (Exception error)
        {
            args.DragEffects = DragDropEffects.None;
            Say(UiText.Format("Could not open: {0}", ErrorText(error)));
        }
    }

    /// <summary>Each input is independent, so a damaged file does not prevent later files from opening.</summary>
    private int OpenInputs(IEnumerable<string> paths)
    {
        var opened = 0;
        var errors = new List<string>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { OpenInputCore(path); opened++; }
            catch (Exception error) { errors.Add($"{Path.GetFileName(path)}: {ErrorText(error)}"); }
        }
        if (errors.Count > 0)
            Say(UiText.Format("Opened {0}; failed {1}: {2}", opened, errors.Count, string.Join("; ", errors)));
        return opened;
    }

    private void OpenInputCore(string path)
    {
        if (Directory.Exists(path)) Open(path);
        else if (!File.Exists(path)) throw new FileNotFoundException(UiText.Get("The file or project folder could not be found."), path);
        else if (Path.GetExtension(path).Equals(".psd", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path).Equals(".psb", StringComparison.OrdinalIgnoreCase)) ImportPhotoshop(path);
        else if (ImageImporter.LooksImportable(path)) ImportPath(path);
        else throw new ImportException(ImportError.Unsupported);
    }

    private void ImportPhotoshop(string path)
    {
        var imported = PsdImporter.Read(path);
        var document = imported.Snapshot().ToDocument();
        _open = TabForNew();
        _document = document;
        _projectPath = null;
        _history.Reset();
        Show(_open);
        var notes = imported.Notes.Count == 0 ? "" : " — " + string.Join("; ",
            imported.Notes.Select(note => $"{note.Layer}: {UiText.Get(note.What)}"));
        Say(UiText.Format("Imported Photoshop document: {0} layers; {1} conversion notes",
            document.Layers.Count, imported.Notes.Count) + notes);
    }

    private static string ErrorText(Exception error) => error switch
    {
        ImportException { Error: ImportError.TooLarge } => UiText.Format(
            "The image exceeds the {0:N0}-pixel side limit or the document's remaining pixel budget.", DocumentLimits.MaxSide),
        ImportException { Error: ImportError.Unsupported } => UiText.Get("Choose a supported image file or a .comp project folder."),
        ImportException { Error: ImportError.Unreadable } => UiText.Get("The image could not be read. It may be damaged or unavailable."),
        UnauthorizedAccessException => UiText.Get("Access was denied. Choose a folder you can write to, or check the file permissions."),
        _ => UiText.Get(error.Message),
    };

    private async Task CopyToSystemAsync(bool merged = false, bool cut = false)
    {
        if (_clipboardBusy || _document is not { } document) return;
        _clipboardBusy = true;
        try
        {
            CommitText();
            var id = Selected;
            ClipboardImage? copied;
            if (cut)
            {
                if (id is null) return;
                _history.Begin("Cut", document, id);
                try
                {
                    if (!SelectionClipboard.Cut(document, id.Value, out copied))
                    {
                        copied?.Dispose();
                        Say(UiText.Get("Select something on a layer with pixels of its own first"));
                        return;
                    }
                }
                finally { _history.End(document, id); }
                Reselect(id);
            }
            else copied = merged ? SelectionClipboard.CopyMerged(document)
                : id is { } selected ? SelectionClipboard.Copy(document, selected) : null;
            if (copied is null)
            {
                Say(UiText.Get(merged ? "Select something to copy first" : "Select something on a layer with pixels of its own first"));
                return;
            }
            Adopt(copied);
            if (Clipboard is { } system) await ClipboardBridge.WriteAsync(system, copied, document.ID);
            Say(UiText.Format(cut ? "Cut {0} × {1}" : merged
                ? "Copied {0} × {1} from the flattened picture" : "Copied {0} × {1}",
                copied.Region.Width, copied.Region.Height));
        }
        catch (Exception error) { Say(UiText.Format("Could not copy to the system clipboard: {0}", ErrorText(error))); }
        finally { _clipboardBusy = false; }
    }

    private async Task PasteFromSystemAsync()
    {
        if (_clipboardBusy) return;
        _clipboardBusy = true;
        var tab = _open;
        try
        {
            CommitText();
            if (Clipboard is not { } system)
            {
                if (_clipboard is { } local) PasteImage(local);
                else Say(UiText.Get("There is nothing to paste"));
                return;
            }
            using var data = await system.TryGetDataAsync();
            if (data is null) { Say(UiText.Get("There is nothing to paste")); return; }
            var files = await data.TryGetFilesAsync();
            if (!ReferenceEquals(_open, tab) || _allowClose) return;
            if (files is { Length: > 0 })
            {
                OpenInputs(files.Select(item => item.TryGetLocalPath()).OfType<string>());
                return;
            }
            using var image = await ClipboardBridge.ReadAsync(data, tab.Document);
            if (!ReferenceEquals(_open, tab) || _allowClose) return;
            if (image is null) { Say(UiText.Get("There is no image or supported file to paste.")); return; }
            PasteImage(image);
        }
        catch (Exception error) { Say(UiText.Format("Could not paste: {0}", ErrorText(error))); }
        finally { _clipboardBusy = false; }
    }

    private void PasteImage(ClipboardImage clipboard)
    {
        if (_document is not { } document)
        {
            var pixels = clipboard.Image.Copy();
            if (pixels is null) throw new InvalidOperationException(UiText.Get("The image could not be read. It may be damaged or unavailable."));
            _document = ImageImporter.NewDocument(ImportedImage.Create(pixels, UiText.Get("Pasted Layer")));
            _projectPath = null;
            _history.Reset();
            Show(_open);
        }
        else
        {
            Guid? made;
            _history.Begin("Paste", document, Selected);
            try { made = SelectionClipboard.Paste(document, clipboard, Selected); }
            finally { _history.End(document, Selected); }
            if (made is null)
            {
                Say(UiText.Get("The document has reached its layer limit or pixel budget."));
                return;
            }
            Reselect(made);
        }
        Say(UiText.Format("Pasted {0} × {1}", clipboard.Region.Width, clipboard.Region.Height));
    }
}
