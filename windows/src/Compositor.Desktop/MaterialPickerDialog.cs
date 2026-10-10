using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Compositor.Core.Document;
using Compositor.Core.IO;

namespace Compositor.Desktop;

/// <summary>One selection surface and the same immediate rendering path for all imported material types.</summary>
internal sealed class MaterialPickerDialog<T> : DialogWindow where T : class
{
    private readonly StackPanel _rows = new() { Spacing = 4 };
    private readonly SelectionIndicator _selection = new() { CornerRadius = new CornerRadius(8), Background = Skin.AccentBrush };
    private readonly MaterialPreview _preview = new() { Height = 150 };
    private readonly TextBlock _name = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Skin.SecondaryBrush };
    private readonly Button _import = new() { [!ContentControl.ContentProperty] = UiText.Bind("Import…") };
    private readonly Button _use = new() { [!ContentControl.ContentProperty] = UiText.Bind("Use"), IsDefault = true, Classes = { "accent" } };
    private readonly Button _cancel = new() { [!ContentControl.ContentProperty] = UiText.Bind("Cancel"), IsCancel = true };
    private readonly List<MaterialPreview> _thumbnails = [];
    private readonly List<T> _imported = [];
    private readonly List<T> _choices = [];
    private readonly IReadOnlyList<T> _builtIn;
    private readonly Func<string, IReadOnlyList<T>> _read;
    private readonly Func<T, string> _title;
    private readonly string _library;
    private readonly FilePickerFileType _fileType;
    private bool _closed;
    private int _selected;
    internal T? Result { get; private set; }
    internal event Action<T>? PreviewChanged;
    internal StackPanel Rows => _rows;
    internal SelectionIndicator Indicator => _selection;
    internal MaterialPreview Preview => _preview;
    internal Button UseButton => _use;
    internal Button CancelButton => _cancel;
    internal IReadOnlyList<T> Choices => _choices;

    internal MaterialPickerDialog(string title, string hint, IReadOnlyList<T> builtIn, Func<T,string> name,
        Func<string,IReadOnlyList<T>> read, FilePickerFileType fileType, string library, T? current = null)
    {
        UiText.Set(this, TitleProperty, title);
        Width = 680; Height = 540; MinWidth = 620; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _builtIn = builtIn; _title = name; _read = read; _library = library; _fileType = fileType;
        try { _imported.AddRange(MaterialLibrary.Load<T>(library)); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { _status.Text = UiText.Format("Could not load materials: {0}", UiText.Get(error.Message)); _import.IsEnabled = false; }
        _cancel.Click += (_,_) => Close();
        _use.Click += (_,_) => { Result = _choices[_selected]; Close(); };
        _import.Click += async (_,_) =>
        {
            _import.IsEnabled = false;
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = UiText.Get("Import…"), AllowMultiple = true, FileTypeFilter = [_fileType] });
                if (!_closed) await ImportFiles(files.Select(file => file.TryGetLocalPath()).OfType<string>());
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            { if (!_closed) _status.Text = UiText.Format("Could not import materials: {0}", UiText.Get(error.Message)); }
            finally { if (!_closed) _import.IsEnabled = true; }
        };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Grid { Children = { _selection, _rows } } };
        var detail = new StackPanel { Spacing = 14, Children = {
            new Border { Padding = new Thickness(10), Background = Skin.PasteboardBrush, CornerRadius = new CornerRadius(8), Child = _preview }, _name
        } };
        UiText.Set(_import, ToolTip.TipProperty, hint);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,240"), ColumnSpacing = 18 };
        body.Children.Add(scroll); Grid.SetColumn(detail,1); body.Children.Add(detail);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") }; actions.Children.Add(_import);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _cancel, _use } };
        Grid.SetColumn(right,2); actions.Children.Add(right);
        var grid = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 14 };
        grid.Children.Add(body); Grid.SetRow(_status,1); grid.Children.Add(_status); Grid.SetRow(actions,2); grid.Children.Add(actions); Content = grid;
        Rebuild();
        var selected = current is null ? -1 : _choices.FindIndex(choice => Equals(choice,current) || _title(choice) == _title(current));
        Pick(Math.Max(0,selected));
        _rows.SizeChanged += (_,_) => Place(); Opened += (_,_) => Place();
        KeyDown += (_,args) =>
        {
            if (args.Key is not (Key.Up or Key.Down) || args.Source is TextBox) return;
            Pick(Math.Clamp(_selected+(args.Key == Key.Down ? 1 : -1),0,_choices.Count-1)); args.Handled=true;
        };
        Closed += (_,_) => { _closed=true; _preview.Dispose(); foreach (var preview in _thumbnails) preview.Dispose(); };
    }
    internal async Task ImportFiles(IEnumerable<string> paths)
    {
        _import.IsEnabled = _use.IsEnabled = false;
        try
        {
            var files = paths.ToArray(); if (files.Length == 0) return;
            var held = _imported.ToArray();
            var merged = await Task.Run(() =>
            {
                var loaded = files.SelectMany(path => _read(path)).ToArray();
                var result = held.Concat(loaded).DistinctBy(item => System.Text.Json.JsonSerializer.Serialize(item)).ToArray();
                MaterialLibrary.Save(_library,result); return result;
            });
            if (_closed) return;
            var added = merged.Length-_imported.Count; _imported.Clear(); _imported.AddRange(merged); Rebuild(); Pick(_choices.Count-1);
            _status.Text = UiText.Format("Imported {0} materials.",added);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!_closed) _status.Text = UiText.Format("Could not import materials: {0}",UiText.Get(error.Message)); }
        finally { if (!_closed) _import.IsEnabled = _use.IsEnabled = true; }
    }
    private void Rebuild()
    {
        _rows.Children.Clear(); foreach (var preview in _thumbnails) preview.Dispose(); _thumbnails.Clear();
        _choices.Clear(); _choices.AddRange(_builtIn); _choices.AddRange(_imported);
        for (var i=0;i<_choices.Count;i++)
        {
            var index = i; var choice = _choices[i];
            var preview = new MaterialPreview { Width=72,Height=40 }; preview.Show(choice,72,40); _thumbnails.Add(preview);
            var label = new TextBlock { Text = i<_builtIn.Count ? UiText.Get(_title(choice)) : _title(choice), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("72,12,*") }; row.Children.Add(preview); Grid.SetColumn(label,2); row.Children.Add(label);
            var button = new Button { Classes = { "plain","selection-item" }, Height=48, Padding=new Thickness(8,4), HorizontalContentAlignment = HorizontalAlignment.Stretch, Content=row };
            ToolTip.SetTip(button,label.Text); button.Click += (_,_) => Pick(index); _rows.Children.Add(button);
        }
    }
    internal void Pick(int index)
    {
        _selected=index; var choice=_choices[index]; _preview.Show(choice); _name.Text=UiText.Get(_title(choice));
        Place(); _rows.Children[index].BringIntoView(); PreviewChanged?.Invoke(choice);
    }
    private void Place()
    {
        if (_selected >= _rows.Children.Count || _rows.Children[_selected].Bounds.Width <= 0) return;
        var row=_rows.Children[_selected]; _selection.MoveTo(new Rect(row.Bounds.Position,row.Bounds.Size));
    }
}

