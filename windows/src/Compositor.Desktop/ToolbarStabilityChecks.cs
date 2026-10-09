using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Compositor.Desktop;

internal static class ToolbarStabilityChecks
{
    internal static void Run(List<string> report, string output)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("TOOLBAR STABILITY FAILED: " + name); report.Add("PASS: " + name); }
        var language = UiText.Language;
        try
        {
            foreach (var locale in new[] { "en", "zh-CN" })
            foreach (var width in new[] { 760, 1180, 1680 })
            {
                UiText.Language = locale;
                var options = new ToolOptions(); var current = Tool.Brush; var mask = false;
                var bar = new ToolOptionsBar(options) { VerticalAlignment = VerticalAlignment.Top };
                var window = new Window { Width = width, Height = 90, Background = Skin.ChromeBrush, Content = bar };
                UiText.WireChoices(window);
                void Show(Tool tool) { current = tool; bar.Show(tool, true, mask); }
                void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
                Rect Bounds(Control control)
                { var point = control.TranslatePoint(new Point(), window)!.Value; return new Rect(point, control.Bounds.Size); }
                void Stable(string name, IEnumerable<Control> controls, Action change)
                {
                    var before = controls.Distinct().ToDictionary(control => control, Bounds);
                    change(); Layout();
                    foreach (var (control, bounds) in before)
                    {
                        var after = Bounds(control);
                        Check(Math.Abs(after.X - bounds.X) < 0.1 && Math.Abs(after.Width - bounds.Width) < 0.1,
                            $"{locale}/{width} {name}: {control.GetType().Name} keeps its position and width");
                    }
                    Check(UiLayoutAudit.CheckText(bar, name) > 0, $"{locale}/{width} {name}: captions and numeric values stay fully visible");
                }
                void Click(Button button)
                {
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(point, RawInputModifiers.None);
                    window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                    window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Layout();
                }
                void Capture(string name)
                {
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
                }
                bar.Changed += () => bar.Show(current, true, mask);
                window.Show(); Show(Tool.Brush); Layout();
                try
                {
                    foreach (var onMask in new[] { false, true })
                    {
                        mask = onMask; options.Erase = false; Show(Tool.Brush); Layout();
                        var common = bar.CellsFor("mode").Concat(bar.CellsFor("brush")).Concat(bar.CellsFor("smoothing"));
                        Stable("Paint to Erase", common, () => Click(bar.BrushModeChoice.ButtonAt(1)));
                        Check(options.Erase && bar.TitleLabel.Text == UiText.Get("Eraser"), "the actual Erase click updates mode and title");
                        if (!mask && width == 1180) Capture("toolbar-" + locale + "-erase");
                        Stable("Erase to Paint", common, () => Click(bar.BrushModeChoice.ButtonAt(0)));
                        Check(!options.Erase && bar.TitleLabel.Text == UiText.Get("Brush"), "the actual Paint click restores mode and title");
                        if (!mask && width == 1180) Capture("toolbar-" + locale + "-paint");
                    }
                    mask = false; Show(Tool.Marquee); Layout();
                    Stable("Rectangle to Ellipse", bar.CellsFor("marqueeShape"), () => Show(Tool.Ellipse));
                    Show(Tool.Lasso); Layout();
                    Stable("Freehand to Polygonal", bar.CellsFor("lasso"), () => Show(Tool.Polygon));
                    Show(Tool.Pan); bar.ShowZoom(100); Layout();
                    Stable("Pan to Zoom", bar.CellsFor("zoom"), () => Show(Tool.Zoom));
                    Show(Tool.Liquify); Layout();
                    Stable("Liquify to Blur", bar.CellsFor("smear").Concat(bar.CellsFor("brush")), () => Show(Tool.Blur));
                    Stable("Blur to Smudge", bar.CellsFor("smear").Concat(bar.CellsFor("brush")), () => Show(Tool.Smudge));
                    options.Wand = options.Wand with { Tolerance = 0, Radius = 0 }; Show(Tool.Wand); Layout();
                    Stable("Wand 0 to maximum", bar.CellsFor("wand"), () =>
                    { options.Wand = options.Wand with { Tolerance = 255, Radius = 100 }; Show(Tool.Wand); });
                    Stable("Wand sample layer to all layers", bar.CellsFor("wand"), () =>
                    { options.WandAllLayers = true; Show(Tool.Wand); });
                    Show(Tool.Gradient); Layout();
                    foreach (var choice in bar.CellsFor("gradient").OfType<ComboBox>())
                    for (var index = 0; index < choice.Items.Count; index++)
                    {
                        var selected = index;
                        Stable("Gradient choice " + selected, bar.CellsFor("gradient"), () => choice.SelectedIndex = selected);
                    }
                    options.ShapeCornerRadius = 0; Show(Tool.Shape); Layout();
                    Stable("Radius 0 to 1000", bar.CellsFor("corner"), () =>
                    { options.ShapeCornerRadius = 1000; Show(Tool.Shape); });
                    var cornerBounds = Bounds(bar.CellsFor("corner").Single());
                    Stable("Shape rectangle to line", bar.CellsFor("shape"), () =>
                    { options.Shape = Compositor.Core.Format.ShapeKind.Line; Show(Tool.Shape); });
                    var lineBounds = Bounds(bar.CellsFor("linewidth").Single());
                    Check(Math.Abs(cornerBounds.X - lineBounds.X) < 0.1 && Math.Abs(cornerBounds.Width - lineBounds.Width) < 0.1,
                        $"{locale}/{width} switching Radius to Width keeps the amount in the same place");
                    options.Brush = options.Brush with { Diameter = 1, Hardness = 0, Opacity = 0.01 };
                    Show(Tool.Brush); Layout();
                    Stable("Brush amounts 1 to maximum", bar.CellsFor("brush"), () =>
                    { options.Brush = options.Brush with { Diameter = 2000, Hardness = 1, Opacity = 1 }; Show(Tool.Brush); });
                }
                finally { window.Close(); }
            }
        }
        finally { UiText.Language = language; }
    }
}
