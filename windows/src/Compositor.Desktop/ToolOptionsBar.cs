using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Document;
using Compositor.Core.Format;

namespace Compositor.Desktop;

/// <summary>Which of the magic wand's amounts was asked for.</summary>
internal enum WandSetting
{
    Tolerance,
    SampleSize,
}

/// <summary>
/// The strip of options above the canvas, as the Mac keeps above its own: what the tool in hand can be told.
/// Its inline fields, sliders and segmented pickers edit the same settings as the Tools menu.
/// </summary>
internal sealed partial class ToolOptionsBar : Border
{
    /// <summary>How tall the strip is, which is the Mac's own tool header.</summary>
    private const double StripHeight = 42;

    private readonly ToolOptions _options;
    private readonly StableCaption _title = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
        FontSize = 13,
        Margin = new Thickness(0, 0, 12, 0),
    };
    private readonly TextBlock _zoom = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _cells = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    internal ScrollViewer Overflow { get; } = new()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
    };

    /// <summary>Every row, by the name of the tools it belongs to: a name may cover several controls.</summary>
    private readonly Dictionary<string, List<Control>> _named = [];

    /// <summary>A setting was changed by the bar, so the window pushes it to the canvas and says what it is.</summary>
    public event Action? Changed;

    /// <summary>One of the magic wand's amounts was asked for.</summary>
    public event Action<WandSetting>? WandSettingAsked;

    /// <summary>A colour swatch was clicked: true for the foreground, false for the gradient's background.</summary>
    public event Action<bool>? ColourAsked;

    /// <summary>A layer is to be flipped, across its own middle, one way or the other.</summary>
    public event Action<bool>? FlipAsked;

    /// <summary>Which of the crop ratios was chosen, by its place in the window's own list.</summary>
    public event Action<int>? CropRatioChosen;
    public event Action? CropApplied;
    public event Action? CropCancelled;

    /// <summary>The Type tool's text is to be edited.</summary>
    public event Action? TextAsked;
    public event Action? BrushesAsked;
    public event Action? ShapesAsked;
    public event Action? GradientsAsked;

    public ToolOptionsBar(ToolOptions options)
    {
        _options = options;
        Height = StripHeight;
        Background = Skin.ChromeBrush;
        Padding = new Thickness(18, 0, 18, 0);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        row.Children.Add(_title);
        Grid.SetColumn(_fixedTransformToggles, 1);
        row.Children.Add(_fixedTransformToggles);
        Overflow.Content = _cells;
        Grid.SetColumn(Overflow, 2);
        row.Children.Add(Overflow);
        Grid.SetColumn(_pendingTransformActions, 3);
        row.Children.Add(_pendingTransformActions);
        Child = row;
        Build();
        LayoutUpdated += (_, _) => FitSliders();
    }

    /// <summary>
    /// Shows the rows the tool in hand has. Called when the tool or the panel selection changes and never on a
    /// repaint: a row rebuilt under the pointer's own drag would drop the drag.
    /// </summary>
    public void Show(Tool tool, bool hasDocument, bool maskSelected)
    {
        var brush = tool is Tool.Brush or Tool.Clone or Tool.Blur or Tool.Liquify or Tool.Smudge or Tool.Heal;
        _loading = true;
        try
        {
            On("brush", brush && hasDocument);
            On("mode", tool == Tool.Brush);
            _hardness.IsEnabled = tool != Tool.Brush || _options.Brush.Tip is null;
            On("smoothing", tool == Tool.Brush);
            On("smear", tool is Tool.Blur or Tool.Liquify or Tool.Smudge);
            On("brushColor", brush && tool is not (Tool.Clone or Tool.Blur or Tool.Liquify or Tool.Smudge) && !maskSelected);
            On("heal", tool == Tool.Heal);
            On("clone", tool == Tool.Clone);
            On("mask", tool == Tool.Brush && maskSelected);
            On("blur", tool == Tool.Blur);
            On("marqueeShape", tool is Tool.Marquee or Tool.Ellipse);
            On("lasso", tool is Tool.Lasso or Tool.Polygon);
            On("eye", tool == Tool.Eyedropper);
            On("wand", tool == Tool.Wand);
                On("gradient", tool == Tool.Gradient);
            On("shape", tool == Tool.Shape);
            On("corner", tool == Tool.Shape && _options.Shape == ShapeKind.Rectangle);
            On("linewidth", tool == Tool.Shape && _options.Shape == ShapeKind.Line);
            On("crop", tool == Tool.Crop);
            On("transform", tool == Tool.Move && hasDocument);
            On("type", tool == Tool.Type);
            On("zoom", tool is Tool.Pan or Tool.Zoom);
            _title.Variants = tool switch
            {
                Tool.Brush => ["Brush", "Eraser"],
                Tool.Marquee or Tool.Ellipse => ["Marquee", "Elliptical marquee"],
                Tool.Lasso or Tool.Polygon => ["Lasso", "Polygonal lasso"],
                Tool.Pan or Tool.Zoom => ["Pan", "Zoom"],
                _ => [],
            };
            UiText.Set(_title, TextBlock.TextProperty, tool == Tool.Brush && _options.Erase ? "Eraser" : Names.TryGetValue(tool, out var name) ? name : "");
            // The marquee's shape and the lasso's kind *are* the tool in hand, so the bar follows the tool
            // rather than the other way round: picking one here asks for the tool the window already has.
            _marqueeShape.SelectedIndex = tool == Tool.Ellipse ? 1 : 0;
            _lassoKind.SelectedIndex = tool == Tool.Polygon ? 1 : 0;
            _smearMode.SelectedIndex = tool == Tool.Blur ? 1 : tool == Tool.Smudge ? 2 : 0;
            _opacity.SetLabel(tool is Tool.Blur or Tool.Liquify or Tool.Smudge ? "Strength" : "Opacity");
            Refresh();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The zoom the window is showing, for the Pan and Zoom rows.</summary>
    public void ShowZoom(double percent) => _zoom.Text = UiText.Format("Zoom {0:0}%", percent);

    /// <summary>Whether a row is on show, which is what the self check reads to see the gating works.</summary>
    internal bool Shows(string name) => _named.TryGetValue(name, out var cells) && cells[0].IsVisible;

    /// <summary>
    /// The Anti-alias tick as a press on it would leave it, for the check: a tick's own write goes through the
    /// same Set every other control's does, so this drives the whole path rather than reaching past it.
    /// </summary>
    internal void PressAntialias(bool on) => _antialias.IsChecked = on;

    /// <summary>The names of the rows on show, in the order they were built.</summary>
    internal IEnumerable<string> Showing => _named.Where(entry => entry.Value[0].IsVisible).Select(entry => entry.Key);

    /// <summary>Every row's value read back off its own control, which is what the bar is showing.</summary>
    private void Refresh()
    {
        _size.Value = _options.Brush.Diameter;
        _hardness.Value = _options.Brush.Hardness * 100;
        _opacity.Value = _options.Brush.Opacity * 100;
        _blurRadius.Value = _options.Brush.BlurRadius;
        _smoothing.Value = _options.Brush.Smoothing * 100;
        _tolerance.Content = UiText.Format("Tolerance {0}", _options.Wand.Tolerance);
        _sampleSize.Content = UiText.Format("Sample {0}", _options.Wand.Radius);
        _corner.Value = _options.ShapeCornerRadius;
        _lineWidth.Value = _options.ShapeLineWidth;
        _fill.Show(_options.Brush.Red, _options.Brush.Green, _options.Brush.Blue);
        _shapeFill.Show(_options.Brush.Red, _options.Brush.Green, _options.Brush.Blue);
        _gradientFill.Show(_options.GradientBackground.Red, _options.GradientBackground.Green,
            _options.GradientBackground.Blue);
        _brushMode.SelectedIndex = _options.Erase ? 1 : 0;
        _maskPaint.SelectedIndex = _options.MaskPaintWhite ? 1 : 0;
        _healMode.SelectedIndex = (int)_options.Brush.Healing;
        _aligned.IsChecked = _options.Brush.CloneAligned;
        _cloneAll.SelectedIndex = _options.Brush.CloneAllLayers ? 1 : 0;
        _contiguous.IsChecked = _options.Wand.Contiguous;
        _antialias.IsChecked = _options.SelectionAntialiased;
        _sampleRing.IsChecked = _options.ShowsSampleRing;
        _wandAll.SelectedIndex = _options.WandAllLayers ? 1 : 0;
        _shapeKind.SelectedIndex = Math.Min(3,(int)_options.Shape);
        var tip = _options.Brush.Tip;
        var name = tip?.Name ?? (_options.Brush.Hardness == 0 ? "Soft Round" : "Hard Round");
        _brushPreview.Show(new BrushStyle(name,tip,tip is null ? _options.Brush.Hardness : null),62,22);
        ToolTip.SetTip(_brushes,UiText.Get("Brushes")+": "+UiText.Get(name));
        _shapePreview.Show(_options.CustomShape ?? new ShapePreset(_options.Shape.ToString(),_options.Shape),32,18);
        var from = new SkiaSharp.SKColor((byte)(_options.Brush.Red*255),(byte)(_options.Brush.Green*255),(byte)(_options.Brush.Blue*255));
        var to = _options.GradientToBackground ? new SkiaSharp.SKColor((byte)(_options.GradientBackground.Red*255),(byte)(_options.GradientBackground.Green*255),(byte)(_options.GradientBackground.Blue*255)) : from.WithAlpha(0);
        var gradient = _options.GradientPreset ?? new GradientPreset("Foreground", [new(0,(uint)from),new(1,(uint)to)]);
        if (_options.GradientReversed) gradient=gradient.Reversed();
        _gradientPreview.Show(gradient,62,20);
        _gradientTo.IsEnabled = _gradientFill.IsEnabled = _options.GradientPreset is null;
        ToolTip.SetTip(_gradients,UiText.Get("Gradient presets…")+": "+UiText.Get(gradient.Name));
        _gradientKind.SelectedIndex = (int)_options.Gradient;
        _gradientTo.SelectedIndex = _options.GradientToBackground ? 1 : 0;
        _gradientReversed.IsChecked = _options.GradientReversed;
    }

    /// <summary>Whether the marquee is drawing an ellipse, which is the tool in hand rather than a setting.</summary>
    private bool _marqueeEllipse;

    /// <summary>The bar told which shape the marquee is drawing, since that is the tool and not a setting.</summary>
    public void ShowMarquee(bool ellipse)
    {
        _loading = true;
        try
        {
            _marqueeEllipse = ellipse;
            _marqueeShape.SelectedIndex = ellipse ? 1 : 0;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>The marquee's shape was picked from the bar, which is asking for the tool that draws it.</summary>
    public event Action<bool>? MarqueeShapeChosen;

    /// <summary>The lasso's kind was picked from the bar, which is the same as picking the tool.</summary>
    public event Action<bool>? LassoKindChosen;
    public event Action<Tool>? SmearKindChosen;
    internal InlineNumber BrushSize => _size;
    internal InlineNumber BrushSmoothing => _smoothing;
    internal ComboBox MaskPaintChoice => _maskPaint;
    internal SegmentedChoice BrushModeChoice => _brushMode;
    internal SegmentedChoice ShapeChoice => _shapeKind;
    internal Button BrushesButton => _brushes;
    internal Button GradientsButton => _gradients;
    internal MaterialPreview BrushPreview => _brushPreview;
    internal MaterialPreview GradientPreview => _gradientPreview;
    internal TextBlock TitleLabel => _title;
    internal IReadOnlyList<Control> CellsFor(string name) => _named.TryGetValue(name, out var cells) ? cells : [];


    private readonly InlineNumber _size = new("Size", 1, 2000, unit: "px", fieldWidth: 48, slider: true, adaptiveSlider: true, logarithmicSlider: true);
    private readonly InlineNumber _hardness = new("Hardness", 0, 100, unit: "%", slider: true, adaptiveSlider: true);
    private readonly InlineNumber _opacity = new("Opacity", 1, 100, unit: "%", slider: true, alternateLabels: ["Opacity", "Strength"], adaptiveSlider: true);
    private readonly InlineNumber _blurRadius = new("Radius", 0.5, 50, step: 0.1, unit: "px", slider: true, sliderMaximum: 20, adaptiveSlider: true);
    private readonly InlineNumber _smoothing = new("Smoothing", 0, 100, slider: true, adaptiveSlider: true);
    private readonly Button _tolerance = new StableCaptionButton { AlternateCaptions = () =>
        [UiText.Format("Tolerance {0}", 255), UiText.Format("Tolerance {0}", 188)] };
    private readonly Button _sampleSize = new StableCaptionButton { AlternateCaptions = () =>
        [UiText.Format("Sample {0}", 100), UiText.Format("Sample {0}", 88)] };
    private readonly InlineNumber _corner = new("Radius", 0, 5000, unit: "px", fieldWidth: 48, slider: true,
        sliderMaximum: 200, adaptiveSlider: true, alternateLabels: ["Radius", "Width"]);
    private readonly InlineNumber _lineWidth = new("Width", 1, 5000, unit: "px", fieldWidth: 48, slider: true,
        sliderMaximum: 100, adaptiveSlider: true, alternateLabels: ["Radius", "Width"]);
    private readonly Swatch _fill = new();
    private readonly Swatch _shapeFill = new();
    private readonly Swatch _gradientFill = new();
    private readonly SegmentedChoice _brushMode = new("Paint", "Erase");
    private readonly MaterialPreview _brushPreview = new() { Width=62,Height=22 };
    private readonly Button _brushes = new() { Width=80,Height=26,Padding=new Thickness(5,1) };
    private readonly MaterialPreview _shapePreview = new() { Width=32,Height=18 };
    private readonly MaterialPreview _gradientPreview = new() { Width=62,Height=20,CornerRadius=10 };
    private readonly Button _gradients = new() { Width=80,Height=26,Padding=new Thickness(5,1) };
    private readonly SegmentedChoice _smearMode = new("Liquify", "Blur", "Smudge");
    private readonly ComboBox _maskPaint = new();
    private readonly SegmentedChoice _healMode = new("Content-Aware", "Create Texture", "Proximity Match");
    private readonly CheckBox _aligned = new() { [!ContentControl.ContentProperty] = UiText.Bind("Aligned") };
    private readonly SegmentedChoice _cloneAll = new("This Layer", "All Layers");
    private readonly ComboBox _marqueeShape = new StableChoice();
    private readonly ComboBox _lassoKind = new StableChoice();
    private readonly CheckBox _contiguous = new() { [!ContentControl.ContentProperty] = UiText.Bind("Contiguous") };
    private readonly CheckBox _antialias = new() { [!ContentControl.ContentProperty] = UiText.Bind("Anti-alias") };
    private readonly CheckBox _sampleRing = new() { [!ContentControl.ContentProperty] = UiText.Bind("Sample Ring") };
    private readonly ComboBox _wandAll = new StableChoice();
    private readonly SegmentedChoice _shapeKind = new("Rectangle", "Ellipse", "Line", "More…");
    private readonly ComboBox _gradientKind = new StableChoice();
    private readonly ComboBox _gradientTo = new StableChoice();
    private readonly CheckBox _gradientReversed = new() { [!ContentControl.ContentProperty] = UiText.Bind("Reverse") };
    private readonly ComboBox _cropRatio = new() { Width = 150 };
    private readonly Button _cropApply = new() { [!ContentControl.ContentProperty] = UiText.Bind("Apply Crop") };
    private readonly Button _cropCancel = new() { [!ContentControl.ContentProperty] = UiText.Bind("Cancel") };
    private readonly Button _flipH = new() { [!ContentControl.ContentProperty] = UiText.Bind("Flip H") };
    private readonly Button _flipV = new() { [!ContentControl.ContentProperty] = UiText.Bind("Flip V") };
    private readonly Button _editText = new() { [!ContentControl.ContentProperty] = UiText.Bind("Edit Text…") };

    /// <summary>What each tool's strip is called, which is the Mac's own title.</summary>
    private static readonly Dictionary<Tool, string> Names = new()
    {
        [Tool.Pan] = "Pan",
        [Tool.Zoom] = "Zoom",
        [Tool.Move] = "Transform",
        [Tool.Marquee] = "Marquee",
        [Tool.Ellipse] = "Elliptical marquee",
        [Tool.Lasso] = "Lasso",
        [Tool.Polygon] = "Polygonal lasso",
        [Tool.Wand] = "Magic wand",
        [Tool.Brush] = "Brush",
        [Tool.Clone] = "Clone stamp",
        [Tool.Blur] = "Smear",
        [Tool.Liquify] = "Smear",
        [Tool.Smudge] = "Smear",
        [Tool.Heal] = "Spot healing",
        [Tool.Eyedropper] = "Eyedropper",
        [Tool.Type] = "Type",
        [Tool.Crop] = "Crop",
        [Tool.Shape] = "Shape",
        [Tool.Gradient] = "Gradient",
    };

    private void Build()
    {
        _size.Changed += value => Set(ref _options.Brush, _options.Brush with { Diameter = value });
        _hardness.Changed += value => Set(ref _options.Brush, _options.Brush with { Hardness = value / 100 });
        _opacity.Changed += value => Set(ref _options.Brush, _options.Brush with { Opacity = value / 100 });
        _blurRadius.Changed += value => Set(ref _options.Brush, _options.Brush with { BlurRadius = value });
        _smoothing.Changed += value => Set(ref _options.Brush, _options.Brush with { Smoothing = value / 100 });
        foreach (var (setting, button) in new (WandSetting, Button)[]
                 {
                     (WandSetting.Tolerance, _tolerance), (WandSetting.SampleSize, _sampleSize),
                 })
        {
            var which = setting;
            button.Click += (_, _) => WandSettingAsked?.Invoke(which);
        }
        _corner.Changed += value => Set(ref _options.ShapeCornerRadius, value);
        _lineWidth.Changed += value => Set(ref _options.ShapeLineWidth, value);
        _fill.Click += (_, _) => ColourAsked?.Invoke(true);
        _shapeFill.Click += (_, _) => ColourAsked?.Invoke(true);
        _gradientFill.Click += (_, _) => ColourAsked?.Invoke(false);

        _brushMode.SelectedIndex = 0;
        _brushMode.Changed += index => Set(ref _options.Erase, index == 1);
        _smearMode.Changed += index => SmearKindChosen?.Invoke(index == 1 ? Tool.Blur : index == 2 ? Tool.Smudge : Tool.Liquify);
        _maskPaint.ItemsSource = new[] { "Paint Black · Hide", "Paint White · Reveal" };
        _maskPaint.SelectedIndex = 0;
        _maskPaint.Width = 180;
        _maskPaint.SelectionChanged += (_, _) => Set(ref _options.MaskPaintWhite, _maskPaint.SelectedIndex == 1);
        _healMode.SelectedIndex = 0;
        _healMode.Changed += _ =>
        {
            var healed = _options.Brush with { Healing = (HealingMode)Math.Max(0, _healMode.SelectedIndex) };
            Set(ref _options.Brush, healed);
        };
        _aligned.IsCheckedChanged += (_, _) =>
        {
            var brush = _options.Brush with { CloneAligned = _aligned.IsChecked == true };
            Set(ref _options.Brush, brush);
        };
        _antialias.IsChecked = _options.SelectionAntialiased;
        _antialias.IsCheckedChanged += (_, _) =>
            Set(ref _options.SelectionAntialiased, _antialias.IsChecked == true);
        _sampleRing.IsChecked = _options.ShowsSampleRing;
        _sampleRing.IsCheckedChanged += (_, _) =>
            Set(ref _options.ShowsSampleRing, _sampleRing.IsChecked == true);
        _cloneAll.SelectedIndex = 0;
        _cloneAll.Changed += _ =>
        {
            var brush = _options.Brush with { CloneAllLayers = _cloneAll.SelectedIndex == 1 };
            Set(ref _options.Brush, brush);
        };

        _marqueeShape.ItemsSource = new[] { "Rectangle", "Ellipse" };
        _marqueeShape.SelectedIndex = 0;
        _marqueeShape.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _marqueeEllipse = _marqueeShape.SelectedIndex == 1;
            MarqueeShapeChosen?.Invoke(_marqueeEllipse);
        };
        _lassoKind.ItemsSource = new[] { "Freehand", "Polygonal" };
        _lassoKind.SelectedIndex = 0;
        _lassoKind.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            LassoKindChosen?.Invoke(_lassoKind.SelectedIndex == 1);
        };
        _contiguous.IsCheckedChanged += (_, _) =>
        {
            var wand = _options.Wand with { Contiguous = _contiguous.IsChecked == true };
            Set(ref _options.Wand, wand);
        };
        _wandAll.ItemsSource = new[] { "Sample: This Layer", "Sample: All Layers" };
        _wandAll.SelectedIndex = 0;
        _wandAll.SelectionChanged += (_, _) => Set(ref _options.WandAllLayers, _wandAll.SelectedIndex == 1);

        _shapeKind.SelectedIndex = 0;
        _shapeKind.Changed += index =>
        {
            if (index >= 3) return;
            _options.CustomShape = null;
            var shape = (ShapeKind)index;
            Set(ref _options.Shape, shape);
            // The corner radius belongs to a rectangle and the width to a line, so which of the two shows
            // follows the kind that was just picked.
            On("corner", shape == ShapeKind.Rectangle);
            On("linewidth", shape == ShapeKind.Line);
        };
        _shapeKind.ButtonAt(3).Content = _shapePreview;
        ToolTip.SetTip(_shapeKind.ButtonAt(3),UiText.Get("More shapes…"));
        _shapeKind.ButtonAt(3).Click += (_,_) => { if (!_loading) ShapesAsked?.Invoke(); };
        _gradientKind.ItemsSource = new[] { "Linear", "Radial", "Angle", "Reflected", "Diamond" };
        _gradientKind.SelectedIndex = 0;
        _gradientKind.SelectionChanged += (_, _) => Set(ref _options.Gradient, (GradientShape)Math.Max(0, _gradientKind.SelectedIndex));
        _gradientTo.ItemsSource = new[] { "To nothing", "To the background color" };
        _gradientTo.SelectedIndex = 0;
        _gradientTo.SelectionChanged += (_, _) => { if (!_loading) _options.GradientPreset=null; Set(ref _options.GradientToBackground, _gradientTo.SelectedIndex == 1); };
        _gradientReversed.IsCheckedChanged += (_, _) => Set(ref _options.GradientReversed, _gradientReversed.IsChecked == true);

        _cropRatio.SelectionChanged += (_, _) => CropRatioChosen?.Invoke(_cropRatio.SelectedIndex);
        _cropApply.Click += (_, _) => CropApplied?.Invoke();
        _cropCancel.Click += (_, _) => CropCancelled?.Invoke();
        _flipH.Click += (_, _) => FlipAsked?.Invoke(true);
        _flipV.Click += (_, _) => FlipAsked?.Invoke(false);
        _editText.Click += (_, _) => TextAsked?.Invoke();
        _brushes.Click += (_, _) => BrushesAsked?.Invoke();
        _brushes.Content = _brushPreview;
        _gradients.Content = _gradientPreview;
        Avalonia.Automation.AutomationProperties.SetName(_brushes,UiText.Get("Brushes"));
        Avalonia.Automation.AutomationProperties.SetName(_gradients,UiText.Get("Gradients"));
        Avalonia.Automation.AutomationProperties.SetName(_shapeKind.ButtonAt(3),UiText.Get("Shapes"));
        ToolTip.SetTip(_gradients,UiText.Get("Gradient presets…"));
        _gradients.Click += (_,_) => GradientsAsked?.Invoke();

        Cell("mode", _brushMode);
        Cell("mode", _brushes);
        Cell("smear", _smearMode);
        Cell("heal", _healMode);
        Cell("clone", _aligned);
        Cell("clone", _cloneAll);
        Cell("brush", _size);
        Cell("brush", _hardness);
        Cell("brush", _opacity);
        Cell("smoothing", _smoothing);
        Cell("brushColor", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Color"), VerticalAlignment = VerticalAlignment.Center }, _fill } });
        Cell("mask", _maskPaint);
        // The Blur brush's own Radius, which the Mac's brush controls show for that tool alone.
        Cell("blur", _blurRadius);
        Cell("marqueeShape", _marqueeShape);
        Cell("lasso", _lassoKind);
        Cell("lasso", _antialias);
        // The eyedropper's own row, which the Mac keeps in the picker's controls: the ring is the only thing that
        // tool can be told.
        Cell("eye", _sampleRing);
        Cell("wand", _tolerance);
        Cell("wand", _sampleSize);
        Cell("wand", _contiguous);
        Cell("wand", _wandAll);
        Cell("gradient", _gradientKind);
        Cell("gradient", _gradients);
        Cell("gradient", _gradientTo);
        Cell("gradient", _gradientFill);
        Cell("gradient", _gradientReversed);
        Cell("shape", _shapeKind);
        Cell("corner", _corner);
        Cell("linewidth", _lineWidth);
        Cell("shape", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Fill"), VerticalAlignment = VerticalAlignment.Center }, _shapeFill } });
        Cell("crop", _cropRatio);
        Cell("crop", _cropApply);
        Cell("crop", _cropCancel);
        BuildTransform();
        Cell("type", _editText);
        Cell("zoom", _zoom);
    }


    /// <summary>The crop ratios the window offers, so the bar's own list is the same list.</summary>
    public void ShowCropRatios(IReadOnlyList<string> ratios)
    {
        _cropRatio.ItemsSource = ratios;
        _cropRatio.SelectedIndex = 0;
    }

    /// <summary>Which ratio the crop frame is on, so the bar follows the menu and the frame follows the bar.</summary>
    public void ShowCropRatio(int index) => _cropRatio.SelectedIndex = index;

    /// <summary>One option changed by the bar: the window is told, and the values are read back.</summary>
    private void Set<T>(ref T field, T value)
    {
        // While the bar is filling itself in, a widget written by Show reads back as a change. It is not one:
        // the window is the one that asked for the filling, and answering it would be a loop.
        if (_loading) return;
        field = value;
        Refresh();
        Changed?.Invoke();
    }

    /// <summary>Whether the bar is filling itself in, which is when its own writes are not changes.</summary>
    private bool _loading;

    /// <summary>One option's place in the strip, under its tool's name, kept so Show can hide it.</summary>
    private void Cell(string name, Control control)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        _cells.Children.Add(control);
        Register(name, control);
    }

    private void Register(string name, Control control)
    {
        if (!_named.TryGetValue(name, out var cells))
        {
            cells = [];
            _named[name] = cells;
        }
        cells.Add(control);
    }

    /// <summary>Whether a row and every control in it are on show.</summary>
    private void On(string name, bool shown)
    {
        if (!_named.TryGetValue(name, out var cells)) return;
        foreach (var cell in cells) cell.IsVisible = shown;
    }

    private void FitSliders()
    {
        if (Overflow.Bounds.Width <= 0 || _cells.DesiredSize.Width <= 0) return;
        var sliders = new[] { _size, _hardness, _smoothing, _opacity, _blurRadius, _corner, _lineWidth }
            .Where(control => control.IsVisible || (ReferenceEquals(control, _blurRadius) && _smearMode.IsVisible)).ToArray();
        if (sliders.Any(control => control.IsInteracting)) return;
        // Calculate from the full row, so changing to compact mode does not make it oscillate back.
        var fullWidth = _cells.DesiredSize.Width + sliders.Where(control => control.IsVisible && !control.SliderExpanded)
            .Sum(control => control.ExpandedWidthDifference);
        if (_smearMode.IsVisible && !_blurRadius.IsVisible) fullWidth += _blurRadius.ExpandedNaturalWidth + _cells.Spacing;
        var compact = new HashSet<InlineNumber>();
        foreach (var control in sliders)
        {
            if (fullWidth <= Overflow.Bounds.Width - 2) break;
            compact.Add(control);
            fullWidth -= control.ExpandedWidthDifference;
        }
        foreach (var control in sliders) control.ExpandSlider(!compact.Contains(control));
        Overflow.HorizontalScrollBarVisibility = fullWidth > Overflow.Bounds.Width
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden;
    }

    /// <summary>A clickable colour of the bar's own, which the window finds out about rather than owns.</summary>
    private sealed class Swatch : Button
    {
        protected override Type StyleKeyOverride => typeof(Button);
        public Swatch()
        {
            Width = 34;
            Height = 18;
            MinHeight = 18;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(0);
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(Colors.White, 0.35);
        }

        public void Show(double red, double green, double blue) => Background = new SolidColorBrush(
            Color.FromRgb((byte)Math.Clamp(Math.Round(red * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(green * 255), 0, 255),
                (byte)Math.Clamp(Math.Round(blue * 255), 0, 255)));
    }
}
