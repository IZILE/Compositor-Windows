using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void BrushControlsSelfCheck(string output, List<string> report)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("BRUSH CONTROLS FAILED: " + name); report.Add("PASS: " + name); }
        void Layout() { UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void ClickIn(Control control)
        {
            control.BringIntoView(); Layout(); var root = TopLevel.GetTopLevel(control)!;
            var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
            root.MouseMove(at, RawInputModifiers.None); root.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            root.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Layout();
        }
        void Photograph(Window window, string name)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        var original = _options.Brush; var language = UiText.Language;
        foreach (var locale in new[] { "en", "zh-CN" })
        {
            ChangeLanguage(locale); Width = 1180; Height = 780; Layout();
            foreach (var tool in new[] {Tool.Brush, Tool.Clone, Tool.Heal, Tool.Blur, Tool.Liquify, Tool.Smudge})
            {
                SetTool(tool); _options.PaintOnMask = false; _options.Brush = original with {Diameter = 40, Tip = null}; OptionsChanged(); Layout();
                var scroll = _optionsBar.Overflow;
                Check(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                    $"{locale}: {tool} shows every header control at the default 1180-pixel window width");
                var size = _optionsBar.BrushSize;
                if (!size.SliderExpanded) ClickIn(size.SliderButton!);
                var slider = size.SliderExpanded ? size.Slider! : size.PopupSlider!;
                var root = TopLevel.GetTopLevel(slider)!; root.UpdateLayout();
                var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
                var start = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), root)!.Value;
                root.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                var previous = _canvas.Brush.Diameter;
                for (var step = 1; step <= 12; step++)
                {
                    root.MouseMove(start + new Vector(step * 2, 0), RawInputModifiers.LeftMouseButton); Layout();
                    Check(_canvas.Brush.Diameter >= previous && _canvas.Brush.Diameter == size.Value,
                        $"{locale}: {tool} size drag {step} updates the actual brush continuously");
                    previous = _canvas.Brush.Diameter;
                }
                root.MouseUp(start + new Vector(24, 0), MouseButton.Left, RawInputModifiers.None); Layout();
                Check(previous > 40, $"{locale}: {tool} size slider responds to real routed pointer input");
                size.SliderPopup!.IsOpen = false;
                size.Field.Focus(); size.Field.Text = "123";
                this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
                Check(_canvas.Brush.Diameter == 123 && size.Field.Text == "123",
                    $"{locale}: {tool} keeps exact numeric entry alongside the slider");
                if (tool == Tool.Brush) Photograph(this, "brush-size-default-" + locale);
            }
            Width = 800; SetTool(Tool.Brush); Layout();
            var compact = _optionsBar.BrushSize;
            Check(!compact.SliderExpanded, $"{locale}: a narrow window uses the compact size control");
            ClickIn(compact.SliderButton!);
            Check(compact.SliderPopup!.IsOpen && TopLevel.GetTopLevel(compact.PopupSlider!) is not null,
                $"{locale}: the compact size button opens its working slider");
            compact.SliderPopup.IsOpen = false;
            Width = 1680; Layout();
            Check(compact.SliderExpanded, $"{locale}: a wide window restores the inline size slider");
            var inline = compact.Slider!; var inlineThumb = inline.GetVisualDescendants().OfType<Thumb>().Single();
            var drag = inlineThumb.TranslatePoint(new Point(6, 6), this)!.Value;
            this.MouseDown(drag, MouseButton.Left, RawInputModifiers.LeftMouseButton); Width = 800; Layout();
            Check(compact.SliderExpanded, $"{locale}: resizing during a size drag retains the captured slider");
            this.MouseUp(drag, MouseButton.Left, RawInputModifiers.None); Layout();
            Check(!compact.SliderExpanded, $"{locale}: compact layout resumes after the drag is released");
            Width = 1180; SetTool(Tool.Shape); Layout(); _canvas.Fit();
            foreach (var kind in Enum.GetValues<ShapeKind>())
            {
                ClickIn(_optionsBar.ShapeChoice.ButtonAt((int)kind)); Layout();
                Check(_options.Shape == kind && _canvas.ShapeKind == kind && _shapeKindItems[kind].IsChecked,
                    $"{locale}: the visible {kind} button synchronizes the canvas and Tools menu");
                var count = _document!.Layers.Count;
                var from = _canvas.InView(new SKPoint(100, 120)); var to = _canvas.InView(new SKPoint(320, 190));
                var offset = _canvas.TranslatePoint(new Point(), this)!.Value; from += (Vector)offset; to += (Vector)offset;
                this.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                this.MouseMove(to, RawInputModifiers.LeftMouseButton | RawInputModifiers.Shift); Layout();
                Photograph(this, "shape-" + kind.ToString().ToLowerInvariant() + "-" + locale);
                if (kind == ShapeKind.Ellipse)
                    Check(_canvas.ShapeDraftPixels is { } draft && draft.GetPixel(1, 1).Alpha == 0,
                        $"{locale}: an ellipse reusing the rectangle's bounds has transparent corners in its actual preview");
                this.MouseUp(to, MouseButton.Left, RawInputModifiers.None); Layout();
                Check(_document.Layers.Count == count + 1 && _document.Layers.First(layer => layer.ID == Selected).Shape?.Style.Kind == kind,
                    $"{locale}: a real drag commits an editable {kind} layer");
                if (kind == ShapeKind.Line)
                {
                    var shape = _document.Layers.First(layer => layer.ID == Selected);
                    Check(Math.Abs(shape.Shape!.Style.Start!.Value.Y - shape.Shape.Style.End!.Value.Y) < 0.001,
                        $"{locale}: Shift constrains the line direction instead of turning its box into a square");
                }
                Undo(); Layout(); Check(_document!.Layers.Count == count, $"{locale}: {kind} drawing is one undo step");
            }
            SetShapeKind(ShapeKind.Line); Layout();
            var a = _canvas.TranslatePoint(_canvas.InView(new SKPoint(140, 290)), this)!.Value;
            var b = _canvas.TranslatePoint(_canvas.InView(new SKPoint(340, 140)), this)!.Value;
            this.MouseDown(a, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            this.MouseMove(b, RawInputModifiers.LeftMouseButton); Photograph(this, "shape-negative-line-" + locale);
            var lineDraft = _canvas.ShapeDraftPixels!;
            Check(lineDraft.GetPixel(lineDraft.Width / 4, lineDraft.Height / 4).Alpha == 0
                && lineDraft.GetPixel(lineDraft.Width / 2, lineDraft.Height / 2).Alpha > 0,
                $"{locale}: the line preview uses the actual negative-slope endpoints");
            _options.Brush = _options.Brush with {Red = 1, Green = 0, Blue = 0}; OptionsChanged();
            Photograph(this, "shape-color-change-" + locale); lineDraft = _canvas.ShapeDraftPixels!;
            Check(lineDraft.GetPixel(lineDraft.Width / 2, lineDraft.Height / 2).Red > 240,
                $"{locale}: changing fill during a stationary shape updates its cached preview");
            this.MouseUp(b, MouseButton.Left, RawInputModifiers.None); Layout(); Undo();
        }
        ChangeLanguage("zh-CN");
        var path = Path.Combine(output, "imported-tip.png");
        using (var image = new SKBitmap(8, 8, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            image.Erase(SKColors.Transparent);
            for (var y = 1; y < 7; y++) for (var x = 1; x < 7; x++)
                if (x is 1 or 6 || y is 1 or 6) image.SetPixel(x, y, SKColors.White);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, data.ToArray());
        }
        var libraryPath = Path.Combine(output, "test-brush-library.json");
        var dialog = new BrushLibraryDialog(original, libraryPath); dialog.Show(this); Layout(); dialog.UpdateLayout();
        var import = dialog.ImportFiles([path]); var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!import.IsCompleted && timer.ElapsedMilliseconds < 30000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        if (!import.IsCompleted) throw new InvalidOperationException("The brush import handler did not finish within 30 seconds.");
        import.GetAwaiter().GetResult(); dialog.UpdateLayout(); Layout();
        Check(dialog.ImportedTips.Count == 1 && BrushLibrary.Load(libraryPath).Count == 1,
            "the chosen PNG reaches the actual import handler and persists as one brush");
        Check(UiLayoutAudit.CheckText(dialog, "brush library") > 0, "brush library captions fit the bilingual dialog");
        using (var animation = new UiAnimationClock(dialog.Indicator.Position, dialog.Indicator))
        {
            animation.Pulse(0); animation.Pulse(200); ClickIn(dialog.Rows.Children[0]); animation.Pulse(210); animation.Pulse(410);
            var startY = dialog.Indicator.Position.Y;
            ClickIn(dialog.Rows.Children[2]); animation.Pulse(420); animation.Pulse(460);
            Check(dialog.Indicator.Position.Y > startY && dialog.Indicator.Position.Y < dialog.Indicator.Destination.Y,
                "brush list selection moves continuously between rows");
            var before = dialog.Indicator.Position.Y; ClickIn(dialog.Rows.Children[1]); animation.Pulse(461);
            Check(Math.Abs(dialog.Indicator.Position.Y - before) < 0.001,
                "rapid brush list retargeting starts at the currently displayed selection");
            animation.Pulse(650); ClickIn(dialog.Rows.Children[2]); animation.Pulse(660); animation.Pulse(860);
            Photograph(dialog, "brush-library-zh-CN");
        }
        ClickIn(dialog.UseButton); Check(dialog.Result?.Tip is not null, "Use Brush applies the imported selection");
        ApplyBrushPreset(dialog.Result!); Layout();
        Check(_canvas.Brush.Tip is not null && !_optionsBar.CellsFor("brush").OfType<InlineNumber>().ElementAt(1).IsEnabled,
            "an imported tip reaches painting and keeps its original softness");
        SetTool(Tool.Clone); Check(_canvas.Brush.Tip is null, "clone keeps its original round tip independently of imported paint tips");
        SetTool(Tool.Brush); Check(_canvas.Brush.Tip is not null, "returning to Paint restores the chosen imported tip");
        _options.Brush = original; _options.PaintOnMask = false; ChangeLanguage(language); SetTool(Tool.Brush); OptionsChanged(); Layout();
    }
}
