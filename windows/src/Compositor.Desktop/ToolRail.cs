using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>
/// The tool rail down the left of the canvas, as the Mac keeps its own: one button per tool, then the two
/// colours the brush and the background are set to, with a swap and a reset under them. The port has no SF
/// Symbols, so each tool's mark is drawn here from lines and shapes.
/// </summary>
internal sealed class ToolRail : Grid
{
    /// <summary>How wide the rail is, which is the Mac's own 56 points.</summary>
    private const double RailWidth = 56;

    private readonly Dictionary<Tool, Button> _buttons = [];
    private readonly Dictionary<Tool, Tool> _familyTools = [];
    private readonly Dictionary<Tool, Glyph> _glyphs = [];
    private readonly Glyph _foreground = new() { Kind = Tool.Brush };
    private readonly Glyph _background = new() { Kind = Tool.Brush };
    private Button? _front;
    private Button? _back;
    private readonly ScrollViewer _scroll;
    private Tool _marked = Tool.Pan;
    private SelectionIndicator? _selection;
    internal TranslateTransform SelectionPosition => _selection!.Position;
    internal double SelectionDestination { get; private set; }
    private SKColor _foregroundColour = SKColors.Black;
    private SKColor _backgroundColour = SKColors.White;

    /// <summary>A tool was picked from the rail.</summary>
    public event Action<Tool>? Chosen;
    public event Action<bool>? EraseChosen;

    /// <summary>The two colours were swapped.</summary>
    public event Action? ColoursSwapped;

    /// <summary>The two colours were put back to black and white.</summary>
    public event Action? ColoursReset;

    /// <summary>One of the swatches was clicked: true for the foreground, false for the background.</summary>
    public event Action<bool>? ColourChosen;