internal static class MaterialPickers
{
    internal static MaterialPickerDialog<ShapePreset> Shapes(ShapePreset? current = null, string? library = null) => new("Shapes",
        "Import SVG silhouettes. Convert text, strokes and effects to filled paths first.",MaterialPresets.Shapes,p=>p.Name,MaterialImport.Shapes,
        new FilePickerFileType("SVG") { Patterns=["*.svg"] },library??UserData.File("shapes.json"),current);
    internal static MaterialPickerDialog<GradientPreset> Gradients(GradientPreset? current = null, string? library = null, GradientPreset? foreground = null) => new("Gradients",
        "Import GGR gradients with fixed, linear RGB segments.",foreground is null ? MaterialPresets.Gradients : new[] {foreground}.Concat(MaterialPresets.Gradients).ToArray(),p=>p.Name,MaterialImport.Gradients,
        new FilePickerFileType("GGR") { Patterns=["*.ggr"] },library??UserData.File("gradients.json"),current);
    internal static MaterialPickerDialog<PatternPreset> Patterns(string? library = null) => new("Patterns",
        "PNG / JPEG / WebP · Up to 2048 × 2048 pixels",MaterialPresets.Patterns,p=>p.Name,MaterialImport.Patterns,
        new FilePickerFileType("PNG / JPEG / WebP") { Patterns=["*.png","*.jpg","*.jpeg","*.webp"] },library??UserData.File("patterns.json"));
    internal static MaterialPickerDialog<ColorPreset> Colors(string? library = null) => new("Swatches",
        "GPL color palettes",MaterialPresets.Colors,p=>p.Name,MaterialImport.Colors,
        new FilePickerFileType("GPL") { Patterns=["*.gpl"] },library??UserData.File("swatches.json"));
}
