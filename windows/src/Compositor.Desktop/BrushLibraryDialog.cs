using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed record BrushPreset(BrushTip? Tip, double? Hardness);

internal sealed class BrushLibraryDialog : DialogWindow
{
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly SelectionIndicator _selection = new() { HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(8), Background = Skin.AccentBrush, IsHitTestVisible = false };
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush };
    private readonly Button _import = new() { [!ContentControl.ContentProperty] = UiText.Bind("Import brushes…") };
    private readonly Button _use = new() { [!ContentControl.ContentProperty] = UiText.Bind("Use Brush"), IsDefault = true };
    private readonly List<Bitmap> _thumbnails = [];
    private readonly List<BrushTip> _tips = [];
    private readonly List<BrushPreset> _choices = [];
    private readonly string _libraryPath;
    private bool _closed;
    private int _selected;
    private BrushPreset? _result;
    internal Button ImportButton => _import;
    internal Button UseButton => _use;
    internal IReadOnlyList<BrushTip> ImportedTips => _tips;
    internal StackPanel Rows => _rows;
    internal SelectionIndicator Indicator => _selection;
    internal BrushPreset? Result => _result;

    internal BrushLibraryDialog(BrushSettings current, string? libraryPath = null)
    {
        _libraryPath = libraryPath ?? BrushLibrary.DefaultPath;
        UiText.Set(this, TitleProperty, "Brushes");
        Width = 540; Height = 520; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        try { _tips.AddRange(BrushLibrary.Load(_libraryPath)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { _status.Text = UiText.Format("Could not load brushes: {0}", UiText.Get(error.Message)); _import.IsEnabled = false; }
        _scroll.Content = new Grid { Children = { _selection, _rows } };
        var cancel = new Button { [!ContentControl.ContentProperty] = UiText.Bind("Cancel"), IsCancel = true };
        cancel.Click += (_, _) => Close();
        _use.Click += (_, _) => { _result = _choices[_selected]; Close(); };
        _import.Click += async (_, _) =>
        {
            _import.IsEnabled = false;
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = UiText.Get("Import brushes…"),
                    AllowMultiple = true, FileTypeFilter = [new FilePickerFileType("ABR / PNG") { Patterns = ["*.abr", "*.png"] }] });
                if (!_closed) await ImportFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!_closed) _status.Text = UiText.Format("Could not import brushes: {0}", UiText.Get(error.Message)); }
            finally { if (!_closed) _import.IsEnabled = true; }
        };
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        actions.Children.Add(_import);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, _use } };
        Grid.SetColumn(right, 2); actions.Children.Add(right);
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(18), RowSpacing = 12 };
        grid.Children.Add(new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Import ABR sampled tips or PNG masks. ABR descriptor names, angle, texture, scattering and dual-brush settings are not imported."), TextWrapping = TextWrapping.Wrap });
        Grid.SetRow(_scroll, 1); grid.Children.Add(_scroll);
        Grid.SetRow(_status, 2); grid.Children.Add(_status);
        Grid.SetRow(actions, 3); grid.Children.Add(actions); Content = grid;
        Rebuild();
        var importedIndex = current.Tip is { } tip ? _tips.FindIndex(item => item.ID == tip.ID) : -1;
        _selected = importedIndex >= 0 ? importedIndex + 2 : current.Hardness < 1 ? 1 : 0;
        _rows.SizeChanged += (_, _) => Place();
        Opened += (_, _) => Place();
        KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Up or Key.Down) || args.Source is TextBox) return;
            Pick(Math.Clamp(_selected + (args.Key == Key.Down ? 1 : -1), 0, _choices.Count - 1)); args.Handled = true;
        };
        Closed += (_, _) => { _closed = true; foreach (var image in _thumbnails) image.Dispose(); };
    }

    internal async Task ImportFiles(IEnumerable<string> paths)
    {
        _import.IsEnabled = _use.IsEnabled = false;
        try
        {
            var files = paths.ToArray(); if (files.Length == 0) return;
            var loaded = await Task.Run(() => files.Select(BrushImport.Read).ToArray());
            if (_closed) return;
            var merged = _tips.Concat(loaded.SelectMany(result => result.Tips)).DistinctBy(tip => tip.ID).ToArray();
            await Task.Run(() => BrushLibrary.Save(_libraryPath, merged));
            if (_closed) return;
            var added = merged.Length - _tips.Count;
            _tips.Clear(); _tips.AddRange(merged); Rebuild(); Pick(_choices.Count - 1);
            _status.Text = UiText.Format("Imported {0} brushes; skipped {1} unsupported tips.", added, loaded.Sum(result => result.Skipped));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!_closed) _status.Text = UiText.Format("Could not import brushes: {0}", UiText.Get(error.Message)); }
        finally { if (!_closed) _import.IsEnabled = _use.IsEnabled = true; }
    }

    private void Rebuild()
    {
        _rows.Children.Clear(); foreach (var image in _thumbnails) image.Dispose(); _thumbnails.Clear(); _choices.Clear();
        Add("Hard Round", new BrushPreset(null, 1)); Add("Soft Round", new BrushPreset(null, 0));
        foreach (var tip in _tips) Add(tip.Name, new BrushPreset(tip, null));
        void Add(string name, BrushPreset preset)
        {
            var index = _choices.Count; _choices.Add(preset);
            using var pixels = new SKBitmap(64, 40, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var span = pixels.GetPixelSpan();
            for (var y = 0; y < 40; y++) for (var x = 0; x < 64; x++)
            {
                var coverage = preset.Tip?.Sample(x - 31.5, y - 19.5, 34)
                    ?? BrushEdits.Tip(Math.Sqrt(Math.Pow(x - 31.5, 2) + Math.Pow(y - 19.5, 2)), 17, preset.Hardness!.Value);
                var at = (y * 64 + x) * 4; span[at] = span[at + 1] = span[at + 2] = 255; span[at + 3] = (byte)Math.Round(coverage * 255);
            }
            using var data = pixels.Encode(SKEncodedImageFormat.Png, 100); using var stream = data.AsStream();
            var bitmap = new Bitmap(stream); _thumbnails.Add(bitmap);
            var title = new TextBlock { Text = preset.Tip is null ? UiText.Get(name) : name, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,12,*") };
            row.Children.Add(new Image { Source = bitmap, Width = 64, Height = 40 }); Grid.SetColumn(title, 2); row.Children.Add(title);
            var button = new Button { Classes = { "plain", "selection-item" }, Height = 48, Padding = new Thickness(10, 4),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = row };
            ToolTip.SetTip(button, preset.Tip is null ? UiText.Get(name) : name);
            button.Click += (_, _) => Pick(index); _rows.Children.Add(button);
        }
    }
    private void Pick(int index) { _selected = index; Place(); _rows.Children[index].BringIntoView(); }
    private void Place()
    {
        if (_selected >= _rows.Children.Count || _rows.Children[_selected].Bounds.Width <= 0) return;
        var row = _rows.Children[_selected]; _selection.MoveTo(new Rect(row.Bounds.Position, row.Bounds.Size));
    }
    internal static async Task<BrushPreset?> Ask(Window owner, BrushSettings current)
    {
        var dialog = new BrushLibraryDialog(current); await dialog.ShowDialog(owner); return dialog._result;
    }
}