    public ToolRail()
    {
        Width = RailWidth;
        // ContentView.swift scrolls the entire column, including its palette, without a visible indicator.
        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            Content = new StackPanel { Margin = new Thickness(0, 16, 0, 12), Spacing = 18, Children = { Tools(), Colours() } },
        };
        Children.Add(_scroll);
    }

    /// <summary>Marks the tool in hand, which is the only one lit.</summary>
    public void Mark(Tool tool)
    {
        _marked = tool;
        _familyTools[Family(tool)] = tool;
        if (_glyphs.TryGetValue(Family(tool), out var glyph)) { glyph.Kind = tool; glyph.InvalidateVisual(); }
        SelectionDestination = Array.IndexOf(RailTools, Family(tool)) * 46;
        _selection?.MoveTo(new Rect(0, SelectionDestination, 36, 36));
    }

    internal void ShowBrushMode(bool erasing)
    {
        if (!_glyphs.TryGetValue(Tool.Brush, out var glyph) || glyph.Erasing == erasing) return;
        glyph.Erasing = erasing; glyph.InvalidateVisual();
    }

    /// <summary>
    /// The tools as one column with no scroll around them. A bitmap does not lay a scroll view's content out,
    /// so this is what the self check draws: the marks are drawn shapes, and a picture is the only way to look
    /// at them.
    /// </summary>
    internal Control TakeTools()
    {
        var column = Tools();
        column.Margin = new Thickness(4);
        return column;
    }

    /// <summary>The button the rail has marked, which is what the self check reads to see the two agree.</summary>
    internal Tool Marked => _marked;

    /// <summary>
    /// The button a tool is picked by, which the checks press with a pointer: the rail is inside a scroll view,
    /// so a click on it is the one thing that proves the marks are not merely drawn but reachable.
    /// </summary>
    internal Button? ButtonFor(Tool tool) => _buttons.TryGetValue(tool, out var button) ? button : null;

    /// <summary>One of the two colour swatches, which is what a click there opens the picker through. True for
    /// the foreground. The self check is the only caller.</summary>
    internal Button? SwatchFor(bool foreground) => foreground ? _front : _back;

    /// <summary>Shows the two colours, as a swatch each.</summary>
    public void ShowColours(SKColor foreground, SKColor background)
    {
        _foregroundColour = foreground;
        _backgroundColour = background;
        _foreground.Fill = Colour(foreground);
        _background.Fill = Colour(background);
    }

    /// <summary>The two colours the rail is showing, as the brush and the background have them.</summary>
    internal (SKColor Foreground, SKColor Background) Palette => (_foregroundColour, _backgroundColour);

    private static IBrush Colour(SKColor colour) => new SolidColorBrush(
        Color.FromArgb(colour.Alpha, colour.Red, colour.Green, colour.Blue));

    private static Tool Family(Tool tool) => tool switch
    {
        Tool.Ellipse => Tool.Marquee, Tool.Polygon => Tool.Lasso,
        Tool.Smudge or Tool.Liquify => Tool.Blur, _ => tool,
    };

    private static readonly Tool[] RailTools = [Tool.Move, Tool.Marquee, Tool.Lasso, Tool.Wand, Tool.Crop, Tool.Brush,
        Tool.Heal, Tool.Clone, Tool.Blur, Tool.Gradient, Tool.Shape, Tool.Type, Tool.Eyedropper, Tool.Pan, Tool.Zoom];

    private Control Tools()
    {
        var column = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        SelectionDestination = Array.IndexOf(RailTools, Family(_marked)) * 46;
        var track = new Grid();
        _selection = new SelectionIndicator { Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(7),
            Background = Skin.TabFront, BorderBrush = Skin.TabFrontEdge, BorderThickness = new Thickness(1) };
        _selection.MoveTo(new Rect(0, SelectionDestination, 36, 36));
        track.Children.Add(_selection);
        foreach (var tool in RailTools)
        {
            var glyph = new Glyph { Kind = tool };
            _glyphs[tool] = glyph;
            var button = new Button
            {
                Content = glyph,
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(7),
                HorizontalAlignment = HorizontalAlignment.Center,
                Classes = { "plain", "selection-item" },
            };
            UiText.Set(button, ToolTip.TipProperty, Names[tool]);
            var picked = tool;
            button.Click += (_, _) => Chosen?.Invoke(_familyTools.GetValueOrDefault(picked, picked));
            var variants = tool switch
            {
                Tool.Marquee => new[] { Tool.Marquee, Tool.Ellipse }, Tool.Lasso => new[] { Tool.Lasso, Tool.Polygon },
                Tool.Blur => new[] { Tool.Liquify, Tool.Blur, Tool.Smudge }, _ => Array.Empty<Tool>(),
            };
            if (variants.Length > 0)
            {
                button.ContextMenu = new ContextMenu();
                foreach (var variant in variants)
                {
                    var item = new MenuItem { [!MenuItem.HeaderProperty] = UiText.Bind(Names[variant]) };
                    item.Click += (_, _) => Chosen?.Invoke(variant); button.ContextMenu.Items.Add(item);
                }
            }
            if (tool == Tool.Brush)
            {
                button.ContextMenu = new ContextMenu();
                foreach (var erasing in new[] { false, true })
                {
                    var item = new MenuItem { [!MenuItem.HeaderProperty] = UiText.Bind(erasing ? "Eraser" : "Brush") };
                    item.Click += (_, _) => EraseChosen?.Invoke(erasing); button.ContextMenu.Items.Add(item);
                }
            }
            _buttons[tool] = button;
            column.Children.Add(button);
        }
        track.Children.Add(column);
        return track;
    }

    /// <summary>
    /// The two swatches, overlapping as the Mac draws them, with a swap and a reset under them. The foreground
    /// is the one in front, because it is the one that is painted with.
    /// </summary>
    private Control Colours()
    {
        var swatches = new Canvas { Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Center };
        var back = Swatch(_background, false);
        var front = Swatch(_foreground, true);
        Canvas.SetLeft(back, 12);
        Canvas.SetTop(back, 12);
        swatches.Children.Add(back);
        swatches.Children.Add(front);
        var swap = Small("⇄", "Swap the foreground and background colors");
        var reset = Small("↺", "Put them back to black and white");
        swap.Click += (_, _) => ColoursSwapped?.Invoke();
        reset.Click += (_, _) => ColoursReset?.Invoke();
        Canvas.SetLeft(swap, 27); Canvas.SetTop(swap, -3);
        Canvas.SetLeft(reset, -1); Canvas.SetTop(reset, 27);
        swatches.Children.Add(swap); swatches.Children.Add(reset);
        return swatches;
    }

    private Button Swatch(Control glyph, bool foreground)
    {
        var button = new Button
        {
            Content = glyph,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.White, 0.35),
            CornerRadius = new CornerRadius(6),
            MinHeight = 24,
        };
        UiText.Set(button, ToolTip.TipProperty, foreground ? "Foreground color" : "Background color");
        button.Click += (_, _) => ColourChosen?.Invoke(foreground);
        if (foreground) _front = button;
        else _back = button;
        return button;
    }

    private static Button Small(string text, string hint)
    {
        var button = new Button
        {
            Content = text,
            Width = 12,
            Height = 12,
            MinHeight = 12,
            Padding = new Thickness(0),
            FontSize = 9,
            Classes = { "plain" },
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        UiText.Set(button, ToolTip.TipProperty, hint);
        return button;
    }

    /// <summary>What each button says it is, which is also what the Tools menu calls the tool.</summary>
    private static readonly Dictionary<Tool, string> Names = new()
    {
        [Tool.Pan] = "Pan — drag to scroll",
        [Tool.Zoom] = "Zoom — click or drag horizontally; Alt-click to zoom out",
        [Tool.Move] = "Move — drag the layer, or a handle to scale and turn it",
        [Tool.Marquee] = "Marquee — drag a rectangle",
        [Tool.Ellipse] = "Elliptical marquee — drag an oval",
        [Tool.Lasso] = "Lasso — drag round a shape",
        [Tool.Polygon] = "Polygonal lasso — click each corner",
        [Tool.Wand] = "Magic wand — click a color",
        [Tool.Brush] = "Brush",
        [Tool.Clone] = "Clone stamp — Alt-click a source first",
        [Tool.Blur] = "Blur brush",
        [Tool.Liquify] = "Liquify brush — push the pixels around",
        [Tool.Smudge] = "Smudge brush — drag the color along",
        [Tool.Heal] = "Spot healing",
        [Tool.Eyedropper] = "Eyedropper — click the canvas",
        [Tool.Type] = "Type — click where the text goes",
        [Tool.Crop] = "Crop — drag a frame, then apply it",
        [Tool.Shape] = "Shape — drag out a rectangle, ellipse or line",
        [Tool.Gradient] = "Gradient — drag the line it runs along",
    };

    /// <summary>
    /// One tool's mark, drawn rather than set in a font: the rail has no icon library behind it, so each is a
    /// few lines and shapes in a box of its own. A swatch uses the same control with its fill shown instead.
    /// </summary>
    private sealed class Glyph : Control
    {
        /// <summary>The box a mark is drawn in, centred in whatever the button gives it.</summary>
        private const double Side = 18;

        private static readonly IBrush Ink = Skin.LabelBrush;
        private static readonly IBrush Soft = new SolidColorBrush(Colors.White, 0.85);

        public Tool Kind { get; set; }
        public bool Erasing { get; set; }

        /// <summary>What the swatches fill with, when this is a swatch rather than a tool.</summary>
        public IBrush? Fill { get; set; }

        /// <summary>
        /// A drawn control has no size of its own, and a content presenter hands it none unless it is told to
        /// fill: without this the mark is a control of no size and draws nothing.
        /// </summary>
        public Glyph()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        public override void Render(DrawingContext context)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (Fill is { } fill)
            {
                context.FillRectangle(fill, new Rect(size));
                return;
            }
            var ox = (size.Width - Side) / 2;
            var oy = (size.Height - Side) / 2;
            Point At(double x, double y) => new(ox + x * Side / 22, oy + y * Side / 22);
            var pen = new Pen(Ink, 1.4) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var thin = new Pen(Ink, 1.2) { LineCap = PenLineCap.Round };
            var dashed = new Pen(Ink, 1.2) { DashStyle = new DashStyle([2, 2], 0) };
            void Line(double x1, double y1, double x2, double y2) =>
                context.DrawLine(pen, At(x1, y1), At(x2, y2));

            switch (Kind)
            {
                case Tool.Zoom:
                    context.DrawEllipse(null, pen, At(9, 9), 5 * Side / 22, 5 * Side / 22);
                    Line(13, 13, 19, 19);
                    break;
                case Tool.Pan:
                    Outline("M 6,12 L 6,5 C 6,3 9,3 9,5 L 9,10 L 9,3 C 9,1 12,1 12,3 L 12,10 L 12,4 C 12,2 15,2 15,4 L 15,11 L 15,7 C 15,5 18,5 18,7 L 18,14 C 18,18 15,20 12,20 L 10,20 C 8,20 7,19 6,18 L 3,13 C 2,11 4,10 6,12 Z");
                    break;
                case Tool.Move:
                    Line(4, 4, 9, 9); Line(4, 4, 4, 8); Line(4, 4, 8, 4);
                    Line(18, 18, 13, 13); Line(18, 18, 18, 14); Line(18, 18, 14, 18);
                    break;
                case Tool.Marquee:
                    context.DrawRectangle(null, dashed, new Rect(At(4, 5), At(18, 17)));
                    break;
                case Tool.Ellipse:
                    context.DrawEllipse(null, dashed, At(11, 11), 7, 5.5);
                    break;
                case Tool.Lasso:
                    context.DrawEllipse(null, pen, At(11, 10), 6, 5);
                    Line(7, 14, 4, 18);
                    break;
                case Tool.Polygon:
                    context.DrawGeometry(null, pen, Path([(4, 17), (8, 5), (16, 5), (18, 16)], At, close: true));
                    break;
                case Tool.Wand:
                    Line(4, 18, 13, 9);
                    Line(16, 3, 16, 9);
                    Line(13, 6, 19, 6);
                    break;
                case Tool.Brush:
                    if (Erasing)
                    {
                        Outline("M 3,14 L 12,4 Q 14,2 16,4 L 19,7 Q 21,9 19,11 L 10,20 L 7,20 Z M 8,9 L 15,16");
                    }
                    else
                    {
                        Outline("M 8,14 L 16,4 C 17,2 20,4 18,6 L 11,16 Z M 8,14 C 4,13 6,18 3,19 C 7,21 11,19 11,16");
                    }
                    break;
                case Tool.Clone:
                    context.DrawEllipse(Soft, null, At(11, 3.7), 3, 2.7);
                    context.DrawRectangle(Soft, null, new Rect(At(9.5, 6), At(12.5, 12.3)));
                    context.DrawRectangle(Soft, null, new Rect(At(2.6, 11.9), At(19.4, 16.7)), 1.4, 1.4);
                    context.DrawRectangle(Soft, null, new Rect(At(1.3, 18), At(20.7, 20.7)));
                    break;
                case Tool.Blur:
                    Outline("M 11,3 C 10,6 5,11 5,14 C 5,22 17,22 17,14 C 17,11 12,6 11,3 Z");
                    break;
                case Tool.Liquify:
                    Line(3, 13, 7, 9);
                    Line(7, 9, 11, 13);
                    Line(11, 13, 15, 9);
                    Line(15, 9, 19, 13);
                    break;
                case Tool.Smudge:
                    Line(3, 16, 8, 10);
                    Line(8, 10, 13, 16);
                    Line(13, 16, 17, 10);
                    context.DrawEllipse(Soft, null, At(18, 9), 2.4, 2.4);
                    break;
                case Tool.Heal:
                    Outline("M 3,13 L 13,3 C 17,-1 23,5 19,9 L 9,19 C 5,23 -1,17 3,13 Z M 7,9 L 13,15 M 9,7 L 15,13");
                    context.DrawEllipse(Soft, null, At(5, 15), 0.7, 0.7);
                    context.DrawEllipse(Soft, null, At(15, 5), 0.7, 0.7);
                    break;
                case Tool.Eyedropper:
                    Line(6, 18, 15, 9);
                    context.DrawGeometry(Soft, null, Path([(3, 19), (4, 15), (7, 18)], At, close: true));
                    Line(12, 6, 16, 10);
                    break;
                case Tool.Type:
                    Line(5, 5, 17, 5);
                    Line(11, 5, 11, 19);
                    break;
                case Tool.Crop:
                    Line(4, 7, 16, 7);
                    Line(16, 7, 16, 19);
                    Line(7, 4, 7, 16);
                    Line(7, 16, 19, 16);
                    break;
                case Tool.Shape:
                    context.DrawRectangle(null, pen, new Rect(At(4, 4), At(14, 14)));
                    context.DrawEllipse(null, pen, At(13, 13), 5, 5);
                    break;
                case Tool.Gradient:
                    context.DrawRectangle(null, pen, new Rect(At(4, 6), At(18, 16)));
                    for (var step = 0; step < 5; step++)
                    {
                        context.FillRectangle(new SolidColorBrush(Colors.White, 0.15 + step * 0.2),
                            new Rect(At(5 + step * 2.6, 7), At(6.6 + step * 2.6, 15)));
                    }
                    break;
            }

            void Outline(string data)
            {
                using var shifted = context.PushTransform(Matrix.CreateScale(Side / 22, Side / 22) * Matrix.CreateTranslation(ox, oy));
                context.DrawGeometry(null, new Pen(Ink, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), StreamGeometry.Parse(data));
            }
        }

        /// <summary>A closed or open path through points given in the mark's own box.</summary>
        private static StreamGeometry Path((double X, double Y)[] points, Func<double, double, Point> at, bool close)
        {
            var geometry = new StreamGeometry();
            using var path = geometry.Open();
            path.BeginFigure(at(points[0].X, points[0].Y), close);
            for (var index = 1; index < points.Length; index++) path.LineTo(at(points[index].X, points[index].Y));
            path.EndFigure(close);
            return geometry;
        }
    }
}
