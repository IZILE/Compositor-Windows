using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Guid? _textStyleHistory;
    private bool _textColorOpen;

    private LayerTextStyle CurrentTextStyle => _text?.Style
        ?? (Selected is { } id ? _document?.Layers.FirstOrDefault(layer => layer.ID == id)?.Text?.Style : null)
        ?? _options.TextStyle;

    private void WireEditingControls()
    {
        _optionsBar.SelectionAdjusted += AdjustSelection;
        _optionsBar.SelectionDeselected += Deselect;
        _optionsBar.TextStyleStarted += BeginTextStyle;
        _optionsBar.TextStyleChanged += ChangeTextStyle;
        _optionsBar.TextStyleFinished += EndTextStyle;
        _optionsBar.TextDone += () => { CommitText(); _canvas.Focus(); };
        _optionsBar.TextCancelled += () => { CancelText(); _canvas.Focus(); };
        _optionsBar.TextColorAsked += OpenTextColor;
        _optionsBar.TextCanvasFocused += () => _canvas.Focus();
        Deactivated += (_, _) => ShowSelectionModifiers(KeyModifiers.None);
        _canvas.SelectionModeDisplayed = mode => _optionsBar.ShowSelection(mode, _document?.Selection.Path is not null);
    }

    private void RefreshEditingControls()
    {
        _canvas.SelectionModeChoice = _options.SelectionMode;
        _optionsBar.ShowSelection(_options.SelectionMode, _document?.Selection.Path is not null);
        var canEdit = Selected is { } id && _document?.Layers.Any(layer => layer.ID == id && layer.Text is not null) == true;
        _optionsBar.ShowText(CurrentTextStyle, _text is not null, canEdit, _tool == Tool.Type);
    }

    private void ShowSelectionModifiers(KeyModifiers modifiers) =>
        _optionsBar.ShowSelection(_canvas.ModeOf(modifiers), _document?.Selection.Path is not null);

    private void AdjustSelection(SelectionAdjustment adjustment, int amount)
    {
        if (_document?.Selection.Path is null) return;
        _canvas.CancelDraft();
        Change(adjustment + " Selection", document => adjustment switch
        {
            SelectionAdjustment.Expand => SelectionEdits.Expand(document, amount),
            SelectionAdjustment.Contract => SelectionEdits.Contract(document, amount),
            _ => SelectionEdits.Feather(document, amount),
        });
        _canvas.Focus();
    }

    private void BeginTextStyle()
    {
        if (_text is not null || _textStyleHistory is not null || _document is not { } document
            || Selected is not { } id || !document.Layers.Any(layer => layer.ID == id && layer.Text is not null)) return;
        _textStyleHistory = id;
        _history.Begin("Text Style", document, id);
    }

    private void ChangeTextStyle(Action<LayerTextStyle> change)
    {
        if (_document is not { } document) return;
        if (_text is { } session)
        {
            if (!session.ChangeStyle(document, change)) { Say("That text could not be drawn"); RefreshEditingControls(); return; }
            RememberTextStyle(session.Style);
            ShowText();
            return;
        }
        var wanted = CopyTextStyle(CurrentTextStyle);
        change(wanted);
        if (!wanted.IsValid || string.IsNullOrWhiteSpace(wanted.FontName)) return;
        if (Selected is { } id && document.Layers.Any(layer => layer.ID == id && layer.Text is not null))
        {
            var grouped = _textStyleHistory is not null;
            if (!grouped) BeginTextStyle();
            if (!TextEdits.SetStyle(document, id, wanted)) Say("That text could not be drawn");
            else RememberTextStyle(wanted);
            if (!grouped) EndTextStyle();
        }
        else RememberTextStyle(wanted);
        Refresh();
    }

    private void EndTextStyle()
    {
        if (_textStyleHistory is { } id && _document is { } document)
        {
            _textStyleHistory = null;
            _history.End(document, id);
            Refresh();
        }
        // Text fields retain Windows keyboard editing; only a toolbar commit returns typing to the canvas.
    }

    private void RememberTextStyle(LayerTextStyle style)
    {
        _options.TextStyle = CopyTextStyle(style);
        _options.TextStyle.Content = ""; _options.TextStyle.BoxSize = null;
        _options.TextStyle.ColorRuns = null; _options.TextStyle.FontRuns = null;
    }

    private void OpenTextColor()
    {
        if (_document is null || _textColorOpen) return;
        _textColorOpen = true;
        var original = CopyTextStyle(CurrentTextStyle);
        var originalLayer = _text is null && Selected is { } id
            ? _document.Layers.FirstOrDefault(layer => layer.ID == id && layer.Text is not null)?.Clone() : null;
        BeginTextStyle();
        var picker = new ColorPickerDialog("Text color", (original.Red, original.Green, original.Blue));
        void Preview((double Red, double Green, double Blue) color)
        {
            var current = CurrentTextStyle;
            if (current.ColorRuns is null && Math.Abs(current.Red - color.Red) < 1e-12
                && Math.Abs(current.Green - color.Green) < 1e-12 && Math.Abs(current.Blue - color.Blue) < 1e-12) return;
            ChangeTextStyle(style => { style.Red = color.Red; style.Green = color.Green; style.Blue = color.Blue; style.ColorRuns = null; });
        }
        picker.Moved += Preview;
        picker.Applied += Preview;
        picker.Cancelled += () =>
        {
            if (originalLayer is not null && _document?.Layers.FirstOrDefault(layer => layer.ID == originalLayer.ID) is { } current)
            {
                current.Asset = originalLayer.Asset; current.Transform = originalLayer.Transform;
                current.Name = originalLayer.Name; current.Text = originalLayer.Text;
                RememberTextStyle(original); Refresh();
            }
            else ChangeTextStyle(style =>
            { style.Red = original.Red; style.Green = original.Green; style.Blue = original.Blue; style.ColorRuns = original.ColorRuns; });
        };
        picker.Closed += (_, _) => { _textColorOpen = false; EndTextStyle(); _canvas.Focus(); };
        picker.ShowDialog(this);
    }

    private static LayerTextStyle CopyTextStyle(LayerTextStyle style) => new()
    {
        Content = style.Content, FontName = style.FontName, FontSize = style.FontSize,
        Red = style.Red, Green = style.Green, Blue = style.Blue, Alignment = style.Alignment,
        Tracking = style.Tracking, Leading = style.Leading, BoxSize = style.BoxSize,
        ColorRuns = style.ColorRuns?.Select(run => new LayerTextColorRun
        { Location = run.Location, Length = run.Length, Red = run.Red, Green = run.Green, Blue = run.Blue }).ToList(),
        FontRuns = style.FontRuns?.Select(run => new LayerTextFontRun
        { Location = run.Location, Length = run.Length, FontName = run.FontName }).ToList(),
    };
}
