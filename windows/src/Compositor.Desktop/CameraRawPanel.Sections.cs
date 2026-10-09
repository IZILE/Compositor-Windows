using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using SkiaSharp;

namespace Compositor.Desktop;

internal sealed partial class CameraRawPanel
{
    private readonly Dictionary<string, RawSection> _sections = [];
    internal IReadOnlyDictionary<string, RawSection> Sections => _sections;

    private void BuildSections(StackPanel groups)
    {
        var children = groups.Children.ToArray();
        groups.Children.Clear();
        groups.Spacing = 12;
        var contents = new Dictionary<string, StackPanel>();
        StackPanel? current = null;
        foreach (var child in children)
        {
            if (child is TextBlock { Tag: string name })
            {
                contents[name] = current = new StackPanel { Spacing = 8, Margin = new Thickness(18, 6, 0, 0) };
            }
            else if (current is not null) current.Children.Add(child);
            else groups.Children.Add(child);
        }
        // These ten sections and their initial expansion match CameraRawControls.swift.
        foreach (var name in new[] { "Light", "Color", "Color grading", "Effects", "Curve", "Color mixer",
                     "Detail", "Optics", "Geometry", "Calibration" })
        {
            var body = contents[name];
            if (name == "Color mixer")
            {
                body.Children.Add(new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Point color"),
                    FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
                body.Children.Add(contents["Point color"]);
                contents["Point color"].Margin = new Thickness(0);
            }
            var section = new RawSection(name, body, name is "Light" or "Color" or "Color grading");
            _sections.Add(name, section);
            groups.Children.Add(section);
        }
    }

    private static void SetTrack(Slider slider, string label)
    {
        Color[]? colors = label switch
        {
            "Temperature, cool to warm" => [Color.FromRgb(56, 117, 242), Color.FromRgb(250, 209, 46)],
            "Tint, green to magenta" => [Color.FromRgb(71, 179, 87), Color.FromRgb(179, 102, 163)],
            "Vibrance" or "Saturation" => [Color.FromRgb(158, 158, 163), Color.FromRgb(219, 46, 51)],
            _ => null,
        };
        var family = Array.FindIndex(CameraRawSettings.MixerFamilies, name => label.StartsWith(name + ":", StringComparison.Ordinal));
        if (family >= 0)
        {
            var hue = new float[] { 0, 30, 60, 120, 180, 240, 270, 300 }[family];
            Color Tone(float degrees, float saturation, float value)
            {
                var ink = SKColor.FromHsv((degrees + 360) % 360, saturation, value);
                return Color.FromRgb(ink.Red, ink.Green, ink.Blue);
            }
            colors = label.EndsWith(": hue", StringComparison.Ordinal)
                ? [Tone(hue - 50, 85, 90), Tone(hue + 50, 85, 90)]
                : label.EndsWith(": saturation", StringComparison.Ordinal)
                    ? [Color.FromRgb(140, 140, 143), Tone(hue, 90, 90)]
                    : [Tone(hue, 55, 18), Tone(hue, 35, 95)];
        }
        if (colors is null) return;
        slider.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = new GradientStops { new GradientStop(colors[0], 0), new GradientStop(colors[^1], 1) },
        };
    }

    internal sealed class RawSection : StackPanel
    {
        private readonly Control _body;
        private readonly Border _bodyHost;
        private readonly Chevron _chevron;
        private bool _expanded;
        private bool _settingHeight;
        private double _targetHeight;
        internal Button Header { get; }
        internal bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value; _bodyHost.IsHitTestVisible = value;
                Motion.Set(_bodyHost, OpacityProperty, value ? 1.0 : 0.0);
                Motion.Set((RotateTransform)_chevron.RenderTransform!, RotateTransform.AngleProperty, value ? 90.0 : 0.0);
                UpdateHeight();
            }
        }
        internal Border BodyHost => _bodyHost;
        internal Control Disclosure => _chevron;

        private void UpdateHeight()
        {
            if (_settingHeight) return;
            _settingHeight = true;
            try
            {
                var width = Bounds.Width > 0 ? Bounds.Width : 380;
                _body.Measure(new Size(width, double.PositiveInfinity));
                var height = _expanded ? _body.DesiredSize.Height : 0;
                // Compare the destination, not the currently animated height, to avoid restarting a transition.
                if (Math.Abs(_targetHeight - height) > 0.1) { _targetHeight = height; Motion.Set(_bodyHost, HeightProperty, height); }
            }
            finally { _settingHeight = false; }
        }

        internal RawSection(string name, Control body, bool expanded)
        {
            _body = body;
            _body.VerticalAlignment = VerticalAlignment.Top;
            _bodyHost = new Border { Child = body, Height = 0, ClipToBounds = true,
                Transitions = new Transitions
                {
                    new DoubleTransition { Property = HeightProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, 0.58, 1) },
                    new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(80), Easing = new SplineEasing(0, 0, 0.58, 1) },
                } };
            _chevron = new Chevron { Width = 12, Height = 16, VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new RotateTransform { Transitions = new Transitions
                { new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, 0.58, 1) } } } };
            Header = new Button
            {
                Classes = { "plain" }, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { _chevron, new TextBlock { [!TextBlock.TextProperty] = UiText.Bind(name),
                        FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center } },
                },
            };
            Header.Click += (_, _) => Expanded = !Expanded;
            Children.Add(Header);
            Children.Add(_bodyHost);
            Expanded = expanded;
            SizeChanged += (_, args) => { if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 0.1) UpdateHeight(); };
        }
    }

    private sealed class Chevron : Control
    {
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(Skin.LabelBrush, 1.5);
            context.DrawLine(pen, new Point(4, 3), new Point(8, 8));
            context.DrawLine(pen, new Point(8, 8), new Point(4, 13));
        }
    }
}
