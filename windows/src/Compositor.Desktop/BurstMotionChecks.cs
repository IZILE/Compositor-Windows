using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

/// <summary>Input bursts between frames, rather than only already-advanced transition samples.</summary>
internal static class BurstMotionChecks
{
    internal static void Run(List<string> report)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("BURST MOTION FAILED: " + name); report.Add("PASS: " + name); }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12) };
        var window = new Window { Width = 660, Height = 190, Content = row };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Point Middle(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        void Pointer(Point point) { window.MouseMove(point, RawInputModifiers.None); Layout(); }
        window.Show(); Layout();
        try
        {
            var shared = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(80) } };
            var first = new Border { Width = 20, Height = 20, Opacity = 0, Transitions = shared };
            var second = new Border { Width = 20, Height = 20, Opacity = 0, Transitions = shared };
            row.Children.Add(first); row.Children.Add(second); Layout();
            using (var clock = new UiAnimationClock(first, second))
            {
                Motion.Set(first, Visual.OpacityProperty, 1.0); Motion.Set(second, Visual.OpacityProperty, 1.0);
                var readings = new List<double>();
                first.PropertyChanged += (_, args) => { if (args.Property == Visual.OpacityProperty) readings.Add(first.Opacity); };
                Motion.Set(first, Visual.OpacityProperty, 0.0);
                Check(first.Opacity == 0 && readings.All(value => value == 0), "cancelling a same-frame opacity change never exposes the discarded value");
                clock.Pulse(0); clock.Pulse(40); Layout();
                Check(first.Opacity == 0 && second.Opacity is > 0 and < 1 && shared.Count == 1,
                    "cancelling one control preserves another control's shared transition collection");
                clock.Pulse(100); Layout();
                Check(first.Opacity == 0 && second.Opacity == 1, "a cancelled transition cannot resume on a later frame");
            }
            row.Children.Clear();
            var transform = new TranslateTransform { Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromMilliseconds(160) },
                new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(160) },
            } };
            using (var clock = new UiAnimationClock(transform))
            {
                Motion.Set(transform, TranslateTransform.XProperty, 200.0); Motion.Set(transform, TranslateTransform.YProperty, 120.0);
                Motion.Set(transform, TranslateTransform.XProperty, 0.0); clock.Pulse(0); clock.Pulse(40);
                Check(transform.X == 0 && transform.Y is > 0 and < 120, "cancelling the horizontal axis preserves vertical motion");
                var displayed = transform.Y;
                Motion.Set(transform, TranslateTransform.YProperty, 90.0); Motion.Set(transform, TranslateTransform.YProperty, displayed);
                Check(transform.Y == displayed, "retargeting back to an intermediate displayed coordinate never jumps to an abandoned target");
                clock.Pulse(300); Check(transform.Y == displayed, "returning to an intermediate coordinate settles there");
            }

            var buttons = new Button[]
            {
                new() { Content = "Regular" }, new() { Content = "Plain", Classes = { "plain" } },
                new() { Content = "Apply", Classes = { "accent" } }, new ToggleButton { Content = "Toggle" },
            };
            foreach (var button in buttons) row.Children.Add(button);
            Layout();
            foreach (var button in buttons)
            {
                var feedback = button.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "feedback");
                using var clock = new UiAnimationClock(feedback);
                Pointer(new Point(2, 2)); clock.Pulse(0); clock.Pulse(100); Layout();
                Pointer(Middle(button)); Pointer(new Point(2, 2));
                Check(feedback.Opacity == 0, $"{button.Content}: hover and leave within one frame do not flash");
                clock.Pulse(110); clock.Pulse(210); Layout();
                Pointer(Middle(button)); clock.Pulse(220); clock.Pulse(320); Layout();
                var before = feedback.Opacity;
                window.MouseDown(Middle(button), MouseButton.Left, RawInputModifiers.LeftMouseButton); Layout();
                window.MouseUp(Middle(button), MouseButton.Left, RawInputModifiers.None); Layout();
                Check(Math.Abs(feedback.Opacity - before) < 0.001, $"{button.Content}: same-frame press and release retain the hover tint");
                clock.Pulse(330); clock.Pulse(430); Layout();
                Check(Math.Abs(feedback.Opacity - 0.055) < 0.001, $"{button.Content}: rapid feedback settles without a stale pressed state");
            }
            var toggle = (ToggleButton)buttons[^1];
            var face = toggle.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "buttonFace");
            using (var clock = new UiAnimationClock(face))
            {
                toggle.IsChecked = false; clock.Pulse(0); clock.Pulse(200); Layout();
                var before = ((ISolidColorBrush)face.Background!).Color;
                toggle.IsChecked = true; toggle.IsChecked = false; Layout();
                Check(face.Background is ISolidColorBrush brush && brush.Color == before,
                    "toggle background: a same-frame change does not flash the abandoned checked color");
            }
            row.Children.Clear();
            var raw = new CameraRawPanel.RawSection("Light", new Border { Height = 120, Width = 160 }, false);
            row.Children.Add(raw); Layout();
            using (var clock = new UiAnimationClock(raw.BodyHost, (RotateTransform)raw.Disclosure.RenderTransform!))
            {
                raw.Expanded = true; raw.Expanded = false; Layout();
                Check(raw.BodyHost.Height == 0 && raw.BodyHost.Opacity == 0 && ((RotateTransform)raw.Disclosure.RenderTransform!).Angle == 0,
                    "Camera Raw: same-frame expand and collapse do not flash content or the chevron");
                clock.Pulse(0); clock.Pulse(200); Layout();
                Check(raw.BodyHost.Height == 0 && !raw.BodyHost.IsHitTestVisible, "Camera Raw: cancelled expansion leaves no invisible interactive content");
            }
        }
        finally { window.Close(); }
    }
}
