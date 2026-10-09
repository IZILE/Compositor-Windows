using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

internal static class MenuMotionChecks
{
    internal static void Run(List<string> report, string output)
    {
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("MENU MOTION FAILED: " + name); report.Add("PASS: " + name); }
        var first = new MenuItem { Header = "First filter" };
        var disabled = new MenuItem { Header = "Unavailable filter", IsEnabled = false };
        var last = new MenuItem { Header = "Motion blur…" };
        var submenu = new MenuItem { Header = "More filters", Items = { new MenuItem { Header = "Nested filter" }, new MenuItem { Header = "Another nested filter" } } };
        var filter = new MenuItem { Header = "Filters", Items = { first, disabled, new Separator(), last, submenu } };
        var file = new MenuItem { Header = "File", Items = { new MenuItem { Header = "New" } } };
        var menu = new Menu { Items = { file, filter } };
        var window = new Window { Width = 500, Height = 260, Background = Skin.ChromeBrush,
            Content = new DockPanel { LastChildFill = false, Children = { menu } } };
        DockPanel.SetDock(menu, Dock.Top);
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Capture(string name)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            using var frame = window.CaptureRenderedFrame()!; frame.Save(System.IO.Path.Combine(output, name + ".png"), new PngBitmapEncoderOptions());
        }
        window.Show(); Layout();
        try
        {
            var top = menu.GetVisualDescendants().OfType<MenuSelectionPanel>().Single();
            using (var clock = new UiAnimationClock(top))
            {
                file.IsSelected = true; Layout(); clock.Pulse(0);
                file.IsSelected = false; filter.IsSelected = true; Layout(); clock.Pulse(0); clock.Pulse(60); Layout();
                Check(top.HighlightBounds.X > file.Bounds.X && top.HighlightBounds.X < filter.Bounds.X,
                    "top menu buttons share a highlight with intermediate horizontal positions");
                var before = top.HighlightBounds; file.IsSelected = true; filter.IsSelected = false; Layout(); clock.Pulse(60);
                Check(Math.Abs(top.HighlightBounds.X - before.X) < 0.1, "top menu reversal continues at the displayed position");
                clock.Pulse(240); file.IsSelected = false; Layout(); clock.Pulse(240); clock.Pulse(340); Layout();
            }
            filter.IsSubMenuOpen = true; Layout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Layout();
            var popup = filter.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().Single();
            var panel = popup.Child!.GetVisualDescendants().OfType<MenuSelectionPanel>().Single();
            using (var clock = new UiAnimationClock(panel))
            {
                first.IsSelected = true; Layout(); clock.Pulse(0);
                var popupRoot = TopLevel.GetTopLevel(first)!;
                var hoverPoint = last.TranslatePoint(new Point(last.Bounds.Width / 2, last.Bounds.Height / 2), popupRoot)!.Value;
                popupRoot.MouseMove(hoverPoint, RawInputModifiers.None); Layout(); clock.Pulse(0); clock.Pulse(160); Layout();
                Check(ReferenceEquals(panel.Highlighted, last), "real pointer hover moves the menu's shared highlight");
                popupRoot.MouseMove(new Point(-5, -5), RawInputModifiers.None); first.IsSelected = true; last.IsSelected = false; Layout();
                clock.Pulse(160); clock.Pulse(320); Layout();
            }
            using (var clock = new UiAnimationClock(panel))
            {
                first.IsSelected = true; last.IsSelected = false; Layout(); clock.Pulse(0);
                first.IsSelected = false; last.IsSelected = true; Layout(); clock.Pulse(0);
                var values = new List<double>();
                for (var time = 0; time <= 160; time += 10)
                { clock.Pulse(time); Layout(); values.Add(panel.HighlightBounds.Y); }
                Check(values.Skip(1).SkipLast(1).Any(value => value > values[0] + 0.1 && value < values[^1] - 0.1)
                    && values.Zip(values.Skip(1)).All(pair => pair.Second >= pair.First - 0.01),
                    "menu highlight travels across disabled items and separators through continuous frames");
                Check(ReferenceEquals(panel.Highlighted, last), "the final highlight belongs to the latest enabled menu item");
                var face = last.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_LayoutRoot");
                Check(face.Background is ISolidColorBrush brush && brush.Color.A == 0,
                    "selected menu items do not paint a second stationary highlight");
                clock.Pulse(170); last.IsSelected = false; first.IsSelected = true; Layout(); clock.Pulse(170); clock.Pulse(200);
                var before = panel.HighlightBounds.Y; first.IsSelected = false; last.IsSelected = true; Layout(); clock.Pulse(200);
                Check(Math.Abs(panel.HighlightBounds.Y - before) < 0.1, "rapid menu reversal preserves the current highlight position");
                clock.Pulse(370); last.IsSelected = false; first.IsSelected = true; Layout();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); Layout(); clock.Pulse(370); clock.Pulse(550); Layout();
                Check(ReferenceEquals(panel.Highlighted, last), "keyboard Down skips disabled items and separators and moves the same highlight");
                last.IsSelected = false; submenu.IsSelected = true; submenu.IsSubMenuOpen = true; Layout();
                Check(ReferenceEquals(panel.Highlighted, submenu) && submenu.IsSubMenuOpen,
                    "opening a submenu retains its parent's menu highlight");
                submenu.IsSubMenuOpen = false; filter.IsSubMenuOpen = false; Layout(); clock.Pulse(800); Layout();
                Check(!popup.IsOpen && panel.HighlightAlpha < 0.01, "closing the menu cancels queued highlight updates without a stale flash");
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "menu-selection-motion.txt"), string.Join(", ", values));
            }
            filter.IsSubMenuOpen = true; Layout(); first.IsSelected = true; Layout();
            using (var clock = new UiAnimationClock(popup.Child!))
            { clock.Pulse(100); Layout(); Capture("menu-continuous-selection"); }
            filter.IsSubMenuOpen = false;
        }
        finally { window.Close(); }
    }
}
