using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
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
    private void DesignSelfCheck(string output, List<string> report)
    {
        void Check(bool good, string name) { if (!good) throw new InvalidOperationException("DESIGN FAILED: " + name); report.Add("PASS: " + name); }
        void Layout() { ((Control)Content!).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), this)!.Value;
        void Click(Control control, RawInputModifiers keys = RawInputModifiers.None)
        {
            control.BringIntoView(); Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            var point = Middle(control); this.MouseMove(point, keys); this.MouseDown(point, MouseButton.Left, keys | RawInputModifiers.LeftMouseButton);
            this.MouseUp(point, MouseButton.Left, keys); Layout();
        }
        void Capture(string name)
        {
            Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            using var frame = this.CaptureRenderedFrame()!; frame.Save(System.IO.Path.Combine(output, name), new PngBitmapEncoderOptions());
        }
        Check(WindowDecorations == WindowDecorations.Full, "the editor keeps Windows close, minimize, maximize and native window dragging");
        foreach (var language in new[] { "en", "zh-CN" })
            foreach (var panelWidth in new[] { 202, 252, 352 })
            {
                ChangeLanguage(language); _layersSide.Width = panelWidth; Layout();
                var row = this.GetVisualDescendants().OfType<Grid>().Single(grid => Equals(grid.Tag, "opacity-row"));
                var label = (TextBlock)row.Children[0];
                Check(label.Bounds.Width >= label.TextLayout.Width - 0.5, $"{language}/{panelWidth}: the complete opacity caption fits");
                Check(_opacityReadout.TextAlignment == TextAlignment.Center, $"{language}/{panelWidth}: opacity is centered");
            }
        _layersSide.Width = 252; Width = 1180; Height = 780; Layout();
        SetTool(Tool.Move); Layout();
        var number = _optionsBar.TransformX; var savedNumber = number.Value;
        foreach (var extreme in new[] { -29999.99, 29999.99 })
        {
            number.Value = extreme; number.Field.BringIntoView(); Layout();
            Check(UiLayoutAudit.CheckText(_optionsBar, "fractional transform extremes") > 0,
                "signed fractional transform values fit and stay centered: " + extreme);
        }
        number.Value = savedNumber; Layout();
        var menu = this.GetVisualDescendants().OfType<Menu>().Single();
        var file = menu.Items.OfType<MenuItem>().First();
        var menuFace = file.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
        Check(menuFace.CornerRadius.TopLeft == 6 && menuFace.Transitions?.Count > 0, "top menus have rounded animated backgrounds");
        this.MouseMove(Middle(file), RawInputModifiers.None); Capture("menu-hover-zh-CN.png");
        foreach (var topMenu in menu.Items.OfType<MenuItem>())
        {
            topMenu.IsSubMenuOpen = true; Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            var popup = topMenu.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            Check(popup.Child is Control body && UiLayoutAudit.CheckText(body, "top menu " + topMenu.Header) > 0,
                "opened menu captions and Windows shortcut text fit: " + topMenu.Header);
            topMenu.IsSubMenuOpen = false; Layout();
        }
        var filePopup = file.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
        var popupBody = filePopup.Child!;
        Check(PopupMotion.GetEnabled(filePopup), "app popup opening feedback is enabled");
        using (var popupClock = new UiAnimationClock(popupBody))
        {
            file.IsSubMenuOpen = true; Layout(); popupClock.Pulse(0); Layout();
            var values = new List<double>();
            for (var time = 0; time <= 80; time += 10) { popupClock.Pulse(time); Layout(); values.Add(popupBody.Opacity); }
            Check(values[0] < 0.01 && values[^1] > 0.99 && values.Skip(1).SkipLast(1).Any(value => value is > 0.01 and < 0.99),
                "popup opening contains intermediate opacity frames");
            for (var index = 0; index < 8; index++) { file.IsSubMenuOpen = true; file.IsSubMenuOpen = false; }
            Layout(); popupClock.Pulse(500); Layout();
            Check(!filePopup.IsOpen && Math.Abs(popupBody.Opacity - 1) < 0.001,
                "rapid popup close cancels pending opening callbacks and restores the next opening");
            file.IsSubMenuOpen = true; Layout(); popupClock.Pulse(510); popupClock.Pulse(600); Layout();
            Check(filePopup.IsOpen && Math.Abs(popupBody.Opacity - 1) < 0.001, "reopened popup finishes visible after rapid dismissals");
            file.IsSubMenuOpen = false; Layout();
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "popup-opening-motion.txt"), string.Join(", ", values));
        }
        foreach (var id in _tabStrip.VisibleIDs)
        {
            var pill = (Border)_tabStrip.Pill(id)!;
            Check(pill.CornerRadius.TopLeft == 14 && _tabStrip.Indicator.CornerRadius.TopLeft == 14,
                "project tabs share one rounded moving selection background");
        }
        SetTool(Tool.Pan); Layout();
        var brushButton = _rail.ButtonFor(Tool.Brush)!;
        using (var railClock = new UiAnimationClock(_rail.SelectionPosition))
        {
            void Pulse(int time) { railClock.Pulse(time); Layout(); }
            var start = _rail.SelectionPosition.Y;
            Pulse(0); Click(brushButton); Pulse(0);
            var destination = _rail.SelectionDestination;
            var frames = new List<double>();
            for (var time = 0; time <= 160; time += 10)
            {
                Pulse(time); frames.Add(_rail.SelectionPosition.Y);
                if (time % 40 == 0) Capture($"rail-selection-{time:000}.png");
            }
            Check(Math.Abs(frames[0] - start) < 0.1 && frames.Skip(1).SkipLast(1).Any(value => value > destination && value < start),
                "tool selection travels between buttons through intermediate frames");
            Check(frames.Zip(frames.Skip(1)).All(pair => pair.Second <= pair.First + 0.01), "tool selection advances without jumping back");
            Pulse(170); SetTool(Tool.Pan); Pulse(170); Pulse(200); var before = _rail.SelectionPosition.Y;
            SetTool(Tool.Brush); Pulse(200); var after = _rail.SelectionPosition.Y;
            Check(Math.Abs(after - before) < 0.1, "reversing a tool transition continues from its current position");
            for (var index = 0; index < 8; index++) { Pulse(220 + index * 20); SetTool(index % 2 == 0 ? Tool.Pan : Tool.Brush); Pulse(220 + index * 20); }
            Pulse(700);
            Check(Math.Abs(_rail.SelectionPosition.Y - _rail.SelectionDestination) < 0.1 && _rail.Marked == Tool.Brush && _tool == Tool.Brush,
                "rapid tool switching finishes on the latest choice without a stale animated state");
            System.IO.File.WriteAllText(System.IO.Path.Combine(output, "tool-selection-motion.txt"), string.Join(", ", frames));
        }

        if (_document is { } document)
        {
            var saved = document.Clone(); var previous = Selected; var previousBrush = _options.Brush;
            try
            {
                var id = document.Layers.First(layer => !layer.IsGroup && layer.Adjustment is null).ID;
                Reselect(id); var layer = document.Layers.Single(layer => layer.ID == id);
                if (layer.Mask is not null) DeleteMask();
                document.Selection = DocumentSelection.Rectangular(SKRectI.Create(0, 0, document.Width / 2, document.Height), document.Width, document.Height);
                _history.Reset(); Click(_footerButtons[2]);
                Check(layer.Mask is not null && document.Selection.Path is null && _open.MaskTarget == id, "Add Mask consumes the selection and chooses the new mask");
                Click(_open.LayerRows[id].Card.MaskButton, RawInputModifiers.Alt);
                Check(_open.ViewsMaskAlone && _canvas.MaskAloneLayerID == id && _maskBadge.IsVisible, "Alt-click shows the real mask alone with its return badge");
                Capture("mask-alone-zh-CN.png");
                var held = _open; var empty = new Tab(); _tabs.Add(empty); Bring(empty); Layout();
                Check(!_maskBadge.IsVisible && _canvas.MaskAloneLayerID is null, "empty projects do not retain another project's mask badge");
                Bring(held); _tabs.Remove(empty); RefreshTabs(); Layout();
                Check(_open.ViewsMaskAlone && _canvas.MaskAloneLayerID == id, "returning to a project restores its solo mask view");
                Click(_maskBadgeClose!);
                Check(!_open.ViewsMaskAlone && !_maskBadge.IsVisible && _options.PaintOnMask, "the return badge restores the image and keeps the mask as the paint target");
                Undo(); Layout(); Check(document.Layers.Single(item => item.ID == id).Mask is null && document.Selection.Path is not null,
                    "one Undo restores both the mask and the selection");
                _options.Brush = _options.Brush with { Diameter = 80, Hardness = 0.5 }; OptionsChanged();
                this.MouseMove(Middle(_canvas), RawInputModifiers.None); Layout();
                Check(_canvas.BrushHardnessCircle is { } inner && _canvas.BrushCursorCircle is { } outer && Math.Abs(inner.Width / outer.Width - 0.5) < 0.001,
                    "the dashed hardness ring follows the brush's actual hardness");
            }
            finally
            {
                document.Adopt(saved); _history.Reset(); _open.MaskTarget = null; _open.ViewsMaskAlone = false;
                _options.Brush = previousBrush; OptionsChanged();
                ShowLayers(document); Reselect(previous); SynchronizeLayerTarget();
            }
        }
        ControlMotionChecks.Run(report, output);
        SelectionMotionChecks.Run(report, output);
        MenuMotionChecks.Run(report, output);
        BurstMotionChecks.Run(report);
        ChangeLanguage("zh-CN"); Layout(); Capture("design-unified-zh-CN.png");
        foreach (var message in CanvasView.RasterCacheSelfCheck()) Check(true, message);
        PreviewWorkerSelfCheck(report);
    }
}
