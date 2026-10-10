using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using Compositor.Core.IO;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    private readonly ComboBox _textFont = new() { Width = 210 };
    private readonly Button _importFont = new() { [!ContentControl.ContentProperty] = UiText.Bind("Import Font…"), Focusable = false };
    private readonly InlineNumber _textSize = new("Size", 1, 2000, unit: "px", fieldWidth: 52, format: "0.##");
    private readonly Swatch _textColor = new();
    private readonly SegmentedChoice _textAlignment = new("Left", "Center", "Right");
    private readonly InlineNumber _textTracking = new("Tracking", -100, 1000, fieldWidth: 48, format: "0.##");
    private readonly InlineNumber _textLeading = new("Leading", 0, 5000, fieldWidth: 52, format: "0.##");
    private readonly Button _textDone = new() { [!ContentControl.ContentProperty] = UiText.Bind("Done"), Classes = { "accent" }, Focusable = false };
    private readonly Button _textCancel = new() { [!ContentControl.ContentProperty] = UiText.Bind("Cancel"), Focusable = false };
    private readonly StackPanel _textActions = new() { Orientation = Orientation.Horizontal, Spacing = 8,
        Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private LayerTextStyle _shownText = new();
    private static readonly Lazy<string[]> InstalledFonts = new(() => SkiaSharp.SKFontManager.Default.FontFamilies
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToArray());
    private bool _fontsLoaded;
    internal event Action<Action<LayerTextStyle>>? TextStyleChanged;
    internal event Action? TextStyleStarted;
    internal event Action? TextStyleFinished;
    internal event Action? TextColorAsked;
    internal event Action? TextDone;
    internal event Action? TextCancelled;
    internal event Action? TextCanvasFocused;
    internal event Action? FontImportAsked;
    internal Button ImportFontButton => _importFont;
    internal InlineNumber TextSizeControl => _textSize;
    internal InlineNumber TextTrackingControl => _textTracking;
    internal InlineNumber TextLeadingControl => _textLeading;
    internal ComboBox TextFontControl => _textFont;
    internal SegmentedChoice TextAlignmentControl => _textAlignment;
    internal Button TextDoneButton => _textDone;
    internal Button TextCancelButton => _textCancel;
    internal Button TextColorButton => _textColor;

    private void BuildType()
    {
        foreach (var field in new[] { _textSize, _textTracking, _textLeading })
        {
            field.EditingStarted += () => TextStyleStarted?.Invoke();
            field.EditingFinished += () => TextStyleFinished?.Invoke();
            field.EditingCommitted += () => TextCanvasFocused?.Invoke();
        }
        void Change(Action<LayerTextStyle> change) { if (!_loading) TextStyleChanged?.Invoke(change); }
        _textSize.Changed += value => Change(style => style.FontSize = value);
        _textTracking.Changed += value => Change(style => style.Tracking = value);
        _textLeading.Changed += value => Change(style => style.Leading = value);
        ToolTip.SetTip(_textLeading, UiText.Get("0 = Auto (120% of font size)"));
        _textAlignment.Changed += index =>
        {
            TextStyleStarted?.Invoke();
            Change(style => style.Alignment = index switch { 1 => Compositor.Core.Format.TextAlignment.Center, 2 => Compositor.Core.Format.TextAlignment.Right, _ => Compositor.Core.Format.TextAlignment.Left });
            TextStyleFinished?.Invoke();
            TextCanvasFocused?.Invoke();
        };
        _textFont.DropDownOpened += (_, _) => LoadTextFonts();
        _textFont.DropDownClosed += (_, _) => TextCanvasFocused?.Invoke();
        _textFont.ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
        { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        _textFont.SelectionChanged += (_, _) =>
        {
            if (_loading || _textFont.SelectedItem is not string font || font == _shownText.FontName) return;
            TextStyleStarted?.Invoke();
            Change(style => { style.FontName = font; style.FontRuns = null; });
            TextStyleFinished?.Invoke();
        };
        ToolTip.SetTip(_textFont, UiText.Get("Font"));
        UiText.Set(_importFont, ToolTip.TipProperty, "TTF / OTF");
        _importFont.Click += (_, _) => FontImportAsked?.Invoke();
        _textColor.Click += (_, _) => TextColorAsked?.Invoke();
        _textDone.Click += (_, _) => TextDone?.Invoke(); _textCancel.Click += (_, _) => TextCancelled?.Invoke();
        _textActions.Children.Add(_textCancel); _textActions.Children.Add(_textDone);
        Cell("type", _textFont); Cell("type", _importFont); Cell("type", _textSize); Cell("type", _textColor); Cell("type", _textAlignment);
        Cell("type", _textTracking); Cell("type", _textLeading); Cell("type", _editText);
    }

    internal void LoadTextFonts(bool reload = false)
    {
        if (_fontsLoaded && !reload) return;
        var wasLoading = _loading; _loading = true;
        try
        {
            _fontsLoaded = true;
            _textFont.ItemsSource = InstalledFonts.Value.Concat(FontLibrary.Current.Entries.Select(font => font.Name))
                .Append(_shownText.FontName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name).ToArray();
            _textFont.SelectedItem = _shownText.FontName;
        }
        finally { _loading = wasLoading; }
    }

    internal void ShowText(LayerTextStyle style, bool editing, bool canEdit, bool visible)
    {
        var wasLoading = _loading; _loading = true;
        try
        {
            _shownText = style;
            var names = _textFont.ItemsSource as IEnumerable<string>;
            if (names is null || !names.Contains(style.FontName))
                _textFont.ItemsSource = (names ?? []).Append(style.FontName).ToArray();
            _textFont.SelectedItem = style.FontName;
            _textSize.Value = style.FontSize; _textTracking.Value = style.Tracking; _textLeading.Value = style.Leading;
            _textAlignment.SelectedIndex = (int)style.Alignment;
            _textColor.Show(style.Red, style.Green, style.Blue);
            _editText.IsVisible = visible && !editing; _editText.IsEnabled = canEdit;
            _textActions.IsVisible = visible && editing;
        }
        finally { _loading = wasLoading; }
    }
}
