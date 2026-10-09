using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

/// <summary>A reusable Mac-style layer cell. Selecting a target changes its border, never its raster.</summary>
internal sealed class LayerCard : Grid
{
    private readonly Target _pixels = new(false);
    private readonly Target _mask = new(true);
    private readonly TextBlock _name = new() { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _details = new() { FontSize = 10, Foreground = Skin.SecondaryBrush, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Eye _eye = new();
    private readonly Mark _disclosure = new();
    private readonly Mark _link = new() { Kind = "link" };
    private int _indent;
    private int _maskGap;
    private int _maskWidth;
    private string _columns = "";
    internal Button EyeButton { get; }
    internal Button DisclosureButton { get; }
    internal Button PixelsButton { get; }
    internal Button MaskButton { get; }
    internal Button LinkButton { get; }
    internal int RasterBuilds => _pixels.RasterBuilds + _mask.RasterBuilds;
    internal string Details => _details.Text ?? "";
    internal Size PixelPictureSize => _pixels.PictureSize;

    internal LayerCard(Action visibility, Action expansion, Action<bool, KeyModifiers> select, Action link)
    {
        Height = 52;
        ClipToBounds = true;
        Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(80), Easing = new SplineEasing(0, 0, 0.58, 1) } };
        _disclosure.RenderTransform = new RotateTransform { Angle = 90, Transitions = new Transitions
        { new DoubleTransition { Property = RotateTransform.AngleProperty, Duration = TimeSpan.FromMilliseconds(140),
            Easing = new SplineEasing(0, 0, 0.58, 1) } } };
        SizeChanged += (_, _) => PlaceColumns();
        EyeButton = Button(_eye, 20, 32, visibility);
        DisclosureButton = Button(_disclosure, 16, 24, expansion);
        PixelsButton = Button(_pixels, 36, 36, null);
        MaskButton = Button(_mask, 30, 30, null);
        LinkButton = Button(_link, 9, 20, link);
        void Target(Button button, bool mask)
        {
            var keys = KeyModifiers.None;
            button.AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!args.GetCurrentPoint(button).Properties.IsLeftButtonPressed) return;
                // Button handles the bubbling press. Remember modifiers before its class handler runs.
                keys = args.KeyModifiers;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            button.Click += (_, _) => { var pressedKeys = keys; keys = KeyModifiers.None; select(mask, pressedKeys); };
        }
        Target(PixelsButton, false); Target(MaskButton, true);
        Grid.SetColumn(DisclosureButton, 1); Grid.SetColumn(PixelsButton, 2);
        Grid.SetColumn(LinkButton, 3); Grid.SetColumn(MaskButton, 4);
        var words = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 9, 0, 0), Children = { _name, _details } };
        Grid.SetColumn(words, 6);
        Children.Add(EyeButton); Children.Add(DisclosureButton); Children.Add(PixelsButton);
        Children.Add(LinkButton); Children.Add(MaskButton); Children.Add(words);
    }

    internal void Configure(CanvasDocument document, ImageLayer layer, int depth, bool visible, bool collapsed)
    {
        var clipped = layer.MaskSourceID is not null;
        var linkable = layer.Mask is not null && !layer.IsGroup && layer.Adjustment is null;
        var indent = Math.Min(depth, 8) * 24 + (clipped ? 24 : 0);
        // NativeLayerList: disclosure overlaps its following slot by two points; masks add 30 + a 13-point link slot.
        _indent = indent; _maskGap = layer.Mask is null ? 0 : linkable ? 13 : 5; _maskWidth = layer.Mask is null ? 0 : 30;
        PlaceColumns();
        DisclosureButton.Margin = new Thickness(0, 0, -2, 0);
        DisclosureButton.IsVisible = layer.IsGroup;
        _disclosure.Kind = "right";
        ((RotateTransform)_disclosure.RenderTransform!).Angle = collapsed ? 0 : 90;
        _eye.Shown = layer.IsVisible; _eye.InvalidateVisual();
        UiText.Set(EyeButton, ToolTip.TipProperty, layer.IsVisible ? "Hide Layer" : "Show Layer");
        UiText.Set(DisclosureButton, ToolTip.TipProperty, collapsed ? "Expand folder" : "Collapse folder");
        _pixels.Configure(document, layer);
        _mask.Configure(document, layer);
        MaskButton.IsVisible = layer.Mask is not null;
        LinkButton.IsVisible = linkable;
        _link.Shown = layer.Mask?.IsLinked == true; _link.InvalidateVisual();
        UiText.Set(LinkButton, ToolTip.TipProperty, layer.Mask?.IsLinked == false ? "Link mask" : "Unlink mask");
        UiText.Set(PixelsButton, ToolTip.TipProperty, "Select image pixels; Ctrl-click loads its selection");
        UiText.Set(MaskButton, ToolTip.TipProperty, "Select mask; Shift-click enables or disables; Ctrl-click selects its black areas");
        _name.Text = (clipped ? "↳ " : "") + layer.Name;
        ToolTip.SetTip(_name, layer.Name);
        _details.Text = clipped ? UiText.Format("Clipped to {0}", document.Layers.FirstOrDefault(item => item.ID == layer.MaskSourceID)?.Name ?? UiText.Get("Missing source"))
            : layer.LiveText is not null ? UiText.Get("Text · Double-click to edit")
            : layer.Adjustment is not null ? UiText.Get("Adjustment · Double-click to edit")
            : layer.IsGroup ? UiText.Get("Folder") : $"{layer.Transform.Width:0} × {layer.Transform.Height:0} px";
        ToolTip.SetTip(_details, _details.Text);
        Opacity = visible ? 1 : 0.35;
    }

    internal void ShowTarget(bool active, bool mask)
    {
        _pixels.Active = active && !mask; _mask.Active = active && mask;
        _pixels.InvalidateVisual(); _mask.InvalidateVisual();
    }

    private void PlaceColumns()
    {
        // At deep nesting, compact indentation before any control or the name would be pushed out of the panel.
        var room = Bounds.Width > 0 ? Bounds.Width : 236;
        var indent = Math.Min(_indent, Math.Max(0, room - (20 + 14 + 36 + _maskGap + _maskWidth + 5 + 40)));
        var columns = FormattableString.Invariant($"20,{14 + indent},36,{_maskGap},{_maskWidth},5,*");
        if (_columns == columns) return;
        _columns = columns; ColumnDefinitions = new ColumnDefinitions(columns);
    }

    private static Button Button(Control content, double width, double height, Action? action)
    {
        var button = new Button { Classes = { "plain" }, Content = content, Width = width, Height = height,
            MinHeight = height, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    private sealed class Eye : Control
    {
        internal bool Shown { get; set; }
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(Skin.SecondaryBrush, 1.2);
            var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
            context.DrawEllipse(null, pen, center, 7, 4);
            context.DrawEllipse(Skin.SecondaryBrush, null, center, 2, 2);
            if (!Shown) context.DrawLine(pen, new Point(center.X - 6, center.Y - 6), new Point(center.X + 6, center.Y + 6));
        }
    }

    private sealed class Mark : Control
    {
        internal string Kind { get; set; } = "down";
        internal bool Shown { get; set; } = true;
        public override void Render(DrawingContext context)
        {
            if (!Shown) return;
            var pen = new Pen(Skin.SecondaryBrush, 1.15, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var x = Bounds.Width / 2; var y = Bounds.Height / 2;
            if (Kind == "link")
            {
                context.DrawEllipse(null, pen, new Point(x, y - 2), 2, 3);
                context.DrawEllipse(null, pen, new Point(x, y + 2), 2, 3);
            }
            else if (Kind == "down")
            {
                context.DrawLine(pen, new Point(x - 3, y - 2), new Point(x, y + 1));
                context.DrawLine(pen, new Point(x, y + 1), new Point(x + 3, y - 2));
            }
            else
            {
                context.DrawLine(pen, new Point(x - 1, y - 3), new Point(x + 2, y));
                context.DrawLine(pen, new Point(x + 2, y), new Point(x - 1, y + 3));
            }
        }
    }

    private sealed class Target : Control
    {
        private readonly bool mask;
        private static readonly StyledProperty<double> SelectionAmountProperty = AvaloniaProperty.Register<Target, double>("SelectionAmount");
        private bool _active;
        static Target() => AffectsRender<Target>(SelectionAmountProperty);
        internal Target(bool isMask)
        {
            mask = isMask;
            Transitions = new Transitions { new DoubleTransition { Property = SelectionAmountProperty,
                Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, 0.58, 1) } };
        }
        private Bitmap? _image;
        private (SKBitmap? Image, LayerTransform Transform, int Width, int Height, string Icon)? _key;
        private CanvasDocument? _document;
        private ImageLayer? _layer;
        internal Size PictureSize { get; private set; } = new(36, 36);
        internal int RasterBuilds { get; private set; }
        internal bool Active
        {
            get => _active;
            set { if (_active == value) return; _active = value; SetValue(SelectionAmountProperty, value ? 1 : 0); }
        }
        private string _icon = "";
        internal void Configure(CanvasDocument document, ImageLayer layer)
        {
            _document = document; _layer = layer;
            _icon = mask ? "" : layer.IsGroup ? "folder" : layer.LiveText is not null ? "text" : layer.Adjustment is not null ? "adjustment" : "";
            var box = mask ? 30.0 : 36.0;
            PictureSize = _icon.Length > 0 ? new Size(box, box)
                : new Size(Math.Max(1, Math.Round(document.Width * box / Math.Max(document.Width, document.Height), MidpointRounding.AwayFromZero)),
                    Math.Max(1, Math.Round(document.Height * box / Math.Max(document.Width, document.Height), MidpointRounding.AwayFromZero)));
            var key = (mask ? layer.Mask?.Asset.Thumbnail : layer.Asset?.Thumbnail,
                mask ? layer.MaskTransform : layer.Transform, document.Width, document.Height, _icon);
            if (_image is not null && _key == key) { InvalidateVisual(); return; }
            _key = key; _image?.Dispose(); _image = null;
            if (_icon.Length > 0 || (mask && layer.Mask is null)) { InvalidateVisual(); return; }
            var width = (int)PictureSize.Width * 2; var height = (int)PictureSize.Height * 2;
            using var raster = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(raster))
            {
                if (mask) canvas.Clear(MaskBackground(key.Item1));
                else
                {
                    canvas.Clear(new SKColor(56, 56, 56));
                    using var check = new SKPaint { Color = new SKColor(82, 82, 82) };
                    for (var y = 0; y < height; y += 12)
                        for (var x = 0; x < width; x += 12)
                            if ((x / 12 + y / 12) % 2 == 0) canvas.DrawRect(x, y, 12, 12, check);
                }
                if (key.Item1 is { } pixels)
                {
                    var placement = key.Item2;
                    canvas.Scale((float)width / document.Width);
                    canvas.Translate((float)placement.CenterX, (float)placement.CenterY);
                    canvas.RotateDegrees((float)placement.Rotation);
                    canvas.Scale(placement.FlipX ? -1 : 1, placement.FlipY ? -1 : 1);
                    using var image = SKImage.FromBitmap(pixels);
                    using var paint = new SKPaint { IsAntialias = true };
                    canvas.DrawImage(image, SKRect.Create((float)-placement.Width / 2, (float)-placement.Height / 2,
                        (float)placement.Width, (float)placement.Height), new SKSamplingOptions(SKFilterMode.Linear), paint);
                }
            }
            using var png = raster.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = png.AsStream(); _image = new Bitmap(stream); RasterBuilds++;
            InvalidateVisual();
        }
        private static SKColor MaskBackground(SKBitmap? image)
        {
            if (image is null) return SKColors.White;
            long sum = 0; var count = 0;
            for (var y = 0; y < image.Height; y++)
                for (var x = 0; x < image.Width; x++)
                    if (x == 0 || y == 0 || x == image.Width - 1 || y == image.Height - 1)
                    { sum += image.GetPixel(x, y).Red; count++; }
            return sum * 2 >= count * 255L ? SKColors.White : SKColors.Black;
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs args)
        {
            base.OnAttachedToVisualTree(args);
            if (_image is null && _document is not null && _layer is not null) Configure(_document, _layer);
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs args)
        {
            _image?.Dispose(); _image = null;
            base.OnDetachedFromVisualTree(args);
        }
        public override void Render(DrawingContext context)
        {
            var rect = new Rect(new Point((Bounds.Width - PictureSize.Width) / 2, (Bounds.Height - PictureSize.Height) / 2), PictureSize);
            if (_image is { } image)
            {
                using (context.PushClip(new RoundedRect(rect, 3))) context.DrawImage(image, rect);
            }
            else if (_icon == "text")
            {
                var text = new FormattedText("T", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default), 23, Skin.LabelBrush);
                context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
            }
            else if (_icon == "folder")
            {
                var outline = StreamGeometry.Parse("M 5,12 L 5,9 Q 5,7 7,7 L 14,7 L 17,10 L 28,10 Q 30,10 30,12 L 30,27 Q 30,29 28,29 L 7,29 Q 5,29 5,27 Z");
                context.DrawGeometry(null, new Pen(Skin.LabelBrush, 1.3), outline);
            }
            else if (_icon == "adjustment")
            {
                context.DrawEllipse(null, new Pen(Skin.LabelBrush, 1.3), new Point(18, 18), 9, 9);
                using (context.PushClip(new Rect(9, 9, 9, 18))) context.DrawEllipse(Skin.LabelBrush, null, new Point(18, 18), 9, 9);
            }
            if (GetValue(SelectionAmountProperty) is > 0 and var amount)
                using (context.PushOpacity(amount)) context.DrawRectangle(null, new Pen(Skin.AccentBrush, 2), rect.Deflate(1), 3, 3);
            if (mask && _layer?.Mask?.IsEnabled == false)
            {
                var red = new Pen(new SolidColorBrush(Color.FromRgb(255, 69, 58)), 2);
                context.DrawLine(red, rect.TopLeft, rect.BottomRight); context.DrawLine(red, rect.TopRight, rect.BottomLeft);
            }
        }
    }
}
