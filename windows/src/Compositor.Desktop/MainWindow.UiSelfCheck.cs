using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal string UiSelfCheck(string sample, string output)
    {
        var report = new List<string>();
        void Check(bool good, string name)
        {
            if (!good) throw new InvalidOperationException("UI FAILED: " + name);
            report.Add("PASS: " + name);
        }
        void Layout() { ((Control)Content!).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Inside(Control control, Control container, string name)
        {
            var at = control.TranslatePoint(new Point(), container);
            Check(at is { } point && point.X >= -1 && point.Y >= -1
                && point.X + control.Bounds.Width <= container.Bounds.Width + 1
                && point.Y + control.Bounds.Height <= container.Bounds.Height + 1, name);
        }
        void Photograph(string name)
        {
            this.MouseMove(new Point(Width / 2, Height - 12), RawInputModifiers.None);
            Layout();
            if (_cameraRaw is not null) ShowPreviewOnce(null, EventArgs.Empty);
            using var frame = this.CaptureRenderedFrame() ?? throw new InvalidOperationException("No UI frame.");
            frame.Save(System.IO.Path.Combine(output, name), new PngBitmapEncoderOptions());
        }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2,
            control.Bounds.Height / 2), this) ?? throw new InvalidOperationException("Detached control.");
        void CheckTabSpacing(string name)
        {
            var add = _newTabButton!;
            var addAt = add.TranslatePoint(new Point(), this)!.Value;
            var stripAt = _tabStrip.TranslatePoint(new Point(), this)!.Value;
            Check(stripAt.X >= addAt.X + add.Bounds.Width + 7.9, name + ": New button and tabs have an eight-point gap");
            Check(Math.Abs(addAt.Y + add.Bounds.Height / 2 - stripAt.Y - _tabStrip.Bounds.Height / 2) < 0.5,
                name + ": New button and tab capsules align vertically");
            foreach (var id in _tabStrip.VisibleIDs)
            {
                var pill = _tabStrip.Pill(id)!; var at = pill.TranslatePoint(new Point(), this)!.Value;
                Check(at.X >= stripAt.X - 0.5 && at.X + pill.Bounds.Width <= stripAt.X + _tabStrip.Bounds.Width + 0.5,
                    name + ": visible tab stays within its reserved space");
            }
        }

        Show();
        OpenInput(sample);
        Layout();
        if (Environment.GetEnvironmentVariable("COMPOSITOR_MAC_QA_ONLY") == "1")
        {
            Width = 1920; Height = 780; Layout(); _canvas.Fit();
            MacParitySelfCheck(output, report);
            return string.Join(Environment.NewLine, report);
        }
        foreach (var language in new[] { "en", "zh-CN" })
        {
            ChangeLanguage(language);
            foreach (var (width, height) in new[] { (800, 520), (1180, 780), (1920, 1080) })
            {
                Width = width; Height = height; Layout();
                CheckTabSpacing($"{language} {width}");
                foreach (var tool in Enum.GetValues<Tool>())
                {
                    SetTool(tool); Layout();
                    var textChecks = UiLayoutAudit.CheckText(this, $"{language} {width} {tool}");
                    Check(textChecks > 0, $"{language} {width}: {tool} visible captions and numeric fields are complete and centered");
                    Check(_optionsBar.Bounds.Height == 42, $"{language} {width}: {tool} preserves header height");
                    foreach (var control in _optionsBar.GetVisualDescendants().Where(value => value is Button or ComboBox or CheckBox).Cast<Control>())
                    {
                        if (!control.IsVisible || control.GetVisualAncestors().OfType<Control>().Any(parent => !parent.IsVisible)) continue;
                        var cellPosition = control.TranslatePoint(new Point(), _optionsBar);
                        Check(cellPosition is { } point && point.Y >= -1 && point.Y + control.Bounds.Height <= 43,
                            $"{language} {width}: {tool} control fits vertically");
                        if (control.Parent is StackPanel && control is not TextBlock)
                            Check(cellPosition is { } middle && Math.Abs(middle.Y + control.Bounds.Height / 2 - 21) < 1,
                                $"{language} {width}: {tool} option shares the header center");
                    }
                    var scroll = _optionsBar.Overflow;
                    scroll.Offset = new Vector(Math.Max(0, scroll.Extent.Width - scroll.Viewport.Width), 0);
                    Layout();
                    Check(scroll.Extent.Width <= scroll.Viewport.Width + scroll.Offset.X + 1,
                        $"{language} {width}: {tool} last option remains reachable");
                    scroll.Offset = new Vector();
                }
                Inside(_opacityReadout, _layersSide, $"{language} {width}: opacity value stays inside panel");
                foreach (var button in _toolbar.Values) Inside(button, this, $"{language} {width}: view button stays in window");
            }
            Width = 800; Height = 520; Layout();
            SetTool(Tool.Brush);
            var originalBrush = _options.Brush;
            _optionsBar.BrushSize.Field.BringIntoView(); Layout();
            var sizeField = _optionsBar.BrushSize.Field;
            sizeField.Focus(); sizeField.Text = "64";
            this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
            Check(_canvas.Brush.Diameter == 64 && _options.Brush.Diameter == 64,
                $"{language}: brush size is editable directly in the header");
            sizeField.Focus();
            this.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null); Layout();
            Check(_canvas.Brush.Diameter == 65, $"{language}: number arrow steps update the actual brush");
            var smoothing = _optionsBar.BrushSmoothing.Slider!;
            smoothing.BringIntoView(); Layout();
            Click(smoothing.TranslatePoint(new Point(smoothing.Bounds.Width * 0.5, smoothing.Bounds.Height / 2), this)!.Value); Layout();
            Check(_canvas.Brush.Smoothing is > 0.4 and < 0.6,
                $"{language}: smoothing slider sets the brush's normalized smoothing");
            _options.Brush = originalBrush; OptionsChanged();
            _optionsBar.Overflow.Offset = new Vector(); Layout();
            Photograph($"brush-narrow-{language}.png");
            Reselect(_document!.Layers.First(layer => layer.Asset is not null && !layer.IsGroup).ID);
            CameraRawFilter(); Layout();
            using (var initialFrame = this.CaptureRenderedFrame()) initialFrame?.Save(System.IO.Path.Combine(output, $"raw-initial-{language}.png"), new PngBitmapEncoderOptions());
            var raw = _cameraRaw!;
            Check(UiLayoutAudit.CheckText(_cameraRawHost, language + " Camera Raw") > 0, language + ": Camera Raw captions and numeric fields are complete and centered");
            Check(raw.Sections.Count == 10 && raw.Sections["Light"].Expanded && raw.Sections["Color"].Expanded
                && !raw.Sections["Effects"].Expanded, $"{language}: original Camera Raw expansion state");
            var exposure = raw.Amount("Exposure, stops")!;
            Check(exposure.Bounds.Width > 80, $"{language}: exposure slider has usable width");
            Inside(exposure, raw.Parameters, $"{language}: exposure slider stays in the visible scroll viewport");
            var before = exposure.Value;
            var at = exposure.TranslatePoint(new Point(exposure.Bounds.Width * 0.9, exposure.Bounds.Height / 2), this)!.Value;
            this.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            var downValue = exposure.Value;
            this.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Layout();
            var hit = this.GetVisualAt(at);
            Check(exposure.Value > before + 1, $"{language}: visible slider responds to a mouse click ({before} to {downValue} to {exposure.Value}, at {at}, bounds {exposure.Bounds}, hit {string.Join("/", hit is null ? [] : hit.GetVisualAncestors().Prepend(hit).Select(visual => visual.GetType().Name))})");
            var field = ((Grid)exposure.Parent!).Children.OfType<TextBox>().Single();
            field.Focus(); field.Text = "2";
            this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
            Check(Math.Abs(exposure.Value - 2) < 0.001, $"{language}: numeric entry updates the preview slider");
            ShowPreviewOnce(null, EventArgs.Empty);
            Click(Middle(raw.PreviewControl)); Layout();
            Check(!raw.PreviewEnabled && _canvas.PreviewDocument is null, $"{language}: Preview click shows the unmodified document");
            raw.Move("Exposure, stops", 1); ShowPreviewOnce(null, EventArgs.Empty);
            Check(_canvas.PreviewDocument is null, $"{language}: moving amounts keeps a disabled preview off");
            Click(Middle(raw.PreviewControl)); Layout(); ShowPreviewOnce(null, EventArgs.Empty);
            Check(raw.PreviewEnabled && _canvas.PreviewDocument is not null, $"{language}: Preview click restores the edited picture");
            Photograph($"camera-raw-narrow-{language}.png");
            Width = 1180; Height = 780; Layout();
            Photograph($"camera-raw-{language}.png");
            using (var sectionClock = new UiAnimationClock(raw.Sections.Values.Select(section => (Avalonia.Animation.Animatable)section.BodyHost).ToArray()))
            {
                foreach (var section in raw.Sections.Values) section.Expanded = false;
                sectionClock.Pulse(0); Layout(); sectionClock.Pulse(200); Layout();
            }
            Layout();
            Click(Middle(raw.Sections["Effects"].Header)); Layout();
            Check(raw.Sections["Effects"].Expanded, $"{language}: group opens with a mouse click");
            Click(Middle(raw.Sections["Effects"].Header)); Layout();
            Check(!raw.Sections["Effects"].Expanded, $"{language}: group closes with a mouse click");
            var animatedSection = raw.Sections["Effects"];
            using (var disclosureClock = new UiAnimationClock(animatedSection.BodyHost, (Avalonia.Animation.Animatable)animatedSection.Disclosure.RenderTransform!))
            {
                disclosureClock.Pulse(0); animatedSection.Expanded = true; disclosureClock.Pulse(0); Layout();
                var heights = new List<double>();
                for (var time = 0; time <= 140; time += 10)
                { disclosureClock.Pulse(time); Layout(); heights.Add(animatedSection.BodyHost.Height); }
                Check(heights.Skip(1).SkipLast(1).Any(height => height > heights[0] + 0.1 && height < heights[^1] - 0.1),
                    language + ": disclosure expansion has intermediate height frames");
                Check(heights.All(double.IsFinite) && heights.Zip(heights.Skip(1)).All(pair => pair.Second + 0.1 >= pair.First),
                    language + ": disclosure expansion progresses without a size flashback");
                disclosureClock.Pulse(150); animatedSection.Expanded = false; disclosureClock.Pulse(150); Layout();
                disclosureClock.Pulse(180); Layout();
                var heldHeight = animatedSection.BodyHost.Height; animatedSection.Expanded = true; disclosureClock.Pulse(180); Layout();
                Check(Math.Abs(heldHeight - animatedSection.BodyHost.Height) < 0.1,
                    language + $": reversing disclosure animation preserves its current height ({heldHeight} to {animatedSection.BodyHost.Height}; expansion {string.Join(", ", heights)})");
                for (var index = 0; index < 8; index++)
                {
                    var time = 200 + index * 20;
                    disclosureClock.Pulse(time); animatedSection.Expanded = index % 2 == 0;
                    disclosureClock.Pulse(time); Layout();
                }
                disclosureClock.Pulse(600); Layout();
                Check(!animatedSection.Expanded && animatedSection.BodyHost.Height < 0.1 && !animatedSection.BodyHost.IsHitTestVisible,
                    language + $": rapid disclosure clicks end closed without hidden controls receiving input ({animatedSection.Expanded}, {animatedSection.BodyHost.Height}, {animatedSection.BodyHost.IsHitTestVisible})");
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "disclosure-motion-" + language + ".txt"), string.Join(", ", heights));
            }
            raw.Move("Grading blending", 12);
            raw.Move("Sharpen radius", 50);
            raw.Move("Process version", 1);
            raw.Reset();
            Check(raw.Current().IsIdentity && raw.Current().SharpenRadius == 10 && raw.Current().GradeBlending == 50
                && raw.Current().ProcessVersion == 6, $"{language}: Camera Raw Reset restores nonzero defaults");
            CloseCameraRaw(); Layout();
        }

        Width = 1180; Height = 780; Layout();
        Check(Icon is not null, "main window uses the original application icon");
        SetTool(Tool.Move);
        var transformTarget = _document!.Layers.First(layer => layer.Name == "Violet card");
        var transformID = transformTarget.ID;
        var originalTransform = transformTarget.Transform;
        Reselect(transformID); Layout();
        _opacityReadout.Focus(); _opacityReadout.Text = "37";
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Opacity == 0.37,
            "layer opacity can be entered directly as a percentage");
        Undo(); Reselect(transformID); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Opacity == 1,
            "numeric layer opacity undoes in one step");
        var xField = _optionsBar.TransformX.Field;
        xField.BringIntoView(); Layout();
        xField.Focus(); xField.Text = (originalTransform.X + 12).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform.X == originalTransform.X + 12,
            "inline X changes the actual layer without a dialog");
        Undo(); Reselect(transformID); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform == originalTransform,
            "a completed numeric transform undoes in one step");
        _optionsBar.RatioButton.IsChecked = false;
        _optionsBar.RatioButton.BringIntoView(); Layout(); Click(Middle(_optionsBar.RatioButton)); Layout();
        var widthField = _optionsBar.TransformWidth.Field;
        widthField.Focus(); widthField.Text = (originalTransform.Width * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform.Height == originalTransform.Height * 2,
            "aspect ratio lock scales both dimensions");
        Undo(); Reselect(transformID); Layout();
        // A controlled clock makes intermediate-frame checks independent of the machine's current load.
        // These members belong to the pinned Avalonia 12.1.3 implementation and are used only in this diagnostic.
        var clockType = typeof(Avalonia.Animation.Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
        var clock = Activator.CreateInstance(clockType, nonPublic: true)!;
        var pulse = clockType.GetMethod("Pulse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var clockProperty = typeof(Avalonia.Animation.Animatable).GetProperty("Clock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previousClock = clockProperty.GetValue(_optionsBar.PendingTransformActions);
        clockProperty.SetValue(_optionsBar.PendingTransformActions, clock);
        pulse.Invoke(clock, [TimeSpan.Zero]);
        _canvas.Focus(); this.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, "t"); Layout();
        Check(_transformPreview is not null && _optionsBar.PendingTransformActions.IsHitTestVisible,
            "Ctrl+T starts an editable transform preview");
        var motion = new List<double>();
        for (var frame = 0; frame <= 12; frame++)
        {
            pulse.Invoke(clock, [TimeSpan.FromMilliseconds(frame * 10)]);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            motion.Add(_optionsBar.PendingTransformActions.Opacity);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "transform-opacity-frames.txt"), string.Join(", ", motion));
        Check(motion.Any(value => value is > 0 and < 1) && motion[^1] > 0.99,
            "transform action buttons pass through intermediate opacity before completing their fade");
        clockProperty.SetValue(_optionsBar.PendingTransformActions, previousClock);
        xField.Focus(); xField.Text = (originalTransform.X + 25).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform == originalTransform
            && _canvas.PreviewDocument!.Layers.First(layer => layer.ID == transformID).Transform.X == originalTransform.X + 25,
            "pending transform shows a copy while the actual document stays unchanged");
        _canvas.Focus(); this.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Layout();
        Check(_transformPreview is null && _document.Layers.First(layer => layer.ID == transformID).Transform == originalTransform,
            "Escape cancels the pending transform completely");
        _canvas.Focus(); this.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, "t"); Layout();
        xField.Focus(); xField.Text = (originalTransform.X + 25).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        _optionsBar.ApplyTransformButton.BringIntoView(); Layout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
        Photograph("transform-pending-zh-CN.png");
        Click(Middle(_optionsBar.ApplyTransformButton)); Layout();
        Check(_transformPreview is null && _document.Layers.First(layer => layer.ID == transformID).Transform.X == originalTransform.X + 25,
            "Apply button commits the pending transform");
        Undo(); Reselect(transformID); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform == originalTransform,
            "Apply creates exactly one undoable transform");
        var precise = originalTransform with { X = originalTransform.X + 0.123456 };
        _document.Layers.First(layer => layer.ID == transformID).Transform = precise;
        ShowTransformBox(); ShowTransformInspector();
        xField.Focus(); _canvas.Focus(); Layout();
        Check(_document.Layers.First(layer => layer.ID == transformID).Transform == precise,
            "focusing and leaving a rounded numeric field does not alter precise coordinates");
        _document.Layers.First(layer => layer.ID == transformID).Transform = originalTransform;
        ShowTransformBox(); ShowTransformInspector();
        BeginPersistentTransform(); Layout();
        xField.Focus(); xField.Text = (originalTransform.X + 9).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"); Layout();
        var coral = _document.Layers.First(layer => layer.Name == "Coral circle");
        var coralTransform = coral.Transform;
        Reselect(coral.ID); Layout();
        Check(_transformPreview is null && _document.Layers.First(layer => layer.ID == transformID).Transform.X == originalTransform.X + 9
            && _document.Layers.First(layer => layer.ID == coral.ID).Transform == coralTransform,
            "changing layer selection applies the preview to its original targets");
        Undo(); Reselect(transformID); Layout(); _canvas.Fit();
        _optionsBar.AutoSelectControl.IsChecked = false;
        Click(Middle(_optionsBar.AutoSelectControl)); Layout();
        var coralOnCanvas = _canvas.TranslatePoint(_canvas.InView(coralTransform.Point(0.5, 0.5)), this)!.Value;
        Click(coralOnCanvas); Layout();
        Check(Selected == coral.ID, "Auto Select click picks the foreground layer at the canvas point");
        _optionsBar.AutoSelectControl.IsChecked = false;
        Reselect(transformID); Layout();
        _optionsBar.ShowControlsControl.IsChecked = true;
        Click(Middle(_optionsBar.ShowControlsControl)); Layout();
        var savedSnapping = _snappingOn; _snappingOn = false;
        var outside = _canvas.TranslatePoint(_canvas.InView(new SkiaSharp.SKPoint(10, 10)), this)!.Value;
        this.MouseDown(outside, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        this.MouseMove(new Point(outside.X + 20, outside.Y), RawInputModifiers.LeftMouseButton);
        this.MouseUp(new Point(outside.X + 20, outside.Y), MouseButton.Left, RawInputModifiers.None); Layout();
        Check(!_transformShown && _document.Layers.First(layer => layer.ID == transformID).Transform.X > originalTransform.X + 10,
            "hiding transform handles still lets an outside press move the active layer");
        _snappingOn = savedSnapping; Undo(); Reselect(transformID); Layout();
        Click(Middle(_optionsBar.ShowControlsControl)); Layout();
        _optionsBar.Overflow.Offset = new Vector(); Layout();
        _canvas.Fit(); Layout();
        SetTool(Tool.Zoom);
        var zoomPoint = new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2);
        var anchored = _canvas.InDocument(zoomPoint);
        var onCanvas = _canvas.TranslatePoint(zoomPoint, this)!.Value;
        var initialZoom = _canvas.Zoom;
        Click(onCanvas); Layout();
        Check(Math.Abs(_canvas.Zoom / initialZoom - 2) < 0.001, "Zoom tool click doubles the view");
        Check(_statusInfo.Text?.StartsWith($"{_canvas.Zoom * 100:0}%") == true, "the status zoom follows the actual viewport immediately");
        Check(Math.Abs(_canvas.InDocument(zoomPoint).X - anchored.X) < 0.01
            && Math.Abs(_canvas.InDocument(zoomPoint).Y - anchored.Y) < 0.01, "Zoom tool preserves the pixel under the pointer");
        this.MouseDown(onCanvas, MouseButton.Left, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        this.MouseUp(onCanvas, MouseButton.Left, RawInputModifiers.Alt); Layout();
        Check(Math.Abs(_canvas.Zoom / initialZoom - 1) < 0.001, "Alt-click zooms back out");
        this.MouseDown(onCanvas, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        this.MouseMove(new Point(onCanvas.X + 100, onCanvas.Y), RawInputModifiers.LeftMouseButton);
        this.MouseUp(new Point(onCanvas.X + 100, onCanvas.Y), MouseButton.Left, RawInputModifiers.None); Layout();
        Check(Math.Abs(_canvas.Zoom / initialZoom - 2) < 0.001, "Zoom scrubbing follows the Mac's doubling per 100 points");
        _canvas.Fit(); Layout();
        var edge = _panelResize!;
        var start = Middle(edge);

        this.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        this.MouseMove(new Point(start.X - 50, start.Y), RawInputModifiers.LeftMouseButton);
        this.MouseUp(new Point(start.X - 50, start.Y), MouseButton.Left, RawInputModifiers.None);
        Layout();

        Check(_layersSide.Width == 302, "panel divider really resizes with the mouse");
        _layersSide.Width = 252;
        var held = _open;
        for (var index = 0; index < 6; index++) _tabs.Add(new Tab { Document = new CanvasDocument(Guid.NewGuid(), 64, 64),
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"中英文很长的工程标题-LongProject-{index}.comp") });
        RefreshTabs(); Width = 800; Layout();
        CheckTabSpacing("overflow");
        Check(_tabStrip.HiddenCount > 0 && _tabStrip.VisibleIDs.Contains(held.ID), "overflow keeps the selected tab visible");
        foreach (var button in _toolbar.Values) Inside(button, this, "overflow preserves view buttons");
        Photograph("tabs-overflow.png");
        Width = 1920; Layout();
        var pill = _tabStrip.Pill(held.ID)!;
        start = pill.TranslatePoint(new Point(25, 14), this)!.Value;
        var destination = _tabStrip.Pill(_tabs[^1].ID)!.TranslatePoint(new Point(30, 14), this)!.Value;

        this.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        for (var index = 1; index <= 12; index++)
        {
            this.MouseMove(new Point(start.X + (destination.X - start.X) * index / 12, start.Y), RawInputModifiers.LeftMouseButton);
            Layout();
            CheckTabSpacing("tab drag");
        }
        Photograph("tabs-drag.png");
        this.MouseUp(destination, MouseButton.Left, RawInputModifiers.None); Layout();
        Check(_tabs.IndexOf(held) > 0 && ReferenceEquals(_open, held), "drag reorders a real tab and preserves its document");
        Photograph("tabs-reordered.png");
        _tabs.RemoveAll(tab => !ReferenceEquals(tab, held)); RefreshTabs();
        MacParitySelfCheck(output, report);
        Width = 1180; Height = 780; SetTool(Tool.Move); Layout(); _canvas.Fit();
        DesignSelfCheck(output, report);
        Photograph("main-zh-CN.png");
        ChangeLanguage("en"); Layout();
        Photograph("main-en.png");
        return string.Join(Environment.NewLine, report);
    }
}
