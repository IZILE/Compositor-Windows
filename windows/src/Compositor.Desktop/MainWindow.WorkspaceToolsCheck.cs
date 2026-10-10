using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal string WorkspaceToolsSelfCheck(string sample, string output)
    {
        OpenInput(sample); Show(); UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var report = new List<string>();
        void Check(bool good, string name) { if (!good) throw new InvalidOperationException("WORKSPACE TOOLS: " + name); report.Add("PASS: " + name); }
        void Layout(Window? window = null) { (window ?? this).UpdateLayout(); Dispatcher.UIThread.RunJobs(); (window ?? this).UpdateLayout(); }
        void Wait(Task task)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timer.ElapsedMilliseconds < 30000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            if (!task.IsCompleted) throw new TimeoutException("The workspace operation did not finish.");
            task.GetAwaiter().GetResult(); Layout();
        }
        void Until(Func<bool> done)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!done() && timer.ElapsedMilliseconds < 30000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Check(done(), "asynchronous control operation completes"); Layout();
        }
        Point Center(Control control)
        {
            control.BringIntoView(); Layout();
            return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), TopLevel.GetTopLevel(control)!)!.Value;
        }
        void Click(Control control, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var at = Center(control); var root = TopLevel.GetTopLevel(control)!;
            root.MouseMove(at, modifiers); root.MouseDown(at, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
            root.MouseUp(at, MouseButton.Left, modifiers); Layout();
        }
        void Capture(Window window, string name)
        {
            for (var tick = 0; tick < 14; tick++) { Thread.Sleep(16); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            Layout(window); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        Rect Bounds(Control control) => new(control.TranslatePoint(default, this)!.Value, control.Bounds.Size);
        LayerCard Card(Guid id) => _layerRowItems.Single(row => row.ID == id).Card;
        Point Destination(Guid id, double fraction) { _layers.ScrollIntoView(_layerRowItems.Single(row => row.ID == id)); Layout(); var card = Card(id); return card.TranslatePoint(new Point(card.Bounds.Width - 10, card.Bounds.Height * fraction), this)!.Value; }
        void BeginDrag(Control source, Point end, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var at = Center(source); this.MouseMove(at, modifiers);
            this.MouseDown(at, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
            Check(_layerDragSource is not null, $"layer press captures source at {at}; label bounds {source.Bounds}; layer viewport {_layerDragHost.Bounds}");
            this.MouseMove(new Point(at.X + 8, at.Y + 8), modifiers | RawInputModifiers.LeftMouseButton); Layout();
            this.MouseMove(end, modifiers | RawInputModifiers.LeftMouseButton); Layout();
        }
        void Drop(Control source, Point end, RawInputModifiers modifiers = RawInputModifiers.None)
        { BeginDrag(source, end, modifiers); Check(_layerDragging, "visible layer name starts a captured drag"); this.MouseUp(end, MouseButton.Left, modifiers); Layout(); }
        var document = _document!; Width = 1180; Height = 1000; SetTool(Tool.Pan); Layout();
        var a = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 64, 64), "A");
        var b = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 64, 64), "B");
        var c = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 64, 64), "C");
        var folder = new ImageLayer(Guid.NewGuid(), null, new(0, 0, 64, 64), "Folder") { IsGroup = true };
        document.Layers.AddRange(new[] { a, b, c, folder }); ShowLayers(document); Layout();
        var undo = _history.UndoCount;
        Drop(Card(a.ID).NameLabel, Destination(c.ID, .1));
        Check(document.Layers.FindIndex(layer => layer.ID == a.ID) > document.Layers.FindIndex(layer => layer.ID == c.ID)
            && _history.UndoCount == undo + 1, "name drag reorders a layer as one undo step"); Undo(); Layout();
        Drop(Card(c.ID).NameLabel, Destination(folder.ID, .5));
        Check(document.Layers.Single(layer => layer.ID == c.ID).ParentID == folder.ID, "dropping on a folder reparents the layer");
        var count = document.Layers.Count;
        Drop(Card(folder.ID).NameLabel, Destination(b.ID, .1), RawInputModifiers.Control);
        Check(document.Layers.Count == count + 2 && Selected is { } copiedFolder
            && document.Layers.Single(layer => layer.ID == copiedFolder).IsGroup
            && document.Layers.Count(layer => layer.ParentID == copiedFolder) == 1, "Ctrl-drag duplicates a complete folder tree");
        Undo(); Layout();
        Click(Card(a.ID).NameLabel); Click(Card(b.ID).NameLabel, RawInputModifiers.Control);
        Check(SelectedLayers.ToHashSet().SetEquals(new[] { a.ID, b.ID }), "Ctrl-click on names preserves Windows multiselection");
        Drop(Card(a.ID).NameLabel, Destination(folder.ID, .1));
        Check(SelectedLayers.Count == 2, "drag preserves the selected batch after reordering"); Undo(); Layout();
        LayerMaskEdits.Add(document, a.ID, true); ShowLayers(document); Layout(); undo = _history.UndoCount;
        Drop(Card(a.ID).MaskButton, Destination(b.ID, .5), RawInputModifiers.Alt);
        var masked = document.Layers.Single(layer => layer.ID == b.ID);
        Check(masked.Mask is not null && _open.MaskTarget == b.ID && _history.UndoCount == undo + 1,
            "Alt-drag copies the mask and selects the new mask target");
        Check(!ReferenceEquals(masked.Mask, document.Layers.Single(layer => layer.ID == a.ID).Mask), "copied masks keep independent metadata");
        Undo(); Layout(); Check(document.Layers.Single(layer => layer.ID == b.ID).Mask is null, "undo restores the target before mask copy");
        BeginDrag(Card(a.ID).NameLabel, Destination(folder.ID, .5));
        Check(_layerDropMarker.IsVisible, "a valid destination shows one moving drop indicator");
        this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Layout();
        Check(!_layerDragging && !_layerDropMarker.IsVisible && !_layerDragBadge.IsVisible, "Escape removes the drag overlay without applying a move");
        this.MouseUp(new Point(20, 20), MouseButton.Left, RawInputModifiers.None);
        BeginDrag(Card(a.ID).NameLabel, Destination(folder.ID, .5)); var originalTab = _open;
        var otherTab = new Tab { Document = LayerPlacement.NewDocument(32, 32) }; _tabs.Add(otherTab); Bring(otherTab); Layout();
        Check(!_layerDragging && !_layerDropMarker.IsVisible, "switching documents cancels a captured layer drag");
        this.MouseUp(new Point(20, 20), MouseButton.Left, RawInputModifiers.None); Bring(originalTab); _tabs.Remove(otherTab); otherTab.Document!.Dispose(); Layout();
        Capture(this, "layer-workflows");

        using var typeface = SKTypeface.FromFamilyName("Arial"); using var fontStream = typeface.OpenStream(); using var fontData = SKData.Create(fontStream);
        var fontPath = Path.Combine(output, "Font-fixture.ttf"); File.WriteAllBytes(fontPath, fontData.ToArray());
        foreach (var locale in new[] { "en", "zh-CN" })
        {
            var beforeText = _history.UndoCount;
            ChangeLanguage(locale); SetTool(Tool.Type); TypeHere(new SKPoint(60, 600)); _canvas.Focus(); this.KeyTextInput("Imported font 字体"); Layout();
            BeginTextStyle();
            ChangeTextStyle(style => { style.FontSize = 28; style.Alignment = Compositor.Core.Format.TextAlignment.Left; style.Red = style.Green = style.Blue = 1; });
            EndTextStyle(); Layout();
            _fontImportPicker = () => Task.FromResult(new[] { fontPath });
            Click(_optionsBar.ImportFontButton); Until(() => !_importingFonts);
            var name = _text!.Style.FontName;
            Check(FontLibrary.Current.Entries.Any(font => font.Name == name) && FontLibrary.Current.Resolve(name) is not null,
                locale + ": the visible import button loads and previews the selected app-local font");
            Check(_optionsBar.TextFontControl.SelectedItem as string == name && _canvas.IsFocused, locale + ": font control and canvas follow the import without losing typing focus");
            var textID = _text.LayerID!.Value; Click(_optionsBar.TextDoneButton); Layout();
            var beforeFont = _history.UndoCount; var beforeName = document.Layers.Single(layer => layer.ID == textID).Text!.Style.FontName;
            _fontImportPicker = () => Task.FromResult(Array.Empty<string>()); Click(_optionsBar.ImportFontButton); Until(() => !_importingFonts);
            Check(_history.UndoCount == beforeFont && document.Layers.Single(layer => layer.ID == textID).Text!.Style.FontName == beforeName, locale + ": canceled font import changes neither text nor history");
            var reopened = new FontLibrary(UserData.File("fonts")); Check(reopened.Resolve(name) is not null, locale + ": imported font reloads from the persistent catalog");
            Capture(this, "font-import-" + locale);
            while (_history.UndoCount > beforeText) Undo();
            SetTool(Tool.Pan); Layout();
            foreach (var width in new[] { 880, 1180, 1640 })
            {
                Width = width; Layout();
                foreach (var tool in Enum.GetValues<Tool>())
                {
                    SetTool(tool); Layout();
                    Check(UiLayoutAudit.CheckText(this, locale + "/" + tool + "/" + width) > 0, $"{locale}/{width}/{tool}: visible captions and numbers fit");
                    var first = _optionsBar.Showing.SelectMany(_optionsBar.CellsFor).Where(cell => cell.IsVisible).FirstOrDefault();
                    if (first is not null && tool != Tool.Move)
                    {
                        var at = Bounds(first).X + _optionsBar.Overflow.Offset.X;
                        var title = _optionsBar.TitleLabel;
                        var gap = at - Bounds(title).Right;
                        Check(Math.Abs(gap - 8) < 1.1, $"{locale}/{width}/{tool}: first toolbar control stays close to its own title");
                        if (tool != Tool.Brush)
                        {
                            var natural = CaptionWidth.Maximum([title.Text ?? ""], title.FontFamily, title.FontStyle,
                                title.FontWeight, title.FontStretch, title.FontSize);
                            Check(Math.Abs(title.Bounds.Width - natural) < 1.1, $"{locale}/{width}/{tool}: title uses its natural width without padding for unrelated tools");
                        }
                    }
                    foreach (var cell in _optionsBar.Showing.SelectMany(_optionsBar.CellsFor).Where(cell => cell.IsVisible))
                    {
                        var bounds = Bounds(cell); var bar = Bounds(_optionsBar);
                        Check(Math.Abs(bounds.Center.Y - bar.Center.Y) < 1.1, $"{locale}/{width}/{tool}: tool options share a vertical center");
                    }
                }
            }
            Width = 1180; SetTool(Tool.Marquee); Layout(); Capture(this, "marquee-toolbar-" + locale);
            SetTool(Tool.Lasso); Layout(); Capture(this, "lasso-toolbar-" + locale);
            SetTool(Tool.Gradient); Layout();
            var preview = _optionsBar.GradientPreview; var button = _optionsBar.GradientsButton;
            Check(Bounds(preview) == Bounds(button) && preview.CornerRadius == button.CornerRadius.TopLeft,
                locale + ": gradient fills its button and shares the exact corner radius");
            Capture(this, "gradient-toolbar-" + locale);
        }
        _fontImportPicker = null;

        var alpha = new byte[32 * 24];
        for (var y = 0; y < 24; y++) for (var x = 0; x < 32; x++)
            if (Math.Pow((x - 15.5) / 15, 2) + Math.Pow((y - 11.5) / 11, 2) < 1) alpha[y * 32 + x] = 255;
        _options.Brush = _options.Brush with { Diameter = 124, Tip = new BrushTip("Oval fixture", 32, 24, alpha) };
        SetTool(Tool.Brush); Layout(); var pointer = _canvas.TranslatePoint(new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2), this)!.Value;
        this.MouseMove(pointer, RawInputModifiers.None); Layout();
        Check(_canvas.BrushCursorOutline is not null && _canvas.BrushCursorCircle is { } rect && Math.Abs(rect.Width / rect.Height - 32d / 24) < .001,
            "sampled brush cursor follows its actual aspect and alpha contour");
        var contourBuilds = _canvas.BrushContourBuilds; var renders = _canvas.PictureRenderCount;
        for (var i = 0; i < 100; i++) this.MouseMove(pointer + new Vector(i % 20, i % 13), RawInputModifiers.None);
        Check(_canvas.BrushContourBuilds == contourBuilds && _canvas.PictureRenderCount == renders, "100 cursor moves reuse the contour without recompositing the document");
        Capture(this, "brush-contour"); SetErasing(true); Layout();
        Check(_canvas.BrushCursorOutline is not null, "eraser keeps the same sampled tip contour");
        foreach (var tool in new[] { Tool.Clone, Tool.Blur, Tool.Liquify, Tool.Smudge, Tool.Heal })
        {
            SetTool(tool); this.MouseMove(pointer, RawInputModifiers.None); Layout();
            Check(_canvas.BrushCursorOutline is null && _canvas.BrushCursorCircle is not null, tool + ": round working kernel has a round cursor");
        }
        SetTool(Tool.Shape); Layout(); Check(_canvas.BrushCursorCircle is null, "leaving brush tools removes the brush contour immediately");

        foreach (var locale in new[] { "en", "zh-CN" })
        foreach (var format in new[] { ImageExportFormat.Png, ImageExportFormat.Jpeg })
        {
            ChangeLanguage(locale); var task = ImageExportDialog.Ask(this, document, format);
            var dialog = OwnedWindows.OfType<ImageExportDialog>().Single(); Layout(dialog); Wait(dialog.WhenReady);
            Check(dialog.ExportButton.IsEnabled && dialog.Prepared is not null, locale + "/" + format + ": encoding finishes in the background");
            Check(UiLayoutAudit.CheckText(dialog, locale + "/export/" + format) > 0, locale + "/" + format + ": export fields and captions fit");
            var firstSize = dialog.Prepared!.Length;
            if (format == ImageExportFormat.Jpeg)
            {
                Click(dialog.QualityControl.Field); dialog.QualityControl.Field.Text = "20"; dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Wait(dialog.WhenReady);
                Check(dialog.Prepared!.Length < firstSize, locale + ": lowering JPEG quality reduces the actual file size");
                Click(dialog.ViewControl.ButtonAt(0)); Click(dialog.ViewControl.ButtonAt(1));
            }
            else
            {
                dialog.CompressionControl.SelectedIndex = 0; dialog.CompressionControl.SelectedIndex = 2; Wait(dialog.WhenReady);
                Check(dialog.Prepared!.Settings.Compression == PngCompression.Smallest, locale + ": rapid compression changes keep only the latest PNG result");
            }
            Check(dialog.SizeText == ImageExportDialog.FormatBytes(dialog.Prepared!.Length), locale + "/" + format + ": byte count describes the prepared export file");
            Capture(dialog, "export-" + format + "-" + locale); Click(dialog.ExportButton); Wait(task);
            using var result = task.Result!; var destination = Path.Combine(output, "export-" + format + "-" + locale + (format == ImageExportFormat.Png ? ".png" : ".jpg"));
            Wait(result.SaveTo(destination)); Check(new FileInfo(destination).Length == result.Length, locale + "/" + format + ": export reuses the previewed file without recompression");
            using var decoded = SKBitmap.Decode(destination); Check(decoded.Width == document.Width && decoded.Height == document.Height, locale + "/" + format + ": compression preserves image dimensions");
        }
        var cancelTask = ImageExportDialog.Ask(this, document, ImageExportFormat.Png);
        var cancelDialog = OwnedWindows.OfType<ImageExportDialog>().Single(); Layout(cancelDialog); Click(cancelDialog.CancelButton); Wait(cancelTask);
        Check(cancelTask.Result is null, "cancel during background encoding publishes no image");
        for (var cycle = 0; cycle < 12; cycle++)
        {
            var format = cycle % 2 == 0 ? ImageExportFormat.Png : ImageExportFormat.Jpeg;
            var task = ImageExportDialog.Ask(this, document, format);
            var dialog = OwnedWindows.OfType<ImageExportDialog>().Single(); Layout(dialog);
            string? preparedPath = null;
            if (cycle % 3 == 0) { Wait(dialog.WhenReady); preparedPath = dialog.Prepared!.Path; }
            for (var change = 0; change < 24; change++)
            {
                if (format == ImageExportFormat.Png) dialog.CompressionControl.SelectedIndex = change % 3;
                else dialog.QualityControl.Slider!.Value = 20 + change;
            }
            // Exercise both the Cancel action and the native close path with pending work.
            if (cycle % 2 == 0) { var closing = dialog.CancelExport(); Wait(dialog.CancelExport()); Wait(closing); }
            else dialog.Close();
            Wait(task);
            Check(task.Result is null && !OwnedWindows.OfType<ImageExportDialog>().Any(),
                $"export stress {cycle + 1}: rapid setting changes and repeated close leave no live dialog or result");
            Check(preparedPath is null || !File.Exists(preparedPath),
                $"export stress {cycle + 1}: canceled prepared file is removed");
        }
        return string.Join(Environment.NewLine, report);
    }
}
