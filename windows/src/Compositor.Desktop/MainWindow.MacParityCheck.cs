using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void MacParitySelfCheck(string output, List<string> report)
    {
        void Check(bool result, string name)
        {
            if (!result) throw new InvalidOperationException("MAC UI FAILED: " + name);
            report.Add("PASS: " + name);
        }
        void Layout() { ((Control)Content!).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this)
            ?? throw new InvalidOperationException("Detached Mac control.");
        void Click(Control control, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var row = _open.LayerRows.Values.FirstOrDefault(item => item.Card.Children.Contains(control));
            if (row is not null) { _layers.ScrollIntoView(row); Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout(); }
            if (TopLevel.GetTopLevel(control) is null)
            {
                Capture("detached-control.png");
                foreach (var scroll in _layers.GetVisualDescendants().OfType<ScrollViewer>())
                    Console.WriteLine($"Layer scroll: bounds {scroll.Bounds}, extent {scroll.Extent}, viewport {scroll.Viewport}, offset {scroll.Offset}, bars {scroll.VerticalScrollBarVisibility}");
                throw new InvalidOperationException($"Detached control: item index {_layerRowItems.IndexOf(row!)}, rows {_rows.Count}, selected {Selected}, requested {row?.ID}.");
            }
            control.BringIntoView(); Layout(); var point = Middle(control);
            this.MouseDown(point, MouseButton.Left, modifiers | RawInputModifiers.LeftMouseButton);
            this.MouseUp(point, MouseButton.Left, modifiers); Layout();
        }
        void Capture(string name)
        {
            Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            using var frame = this.CaptureRenderedFrame() ?? throw new InvalidOperationException("No Mac parity frame.");
            frame.Save(System.IO.Path.Combine(output, name), new PngBitmapEncoderOptions());
        }
        void Inside(Control control, Control owner, string name)
        {
            if (!control.IsVisible) return;
            var at = control.TranslatePoint(new Point(), owner);
            Check(at is { } p && p.X >= -1 && p.Y >= -1 && p.X + control.Bounds.Width <= owner.Bounds.Width + 1
                && p.Y + control.Bounds.Height <= owner.Bounds.Height + 1, name);
        }

        var document = _document!; var saved = document.Clone(); var initial = Selected;
        var originalBrush = _options.Brush; var erase = _options.Erase; var white = _options.MaskPaintWhite;
        try
        {
            var targetID = document.Layers.Single(layer => layer.Name == "Violet card").ID;
            Reselect(targetID); SetTool(Tool.Brush); Layout();
            var mode = _optionsBar.BrushModeChoice;
            mode.SelectedIndex = 0; _options.Erase = false; RefreshOptionsBar(); Layout();
            var clockType = typeof(Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", true)!;
            var clockProperty = typeof(Avalonia.Animation.Animatable).GetProperty("Clock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var pulse = clockType.GetMethod("Pulse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Frames(Avalonia.Animation.Animatable subject, Action trigger, Func<double> value, string name, int milliseconds)
            {
                var clock = Activator.CreateInstance(clockType, nonPublic: true)!;
                var old = clockProperty.GetValue(subject); clockProperty.SetValue(subject, clock);
                try
                {
                    pulse.Invoke(clock, [TimeSpan.Zero]); trigger(); Layout();
                    var readings = new List<double>();
                    for (var time = 0; time <= milliseconds; time += 10)
                    { pulse.Invoke(clock, [TimeSpan.FromMilliseconds(time)]); Layout(); readings.Add(value()); }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output, name + ".txt"), string.Join(", ", readings));
                    Check(readings.Skip(1).SkipLast(1).Any(item => Math.Abs(item - readings[0]) > 0.001
                        && Math.Abs(item - readings[^1]) > 0.001), name + " has real intermediate animation frames");
                }
                finally { clockProperty.SetValue(subject, old); }
            }
            var transform = (TranslateTransform)mode.SelectionHighlight.RenderTransform!;
            Frames(transform, () => Click(mode.ButtonAt(1)), () => transform.X, "segment-motion", 140);
            Check(_options.Erase, "clicking the moving Erase segment changes the actual brush mode");
            Capture("brush-erase.png");
            var button = mode.ButtonAt(0);
            var feedback = button.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "feedback");
            var pressed = Middle(button);
            Frames(feedback, () => this.MouseDown(pressed, MouseButton.Left, RawInputModifiers.LeftMouseButton),
                () => feedback.Opacity, "button-press-motion", 80);
            Check(feedback.Opacity > 0.15, "button press reaches its tinted state without shifting its bounds");
            Frames(feedback, () => this.MouseUp(pressed, MouseButton.Left, RawInputModifiers.None),
                () => feedback.Opacity, "button-release-motion", 80);

            _history.Reset();
            var opacityBefore = document.Layers.Single(layer => layer.ID == targetID).Opacity;
            var thumb = _opacity.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
            var knob = thumb.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "knob");
            var thumbAt = Middle(thumb);
            Frames(knob, () => this.MouseDown(thumbAt, MouseButton.Left, RawInputModifiers.LeftMouseButton),
                () => ((ISolidColorBrush)knob.Background!).Color.R, "slider-press-motion", 80);
            for (var step = 1; step <= 5; step++)
            { this.MouseMove(new Point(thumbAt.X - step * 4, thumbAt.Y), RawInputModifiers.LeftMouseButton); Layout(); }
            this.MouseUp(new Point(thumbAt.X - 20, thumbAt.Y), MouseButton.Left, RawInputModifiers.None); Layout();
            Check(document.Layers.Single(layer => layer.ID == targetID).Opacity < opacityBefore && _history.UndoCount == 1,
                "five actual slider drag steps create exactly one opacity edit");
            Undo(); Layout(); Check(document.Layers.Single(layer => layer.ID == targetID).Opacity == opacityBefore && !_history.CanUndo,
                "one Undo restores the full opacity drag");

            _options.Brush = _options.Brush with { Diameter = 124 }; OptionsChanged();
            var pointer = new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2);
            var inWindow = _canvas.TranslatePoint(pointer, this)!.Value;
            this.MouseMove(inWindow, RawInputModifiers.None); Layout(); Capture("brush-cursor.png");
            var rendered = _canvas.PictureRenderCount;
            this.MouseMove(new Point(inWindow.X + 12, inWindow.Y + 8), RawInputModifiers.None); Layout();
            Check(_canvas.BrushCursorCircle is { } circle && Math.Abs(circle.Width - 124 * _canvas.Zoom) < 0.001,
                "brush cursor shows the actual diameter at the current zoom");
            Check(_canvas.PictureRenderCount == rendered, "moving the brush cursor does not recompose document pixels");
            SetTool(Tool.Move); Layout(); Check(_canvas.BrushCursorCircle is null, "brush ring disappears immediately when changing tools");
            SetTool(Tool.Ellipse); Check(_rail.Marked == Tool.Ellipse, "alternate rail tools preserve their actual selection");

            Reselect(targetID); AddMask(true); Layout();
            LayerCard Card(Guid id) => _open.LayerRows[id].Card;
            var card = Card(targetID);
            Click(card.MaskButton); SetTool(Tool.Brush); Layout();
            Check(_open.MaskTarget == targetID && _options.PaintOnMask && _optionsBar.Shows("mask"), "mask thumbnail selects the real mask and exposes its paint choices");
            Check(!_footerButtons[2].IsEnabled, "the mask footer button disables when a mask already exists");
            _optionsBar.MaskPaintChoice.SelectedIndex = 0; Layout();
            Check(_options.PaintOnMask && !_options.MaskPaintWhite, "choosing Black keeps the brush on the mask");
            _options.Brush = _options.Brush with { Diameter = 12, Opacity = 1, Hardness = 1 }; OptionsChanged();
            var source = document.Layers.Single(layer => layer.ID == targetID).Asset!.Image;
            Painted([document.Layers.Single(layer => layer.ID == targetID).Transform.Point(0.5, 0.5)]);
            var painted = document.Layers.Single(layer => layer.ID == targetID);
            Check(ReferenceEquals(painted.Asset!.Image, source) && painted.Mask!.Asset.Image.GetPixel(painted.Mask.Asset.Width / 2, painted.Mask.Asset.Height / 2).Red < 10,
                "Black paints the mask and preserves the image pixels");
            Undo(); Reselect(targetID); Click(Card(targetID).MaskButton); Layout();
            Click(Card(targetID).MaskButton, RawInputModifiers.Shift);
            Check(document.Layers.Single(layer => layer.ID == targetID).Mask?.IsEnabled == false && _open.MaskTarget == targetID,
                "Shift-click disables the mask and keeps it selected");
            Click(Card(targetID).MaskButton, RawInputModifiers.Shift);
            Check(document.Layers.Single(layer => layer.ID == targetID).Mask?.IsEnabled == true && _open.MaskTarget == targetID,
                "a second Shift-click enables the mask again");
            Click(Card(targetID).LinkButton);
            Check(document.Layers.Single(layer => layer.ID == targetID).Mask?.IsLinked == false, "link indicator unlinks the mask with an actual click");
            Click(Card(targetID).LinkButton);
            Check(document.Layers.Single(layer => layer.ID == targetID).Mask?.IsLinked == true, "empty link indicator remains clickable to relink the mask");
            Click(Card(targetID).PixelsButton); Check(!_options.PaintOnMask, "clicking the image thumbnail restores the image target");
            var row = _open.LayerRows[targetID]; var rasterCount = Card(targetID).RasterBuilds;
            for (var index = 0; index < 5; index++) Refresh(); Layout();
            Check(ReferenceEquals(row, _open.LayerRows[targetID]) && Card(targetID).RasterBuilds == rasterCount,
                "unchanged layer rows and thumbnails are reused across refreshes");
            Check(Card(targetID).PixelPictureSize == new Size(36, 23), "thumbnail proportions follow the whole 1280 by 800 canvas");
            Click(Card(targetID).PixelsButton, RawInputModifiers.Control);
            Check(document.Selection.Path is { IsEmpty: false }, "Ctrl-click on the image thumbnail loads its actual pixel selection");
            Deselect();

            var clippedID = document.Layers.Single(layer => layer.Name == "Coral circle").ID;
            Reselect(clippedID); ToggleClipping(); Layout();
            Check(Card(clippedID).Details.StartsWith(UiText.Format("Clipped to {0}", "")), "clipped layer row shows its source name");
            Reselect(targetID); GroupSelected(); Layout(); var folderID = Selected!.Value;
            var layerCount = document.Layers.Count; var beforeUndo = _history.UndoName;
            Click(Card(folderID).DisclosureButton);
            Check(_open.CollapsedGroups.Contains(folderID) && !_rows.Contains(targetID) && document.Layers.Count == layerCount
                && _history.UndoName == beforeUndo, "collapsing a folder hides rows without deleting layers or creating an edit");
            Click(Card(folderID).DisclosureButton);
            Check(_rows.Contains(targetID), "expanding a folder restores its child rows");

            for (var depth = 0; depth < 3; depth++) { Reselect(folderID); GroupSelected(); folderID = Selected!.Value; }
            Reselect(targetID); Click(Card(targetID).MaskButton); SetTool(Tool.Brush);
            foreach (var language in new[] { "en", "zh-CN" })
                foreach (var windowWidth in new[] { 800, 1180, 1920 })
                    foreach (var panelWidth in new[] { 202, 252, 352 })
                    {
                        ChangeLanguage(language); Width = windowWidth; Height = windowWidth == 800 ? 520 : 780;
                        _layersSide.Width = panelWidth; Layout();
                        var nested = Card(targetID);
                        foreach (var control in new[] { nested.EyeButton, nested.PixelsButton, nested.LinkButton, nested.MaskButton })
                            Inside(control, nested, $"{language} {windowWidth}/{panelWidth}: nested layer control remains inside its row");
                        var image = nested.PixelsButton.TranslatePoint(new Point(), nested)!.Value;
                        var mask = nested.MaskButton.TranslatePoint(new Point(), nested)!.Value;
                        Check(mask.X >= image.X + nested.PixelsButton.Bounds.Width,
                            $"{language} {windowWidth}/{panelWidth}: image and mask targets do not overlap");
                        foreach (var child in _optionsBar.GetVisualDescendants().OfType<StackPanel>())
                        {
                            if (child.Orientation != Avalonia.Layout.Orientation.Horizontal) continue;
                            var visible = child.Children.OfType<Control>().Where(control => control.IsVisible && control.Bounds.Width > 0).ToArray();
                            for (var index = 1; index < visible.Length; index++)
                                Check(visible[index].Bounds.Left >= visible[index - 1].Bounds.Right - 1,
                                    $"{language} {windowWidth}/{panelWidth}: header siblings do not overlap");
                        }
                        if (windowWidth == 1180 && panelWidth == 252) Capture($"layers-mask-{language}.png");
                    }
            _layersSide.Width = 252; Width = 1920; Height = 1080; Layout(); Capture("brush-wide-zh-CN.png");
            var accentID = document.Layers.Single(layer => layer.Name == "Accent bar").ID;
            Reselect(accentID); _layers.SelectedItems!.Add(_open.LayerRows[clippedID]); Layout();
            var chosen = SelectedLayers.ToHashSet();
            ToggleGroupExpansion(folderID); Layout();
            Check(SelectedLayers.ToHashSet().SetEquals(chosen), "collapsing another folder preserves multiple selected layers");
            ToggleGroupExpansion(folderID); Layout();
            Check(SelectedLayers.ToHashSet().SetEquals(chosen), "expanding another folder preserves multiple selected layers");
            Reselect(targetID); Click(Card(targetID).MaskButton);
            var heldTab = _open;
            var blank = new Tab(); _tabs.Add(blank); Bring(blank); Layout();
            Check(_layerCount.Text == "0" && _layers.ItemCount == 0, "a blank tab clears the layer count and list");
            Check(_footerButtons.Values.All(button => !button.IsEnabled), "blank tabs disable unavailable layer actions");
            Bring(heldTab); Layout(); _tabs.Remove(blank); RefreshTabs();
            Check(_open.MaskTarget == targetID && _options.PaintOnMask, "switching tabs preserves the selected mask without needing another click");
            Reselect(targetID); Click(Card(targetID).MaskButton);
            Check(_options.PaintOnMask && _layerCount.Text == document.Layers.Count.ToString(),
                "returning from a blank tab restores clickable layer targets and the current count");
            Reselect(folderID); UngroupSelected(); Layout();
            Check(document.Layers.All(layer => layer.ID != folderID), "Ungroup removes the folder and preserves its contents");
            Undo(); Layout(); Check(document.Layers.Any(layer => layer.ID == folderID), "Ungroup restores the full folder with one Undo");
            Reselect(targetID); Click(Card(targetID).MaskButton); DeleteLayerOrMask(); Layout();
            Check(document.Layers.Any(layer => layer.ID == targetID) && document.Layers.Single(layer => layer.ID == targetID).Mask is null,
                "Delete on a selected mask removes its mask and retains the layer");
            Click(_footerButtons[2], RawInputModifiers.Alt);
            Check(document.Layers.Single(layer => layer.ID == targetID).Mask?.Asset.Image.GetPixel(0, 0).Red == 0,
                "Alt-click on the mask footer adds a real black mask");
            Undo(); Layout();
        }
        finally
        {
            document.Adopt(saved); _history.Reset(); _open.MaskTarget = null; _open.CollapsedGroups.Clear();
            _options.Brush = originalBrush; _options.Erase = erase; _options.MaskPaintWhite = white;
            _layersSide.Width = 252; Width = 1180; Height = 780; ShowLayers(document); Reselect(initial); OptionsChanged();
        }
    }
}
