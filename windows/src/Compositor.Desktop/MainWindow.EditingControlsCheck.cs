using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal string EditingControlsSelfCheck(string sample, string output)
    {
        Directory.CreateDirectory(output); OpenInput(sample); Show(); UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var report = new List<string>();
        void Check(bool good, string name) { if (!good) throw new InvalidOperationException("EDITING CONTROLS: " + name); report.Add("PASS: " + name); }
        void Layout() { UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Control control)
        {
            control.BringIntoView(); Layout();
            var root = TopLevel.GetTopLevel(control)!;
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
            root.MouseMove(at, RawInputModifiers.None); root.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            root.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Layout();
        }
        void Number(InlineNumber field, string value)
        {
            Click(field.Field); field.Field.Text = value; this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Layout();
        }
        void Capture(string name)
        {
            Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var bitmap = this.CaptureRenderedFrame()!;
            bitmap.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        void Drag(SKPoint first, SKPoint last, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var from = _canvas.TranslatePoint(_canvas.InView(first), this)!.Value;
            var to = _canvas.TranslatePoint(_canvas.InView(last), this)!.Value;
            this.MouseDown(from, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
            this.MouseMove(to, modifiers | RawInputModifiers.LeftMouseButton); this.MouseUp(to, MouseButton.Left, modifiers); Layout();
        }
        var document = _document!;
        foreach (var language in new[] { "en", "zh-CN" })
        {
            ChangeLanguage(language); Width = 1180; Height = 780; Layout(); _canvas.Fit();
            SetTool(Tool.Marquee); Deselect();
            Check(!_optionsBar.DeselectButton.IsEnabled, language + ": empty selection disables modification actions");
            Click(_optionsBar.SelectionModeControl.ButtonAt(0)); Drag(new SKPoint(50, 400), new SKPoint(150, 500));
            Click(_optionsBar.SelectionModeControl.ButtonAt(1)); Drag(new SKPoint(200, 400), new SKPoint(300, 500));
            Check(document.Selection.Path!.Contains(100, 450) && document.Selection.Path.Contains(250, 450), language + ": Add combines two actual marquee drags");
            Click(_optionsBar.SelectionModeControl.ButtonAt(2)); Drag(new SKPoint(225, 425), new SKPoint(275, 475));
            Check(!document.Selection.Path!.Contains(250, 450) && document.Selection.Path.Contains(210, 410), language + ": Subtract cuts the actual marquee mask");
            Click(_optionsBar.SelectionModeControl.ButtonAt(0));
            _canvas.Focus(); this.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.None, null);
            Check(_optionsBar.SelectionModeControl.SelectedIndex == 1 && _options.SelectionMode == SelectionMode.Replace, language + ": held Shift previews Add without changing the saved choice");
            this.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null);
            Check(_optionsBar.SelectionModeControl.SelectedIndex == 0, language + ": releasing Shift restores the chosen mode");
            Click(_optionsBar.SelectionModeControl.ButtonAt(1));
            Drag(new SKPoint(60, 410), new SKPoint(90, 440), RawInputModifiers.Alt);
            Check(!document.Selection.Path!.Contains(75, 425) && _options.SelectionMode == SelectionMode.Add, language + ": Alt overrides a stored Add choice for one outline");
            var history = _history.UndoCount;
            Number(_optionsBar.SelectionAmountControl(SelectionAdjustment.Expand), "6");
            Click(_optionsBar.SelectionAdjustmentButton(SelectionAdjustment.Expand));
            Check(_history.UndoCount == history + 1 && document.Selection.Path!.Contains(46, 450), language + ": Expand uses the inline amount and creates one undo");
            Undo(); Redo();
            Click(_optionsBar.SelectionAdjustmentButton(SelectionAdjustment.Contract));
            Click(_optionsBar.SelectionAdjustmentButton(SelectionAdjustment.Feather));
            Check(document.Selection.Feather == _options.SelectionFeather, language + ": Feather uses the inline amount");
            Capture("selection-" + language);
            Click(_optionsBar.DeselectButton); Check(document.Selection.Path is null, language + ": Deselect clears the outline");
            SetTool(Tool.Wand); Number(_optionsBar.WandToleranceControl, "77");
            Check(_options.Wand.Tolerance == 77, language + ": wand tolerance edits directly without a prompt");
            _optionsBar.WandSampleControl.SelectedIndex = 0; Check(_options.Wand.Radius == 0, language + ": point sampling is available");
            _optionsBar.WandSampleControl.SelectedIndex = 4; Check(_options.Wand.Radius == 15, language + ": 31 by 31 average sets the actual sample radius");
            _options.Wand = _options.Wand with { Radius = 4 }; OptionsChanged();
            Check(_optionsBar.WandSampleControl.SelectedIndex == 6 && _options.Wand.Radius == 4, language + ": custom menu sample radius is retained");

            SetTool(Tool.Type); _options.TextStyle.Red = _options.TextStyle.Green = _options.TextStyle.Blue = 1;
            TypeHere(new SKPoint(70, 620)); _canvas.Focus(); this.KeyTextInput("Hello 你好"); Layout();
            Check(_text is not null && _text.Content == "Hello 你好", language + ": typing on canvas creates a live draft");
            var textID = _text!.LayerID!.Value; var caret = _text.CaretIndex;
            Number(_optionsBar.TextSizeControl, "38"); Number(_optionsBar.TextTrackingControl, "3"); Number(_optionsBar.TextLeadingControl, "46");
            Check(_canvas.IsFocused, language + ": Enter in a style field returns keyboard focus to the canvas");
            this.KeyTextInput("!"); caret++; Check(_text.Content == "Hello 你好!", language + ": typing continues immediately after a toolbar edit");
            Click(_optionsBar.TextAlignmentControl.ButtonAt(2));
            Check(_text.Style.FontSize == 38 && _text.Style.Tracking == 3 && _text.Style.Leading == 46
                && _text.Style.Alignment == Compositor.Core.Format.TextAlignment.Right && _text.CaretIndex == caret, language + ": size tracking leading and alignment update the draft without losing the caret");
            _optionsBar.LoadTextFonts();
            Check(_optionsBar.TextFontControl.Items.Count > 2, language + ": installed font catalog is populated on demand");
            _optionsBar.TextFontControl.SelectedItem = "Consolas"; Layout();
            Check(_text.Style.FontName == "Consolas", language + ": choosing a face rerenders the active text");
            Capture("type-draft-" + language);
            Click(_optionsBar.TextColorButton); var picker = OwnedWindows.OfType<ColorPickerDialog>().Single();
            picker.Sample((.2, .3, 1)); Layout(); Check(Math.Abs(_text.Style.Red - .2) < .00001, language + ": text color previews live");
            Click(picker.Cancel); Check(Math.Abs(_text.Style.Red - 1) < .00001, language + ": color Cancel restores the previous text color");
            var beforeCommit = _history.UndoCount; Click(_optionsBar.TextDoneButton);
            Check(_text is null && _history.UndoCount == beforeCommit + 1, language + ": Done commits typing and all draft styling as one undo");
            Undo(); Check(!document.Layers.Any(layer => layer.ID == textID), language + ": one undo removes the entire new styled text");
            Redo(); Reselect(textID); Layout();
            var beforeStyle = _history.UndoCount; Click(_optionsBar.TextSizeControl.Field);
            this.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null); this.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null); Layout();
            Check(_history.UndoCount == beforeStyle + 1 && document.Layers.First(layer => layer.ID == textID).Text!.Style.FontSize == 40,
                language + ": repeated numeric arrow edits on committed text form one undo");
            Undo(); Check(document.Layers.First(layer => layer.ID == textID).Text!.Style.FontSize == 38, language + ": undo restores the prior committed text style");
            var beforeCancel = _history.UndoCount; var hadRedo = _history.CanRedo;
            TypeHere(new SKPoint((float)document.Layers.First(layer => layer.ID == textID).Transform.X + 4, (float)document.Layers.First(layer => layer.ID == textID).Transform.Y + 4));
            Number(_optionsBar.TextSizeControl, "50"); Click(_optionsBar.TextCancelButton);
            Check(document.Layers.First(layer => layer.ID == textID).Text!.Style.FontSize == 38, language + ": Cancel restores an existing text layer after live styling");
            Check(_history.UndoCount == beforeCancel && _history.CanRedo == hadRedo, language + ": cancelling live text preserves undo and redo history");
            var beforeColor = _history.UndoCount;
            Click(_optionsBar.TextColorButton); picker = OwnedWindows.OfType<ColorPickerDialog>().Single();
            picker.Sample((.2, .4, 1)); Layout(); Click(picker.Cancel);
            Check(_history.UndoCount == beforeColor && Math.Abs(document.Layers.First(layer => layer.ID == textID).Text!.Style.Red - 1) < .00001,
                language + $": cancelling committed-text color restores pixels without an undo entry ({beforeColor} -> {_history.UndoCount}, red {document.Layers.First(layer => layer.ID == textID).Text!.Style.Red})");
            Click(_optionsBar.TextColorButton); picker = OwnedWindows.OfType<ColorPickerDialog>().Single();
            picker.Sample((.2, .4, 1)); Layout(); Click(picker.Ok);
            Check(_history.UndoCount == beforeColor + 1, language + ": accepting committed-text color creates exactly one undo"); Undo();
            beforeColor = _history.UndoCount;
            Click(_optionsBar.TextColorButton); picker = OwnedWindows.OfType<ColorPickerDialog>().Single(); Click(picker.Ok);
            Check(_history.UndoCount == beforeColor, language + ": accepting unchanged text color does not create an empty edit");
            Click(_optionsBar.TextFontControl); Layout(); Check(_optionsBar.TextFontControl.IsDropDownOpen, language + ": font list opens from the visible control");
            this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Layout();
            Check(!_optionsBar.TextFontControl.IsDropDownOpen && _canvas.IsFocused, language + ": closing font list returns keyboard focus to the canvas");
            foreach (var width in new[] { 880, 1180, 1640 })
            {
                Width = width; Layout();
                foreach (var tool in new[] { Tool.Type, Tool.Marquee, Tool.Ellipse, Tool.Wand, Tool.Lasso, Tool.Polygon })
                {
                    SetTool(tool); Layout();
                    Check(UiLayoutAudit.CheckText(this, $"{language}/{tool}/{width}") > 0, $"{language}: {tool} captions and numeric fields fit at width {width}");
                }
            }
            Width = 1180; SetTool(Tool.Brush); Layout();
            Check(!_optionsBar.Shows("type") && !_optionsBar.Shows("selection"), language + ": controls are hidden outside their tool family");
        }
        return string.Join(Environment.NewLine, report);
    }
}
