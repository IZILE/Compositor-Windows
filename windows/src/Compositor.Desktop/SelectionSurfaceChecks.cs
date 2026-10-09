using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Check painted pixels during hover/selection handoff, not only indicator coordinates.</summary>
internal static class SelectionSurfaceChecks
{
    internal static void Run(List<string> report, string output)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("SELECTION SURFACE FAILED: " + name); report.Add("PASS: " + name); }
        var rail = new ToolRail(); rail.Mark(Tool.Move); rail.Chosen += rail.Mark;
        var segments = new SegmentedChoice("Paint", "Erase") { HorizontalAlignment = HorizontalAlignment.Left };
        var tabs = new ProjectTabs(); var selected = 0; var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        void RefreshTabs() => tabs.Update(ids.Select((id, index) => new ProjectTabs.Entry(id, "Project " + index, false,
            index == selected, () => { selected = index; RefreshTabs(); }, () => { })), (_, _) => { });
        RefreshTabs();
        var right = new StackPanel { Margin = new Thickness(12), Spacing = 20, Children = { segments, tabs } };
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("56,*"), Children = { rail, right } };
        Grid.SetColumn(right, 1);
        var window = new Window { Width = 650, Height = 850, Background = Skin.ChromeBrush, Content = host };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        void Pointer(Point point) { window.MouseMove(point, RawInputModifiers.None); Layout(); }
        SKBitmap Frame(string? name = null)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            using var frame = window.CaptureRenderedFrame()!;
            using var stream = new MemoryStream(); frame.Save(stream, new PngBitmapEncoderOptions()); var bytes = stream.ToArray();
            if (name is not null) File.WriteAllBytes(Path.Combine(output, name + ".png"), bytes);
            return SKBitmap.Decode(bytes);
        }
        SKColor Pixel(Point point)
        { using var frame = Frame(); return frame.GetPixel((int)point.X, (int)point.Y); }
        window.Show(); Layout();
        try
        {
            using var positions = new UiAnimationClock(rail.SelectionPosition, segments.Indicator.Position, tabs.Indicator.Position);
            positions.Pulse(0); positions.Pulse(200); Layout();
            var positionTime = 210;
            Button TabButton(int index) => tabs.Pill(ids[index])!.GetVisualDescendants().OfType<Button>().First();
            foreach (var (name, first, second, changed) in new (string, Button, Button, Func<bool>)[]
            {
                ("rail", rail.ButtonFor(Tool.Move)!, rail.ButtonFor(Tool.Brush)!, () => rail.Marked == Tool.Brush),
                ("segments", segments.ButtonAt(0), segments.ButtonAt(1), () => segments.SelectedIndex == 1),
                ("tabs", TabButton(0), TabButton(1), () => selected == 1),
            })
            {
                var controls = new[] { first, second };
                var feedback = controls.Select(button => button.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "feedback")).ToArray();
                var labels = controls.Select(button => button.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter")).ToArray();
                using var clock = new UiAnimationClock(feedback.Cast<Avalonia.Animation.Animatable>().Concat(labels).ToArray());
                for (var index = 0; index < controls.Length; index++)
                {
                    var startTime = index * 400;
                    var button = controls[index]; Pointer(new Point(640, 840)); clock.Pulse(startTime); clock.Pulse(startTime + 100); Layout();
                    var probe = button.TranslatePoint(new Point(4, button.Bounds.Height / 2), window)!.Value;
                    var before = Pixel(probe);
                    Pointer(Middle(button)); clock.Pulse(startTime + 110); clock.Pulse(startTime + 150); Layout();
                    var during = Pixel(probe); clock.Pulse(startTime + 210); Layout(); var after = Pixel(probe);
                    Check(before == during && before == after,
                        $"{name} {(index == 0 ? "selected" : "unselected")}: hovering does not paint a second background over the moving selection");
                    Check(!feedback[index].IsVisible && feedback[index].Opacity == 0,
                        $"{name}: the stationary hover/pressed plate stays hidden");
                }
                var at = Middle(second); var initial = labels[1].Opacity;
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton); Layout();
                clock.Pulse(620); clock.Pulse(660); Layout(); var pressed = labels[1].Opacity;
                Check(pressed < initial && pressed > 0.68, name + ": press feedback has a continuous intermediate content tint");
                window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Layout();
                Check(changed(), name + ": clicking still selects once using the normal operating logic");
                Check(!feedback[1].IsVisible && feedback[1].Opacity == 0, name + ": click handoff never reveals an overlapping background");
                clock.Pulse(670); clock.Pulse(770); positions.Pulse(positionTime); positions.Pulse(positionTime + 200); positionTime += 210; Layout();
                var samePresenter = ReferenceEquals(labels[1], second.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter"));
                Check(Math.Abs(labels[1].Opacity - 1) < 0.001 && second.IsPointerOver && !second.IsPressed && samePresenter,
                    name + ": release preserves the hovered content and returns smoothly to its hover tint");
                using var image = Frame("single-selection-" + name);
            }
        }
        finally { window.Close(); }
    }
}
