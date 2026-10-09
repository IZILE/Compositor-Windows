using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

/// <summary>Regression checks for shared control themes, capture and interrupted feedback.</summary>
internal static class ControlMotionChecks
{
    internal static void Run(List<string> report, string output)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("CONTROL MOTION FAILED: " + name); report.Add("PASS: " + name); }
        var content = new Border { Width = 1000, Height = 1400, Background = Brushes.DimGray };
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        var window = new Window { Width = 400, Height = 320, Content = scroll };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.Show(); Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
        try
        {
            foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
            {
                var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(item => item.Orientation == orientation);
                var thumb = bar.GetVisualDescendants().OfType<Thumb>().Single();
                var face = thumb.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "scrollKnob");
                Check(face.CornerRadius.TopLeft == 4 && face.Transitions?.Count > 0, orientation + " scrollbar uses the shared capsule feedback");
                var start = Middle(thumb); var prior = orientation == Orientation.Vertical ? scroll.Offset.Y : scroll.Offset.X;
                using var clock = new UiAnimationClock(face);
                window.MouseMove(new Point(1, 1), RawInputModifiers.None); clock.Pulse(0); clock.Pulse(100); Layout();
                window.MouseMove(start, RawInputModifiers.None); window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                clock.Pulse(110); Layout();
                var readings = new List<double>();
                for (var time = 110; time <= 190; time += 10)
                { clock.Pulse(time); Layout(); readings.Add(((ISolidColorBrush)face.Background!).Color.A / 255.0); }
                Check(readings.Skip(1).SkipLast(1).Any(value => value > readings[0] + 0.001 && value < readings[^1] - 0.001),
                    orientation + " scrollbar press has intermediate feedback frames");
                var offsets = new List<double>();
                for (var step = 1; step <= 6; step++)
                {
                    var point = start + (orientation == Orientation.Vertical ? new Vector(0, step * 3) : new Vector(step * 3, 0));
                    window.MouseMove(point, RawInputModifiers.LeftMouseButton); Layout();
                    offsets.Add(orientation == Orientation.Vertical ? scroll.Offset.Y : scroll.Offset.X);
                }
                Check(offsets[0] > prior && offsets.Zip(offsets.Skip(1)).All(pair => pair.Second >= pair.First),
                    orientation + " scrollbar drag follows the pointer in the correct direction without reversing");
                var last = start + (orientation == Orientation.Vertical ? new Vector(0, 18) : new Vector(18, 0));
                window.MouseUp(last, MouseButton.Left, RawInputModifiers.None); window.MouseMove(new Point(1, 1), RawInputModifiers.None);
                clock.Pulse(200); clock.Pulse(300); Layout();
                Check(Math.Abs(((ISolidColorBrush)face.Background!).Color.A / 255.0 - 128 / 255.0) < 0.005,
                    orientation + " scrollbar release removes the pressed tint");
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "scrollbar-" + orientation + "-motion.txt"),
                    "feedback: " + string.Join(", ", readings) + "\noffsets: " + string.Join(", ", offsets));
            }
        }
        finally { window.Close(); }
    }
}
