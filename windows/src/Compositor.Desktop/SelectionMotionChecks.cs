using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>Sample the real selection surfaces under interruption, relayout and pointer input.</summary>
internal static class SelectionMotionChecks
{
    internal static void Run(List<string> report, string output)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("SELECTION MOTION FAILED: " + name); report.Add("PASS: " + name); }
        var host = new StackPanel { Margin = new Thickness(12), Spacing = 12 };
        var window = new Window { Width = 620, Height = 180, Background = Skin.ChromeBrush, Content = host };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point, RawInputModifiers.None);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Layout();
        }
        void Capture(string name)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            using var frame = window.CaptureRenderedFrame()!;
            frame.Save(System.IO.Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        var oldLanguage = UiText.Language;
        window.Show(); Layout();
        try
        {
            var rail = new ToolRail(); host.Children.Add(rail); window.Height = 850; Layout();
            rail.Chosen += rail.Mark;
            using (var clock = new UiAnimationClock(rail.SelectionPosition))
            {
                var start = rail.SelectionPosition.Y;
                Click(rail.ButtonFor(Tool.Move)!); Click(rail.ButtonFor(Tool.Pan)!);
                Check(Math.Abs(rail.SelectionPosition.Y - start) < 0.1,
                    "rail: away-and-back pointer clicks before a frame keep the displayed selection");
                clock.Pulse(0); clock.Pulse(200); Layout();
                Check(Math.Abs(rail.SelectionPosition.Y - start) < 0.1,
                    "rail: cancelled selection never resumes on a later frame");
                var time = 210;
                foreach (var tool in new[] { Tool.Move, Tool.Brush, Tool.Zoom, Tool.Clone, Tool.Pan })
                {
                    var before = rail.SelectionPosition.Y; Click(rail.ButtonFor(tool)!);
                    Check(Math.Abs(rail.SelectionPosition.Y - before) < 0.1,
                        "rail: retarget preserves its position before the next frame: " + tool);
                    clock.Pulse(time); clock.Pulse(time + 20); Layout(); time += 30;
                }
                clock.Pulse(800); Layout();
                Check(rail.Marked == Tool.Pan && Math.Abs(rail.SelectionPosition.Y - rail.SelectionDestination) < 0.1,
                    "rail: mixed-distance rapid clicks finish on the latest tool");
            }
            host.Children.Clear(); window.Height = 180; Layout();
            string[][] families = [["Paint", "Erase"], ["Liquify", "Blur", "Smudge"],
                ["Content-Aware", "Create Texture", "Proximity Match"], ["This Layer", "All Layers"],
                ["Off", "Guided"], ["RGB", "Red", "Green", "Blue"]];
            for (var family = 0; family < families.Length; family++)
            {
                var choice = new SegmentedChoice(families[family]) { HorizontalAlignment = HorizontalAlignment.Left };
                choice.SelectedIndex = choice.Count - 1; host.Children.Add(choice); Layout();
                var indicator = choice.Indicator;
                Check(Math.Abs(indicator.Position.X - indicator.Destination.X) < 0.1,
                    $"segments {family}: initial non-first choice is placed without an entrance sweep");
                using var clock = new UiAnimationClock(indicator.Position, indicator);
                void Pulse(int time) { clock.Pulse(time); Layout(); }
                var start = indicator.Position.X;
                var changes = new List<int>(); choice.Changed += changes.Add;
                Click(choice.ButtonAt(0)); Click(choice.ButtonAt(choice.Count - 1));
                Check(Math.Abs(indicator.Position.X - start) < 0.1,
                    $"segments {family}: selecting away and back before the next frame never exposes the abandoned target");
                changes.Clear();
                Pulse(0); Click(choice.ButtonAt(0)); Pulse(0);
                var readings = new List<double>();
                for (var time = 0; time <= 160; time += 10)
                {
                    Pulse(time); readings.Add(indicator.Position.X);
                    if (family == 2 && time % 40 == 0) Capture($"segments-selection-{time:000}");
                }
                Check(Math.Abs(readings[0] - start) < 0.1 && readings.Skip(1).SkipLast(1).Any(value => value > 0.1 && value < start - 0.1)
                    && readings.Zip(readings.Skip(1)).All(pair => pair.Second <= pair.First + 0.01),
                    $"segments {family}: position moves continuously without flashing back");
                Check(Math.Abs(indicator.Width - indicator.Destination.Width) < 0.1 && changes.SequenceEqual(new[] { 0 }),
                    $"segments {family}: unequal label widths settle and a pointer click emits one logical change");
                Pulse(170); Click(choice.ButtonAt(choice.Count - 1)); Pulse(170); Pulse(210);
                var before = indicator.Position.X; var beforeWidth = indicator.Width;
                Click(choice.ButtonAt(0));
                Check(Math.Abs(indicator.Position.X - before) < 0.1 && Math.Abs(indicator.Width - beforeWidth) < 0.1,
                    $"segments {family}: retargeting between clock ticks keeps the displayed geometry");
                Pulse(210);
                Check(Math.Abs(indicator.Position.X - before) < 0.1 && Math.Abs(indicator.Width - beforeWidth) < 0.1,
                    $"segments {family}: reversing continues from the current position and width");
                for (var index = 0; index < 8; index++)
                { Pulse(230 + index * 20); Click(choice.ButtonAt(index % 2 == 0 ? choice.Count - 1 : 0)); Pulse(230 + index * 20); }
                UiText.Language = "zh-CN"; Layout(); Pulse(600); Pulse(800);
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, $"segments-{family}-latest.txt"),
                    $"index={choice.SelectedIndex}; X={indicator.Position.X}; destination={indicator.Destination}; width={indicator.Width}; button={choice.ButtonAt(0).Bounds}");
                Check(choice.SelectedIndex == 0 && Math.Abs(indicator.Position.X - indicator.Destination.X) < 0.1
                    && Math.Abs(indicator.Width - choice.ButtonAt(0).Bounds.Width) < 0.1,
                    $"segments {family}: rapid input and bilingual relayout finish on the latest choice");
                UiText.Language = "en"; host.Children.Remove(choice); Layout();
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, $"segments-{family}-motion.txt"), string.Join(", ", readings));
            }

            var tabs = new ProjectTabs(); host.Children.Add(tabs);
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            var titles = new[] { "Small", "A longer project title", "第三个工程" };
            var selected = 0;
            void Update() => tabs.Update(ids.Select((id, index) => new ProjectTabs.Entry(id, titles[index], false,
                selected == index, () => { selected = index; Update(); }, () => { })), (_, _) => { });
            Update(); Layout();
            var track = tabs.Indicator;
            using (var clock = new UiAnimationClock(track.Position, track))
            {
                void Pulse(int time) { clock.Pulse(time); Layout(); }
                Button Name(int index) => tabs.Pill(ids[index])!.GetVisualDescendants().OfType<Button>().First();
                var initial = track.Position.X; var initialWidth = track.Width;
                Click(Name(2)); Click(Name(0));
                Check(Math.Abs(track.Position.X - initial) < 0.1 && Math.Abs(track.Width - initialWidth) < 0.1,
                    "tabs: an away-and-back selection in one frame never flashes to the abandoned tab");
                Pulse(0); Click(Name(2)); Pulse(0);
                var values = new List<double>();
                for (var time = 0; time <= 160; time += 10)
                { Pulse(time); values.Add(track.Position.X); if (time % 40 == 0) Capture($"tabs-selection-{time:000}"); }
                Check(selected == 2 && values.Skip(1).SkipLast(1).Any(value => value > values[0] + 0.1 && value < values[^1] - 0.1)
                    && values.Zip(values.Skip(1)).All(pair => pair.Second >= pair.First - 0.01),
                    "project tab selection travels across neighboring capsules without reversing");
                Pulse(170); Click(Name(0)); Pulse(170); Pulse(210); var before = track.Position.X;
                Click(Name(1)); Pulse(210);
                Check(Math.Abs(track.Position.X - before) < 0.1, "project tab reversal keeps the displayed position");
                for (var index = 0; index < 8; index++)
                { Pulse(230 + index * 20); Click(Name(index % 3)); Pulse(230 + index * 20); }
                window.Width = 230; Layout(); Pulse(600); Pulse(800);
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "tabs-resized-latest.txt"),
                    $"hidden={tabs.HiddenCount}; selected={selected}; visible={string.Join(',', tabs.VisibleIDs)}; X={track.Position.X}; width={track.Width}; target={track.Destination}; strip={tabs.Bounds}");
                Check(tabs.HiddenCount > 0 && tabs.VisibleIDs.Contains(ids[selected])
                    && Math.Abs(track.Position.X - track.Destination.X) < 0.1
                    && track.Position.X >= -0.1 && track.Position.X + track.Width <= tabs.Bounds.Width + 0.1,
                    "tab overflow and interrupted selection finish inside the resized strip");
                tabs.Update([], (_, _) => { }); Layout();
                Check(!track.IsVisible, "closing the final tab removes its selection surface");
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "tabs-selection-motion.txt"), string.Join(", ", values));
            }
            host.Children.Clear(); window.Width = 620;
            var raw = new CameraRawPanel(new CameraRawSettings(), SKColors.Black);
            // Use the actual panel choices without hiding them in a collapsed or scrolled section.
            foreach (var choice in new[] { raw.UprightChoice, raw.CurveChannelChoice })
            { ((Panel)choice.Parent!).Children.Remove(choice); host.Children.Add(choice); }
            Layout(); Click(raw.UprightChoice.ButtonAt(1)); Click(raw.CurveChannelChoice.ButtonAt(2));
            Check(raw.Current().Geometry.Upright == CameraRawUprightMode.Guided && raw.ActiveCurveChannel == 2,
                "Camera Raw moving segments change the real geometry and curve channel");
            raw.Reset();
            Check(raw.UprightChoice.SelectedIndex == 0 && raw.CurveChannelChoice.SelectedIndex == 0
                && raw.Current().Geometry.Upright == CameraRawUprightMode.Off && raw.ActiveCurveChannel == 0,
                "Camera Raw Reset restores both segment positions and settings");
        }
        finally { UiText.Language = oldLanguage; window.Close(); }
    }
}
