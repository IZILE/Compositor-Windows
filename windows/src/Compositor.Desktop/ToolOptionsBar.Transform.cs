using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Compositor.Core.Format;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Desktop;

internal sealed partial class ToolOptionsBar
{
    private readonly InlineNumber _transformX = new("X", -30_000, 30_000, fieldWidth: 60, format: "0.##");
    private readonly InlineNumber _transformY = new("Y", -30_000, 30_000, fieldWidth: 60, format: "0.##");
    private readonly InlineNumber _transformW = new("W", 1, 30_000, fieldWidth: 60, format: "0.##");
    private readonly InlineNumber _transformH = new("H", 1, 30_000, fieldWidth: 60, format: "0.##");
    private readonly InlineNumber _transformScale = new("Scale", 0.1, 30_000, unit: "%", fieldWidth: 54, format: "0.##");
    private readonly InlineNumber _transformAngle = new("°", -360, 360, fieldWidth: 54, format: "0.##");
    private readonly ComboBox _sampling = new() { Width = 170 };
    private readonly CheckBox _autoSelect = new() { [!ContentControl.ContentProperty] = UiText.Bind("Auto Select") };
    private readonly CheckBox _showControls = new() { [!ContentControl.ContentProperty] = UiText.Bind("Show Controls") };
    private readonly ToggleButton _ratio = new() { Content = new LinkIcon(), Width = 28, Padding = new Thickness(4) };
    private readonly Button _applyTransform = new() { [!ContentControl.ContentProperty] = UiText.Bind("Apply"), Classes = { "accent" } };
    private readonly Button _cancelTransform = new() { [!ContentControl.ContentProperty] = UiText.Bind("Cancel") };
    private readonly StackPanel _fixedTransformToggles = new()
    {
        Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 12, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _pendingTransformActions = new()
    {
        Orientation = Orientation.Horizontal, Spacing = 12, Opacity = 0, IsHitTestVisible = false,
        Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty,
            Duration = TimeSpan.FromSeconds(0.12), Easing = new SplineEasing(0, 0, 0.58, 1) } },
    };
    private LayerTransform? _shownTransform;
    private bool _pendingShown;
    private double _pixelWidth = 1, _pixelHeight = 1;
    internal event Action<LayerTransform>? TransformNumberChanged;
    internal event Action? TransformNumberStarted;
    internal event Action? TransformNumberFinished;
    internal event Action<bool>? AutoSelectChanged;
    internal event Action<bool>? ShowControlsChanged;
    internal event Action<bool>? RatioChanged;
    internal event Action? TransformApplied;
    internal event Action? TransformCancelled;
    internal InlineNumber TransformX => _transformX;
    internal InlineNumber TransformWidth => _transformW;
    internal Button ApplyTransformButton => _applyTransform;
    internal Button CancelTransformButton => _cancelTransform;
    internal Control PendingTransformActions => _pendingTransformActions;
    internal ToggleButton RatioButton => _ratio;
    internal CheckBox AutoSelectControl => _autoSelect;
    internal CheckBox ShowControlsControl => _showControls;

    private void BuildTransform()
    {
        foreach (var field in new[] { _transformX, _transformY, _transformW, _transformH, _transformScale, _transformAngle })
        {
            field.EditingStarted += () => TransformNumberStarted?.Invoke();
            field.EditingFinished += () => TransformNumberFinished?.Invoke();
        }
        void Change(Func<LayerTransform, LayerTransform> change)
        {
            if (_loading || _shownTransform is not { } current) return;
            var wanted = change(current);
            if (wanted.IsValid) TransformNumberChanged?.Invoke(wanted);
        }
        _transformX.Changed += value => Change(current => current with { X = value });
        _transformY.Changed += value => Change(current => current with { Y = value });
        _transformW.Changed += value => Change(current => current with { Width = value,
            Height = _ratio.IsChecked == true ? Math.Clamp(current.Height * value / current.Width, 1, 30_000) : current.Height });
        _transformH.Changed += value => Change(current => current with { Height = value,
            Width = _ratio.IsChecked == true ? Math.Clamp(current.Width * value / current.Height, 1, 30_000) : current.Width });
        _transformScale.Changed += value => Change(current => current.ScaledToPercent(value, _pixelWidth, _pixelHeight));
        _transformAngle.Changed += value => Change(current => current with { Rotation = value % 360 });
        _sampling.ItemsSource = Enum.GetValues<LayerSampling>().Select(value => value switch
        {
            LayerSampling.Nearest => "Nearest",
            LayerSampling.Smooth => "Smooth",
            _ => "High Quality",
        }).ToArray();
        _sampling.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            TransformNumberStarted?.Invoke();
            Change(current => current with { Sampling = (LayerSampling)Math.Max(0, _sampling.SelectedIndex) });
            TransformNumberFinished?.Invoke();
        };
        _autoSelect.IsCheckedChanged += (_, _) => { if (!_loading) AutoSelectChanged?.Invoke(_autoSelect.IsChecked == true); };
        _showControls.IsCheckedChanged += (_, _) => { if (!_loading) ShowControlsChanged?.Invoke(_showControls.IsChecked == true); };
        _ratio.IsCheckedChanged += (_, _) => { if (!_loading) RatioChanged?.Invoke(_ratio.IsChecked == true); };
        ToolTip.SetTip(_ratio, UiText.Get("Lock aspect ratio"));
        _applyTransform.Click += (_, _) => TransformApplied?.Invoke();
        _cancelTransform.Click += (_, _) => TransformCancelled?.Invoke();
        _pendingTransformActions.Children.Add(_cancelTransform);
        _pendingTransformActions.Children.Add(_applyTransform);
        _fixedTransformToggles.Children.Add(_autoSelect); _fixedTransformToggles.Children.Add(_showControls);
        Register("transform", _fixedTransformToggles); Register("transform", _pendingTransformActions);
        Cell("transform", _transformX); Cell("transform", _transformY);
        Cell("transform", _transformW); Cell("transform", _transformH); Cell("transform", _ratio);
        Cell("transform", _transformScale); Cell("transform", _transformAngle);
        var sampling = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,6,*"), Width = 170, VerticalAlignment = VerticalAlignment.Center };
        sampling.Children.Add(new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Sampling"), VerticalAlignment = VerticalAlignment.Center });
        _sampling.Width = double.NaN; Grid.SetColumn(_sampling, 2); sampling.Children.Add(_sampling);
        Cell("transform", sampling);
        Cell("transform", _flipH); Cell("transform", _flipV);
    }

    internal void ShowTransform(LayerTransform? value, double pixelWidth, double pixelHeight, bool autoSelect,
        bool showControls, bool lockRatio, bool pending)
    {
        var wasLoading = _loading; _loading = true;
        try
        {
            _shownTransform = value; _pixelWidth = Math.Max(1, pixelWidth); _pixelHeight = Math.Max(1, pixelHeight);
            _autoSelect.IsChecked = autoSelect; _showControls.IsChecked = showControls; _ratio.IsChecked = lockRatio;
            foreach (var field in new[] { _transformX, _transformY, _transformW, _transformH, _transformScale, _transformAngle })
                field.IsEnabled = value.HasValue;
            _sampling.IsEnabled = _ratio.IsEnabled = value.HasValue;
            if (value is { } box)
            {
                _transformX.Value = box.X; _transformY.Value = box.Y;
                _transformW.Value = box.Width; _transformH.Value = box.Height;
                _transformScale.Value = box.Width * 100 / _pixelWidth; _transformAngle.Value = box.Rotation;
                _sampling.SelectedIndex = (int)box.Sampling;
            }
            if (_pendingShown != pending)
            {
                _pendingShown = pending;
                Motion.Set(_pendingTransformActions, OpacityProperty, pending ? 1.0 : 0.0);
            }
            _pendingTransformActions.IsHitTestVisible = pending;
            _applyTransform.IsEnabled = _cancelTransform.IsEnabled = pending;
        }
        finally { _loading = wasLoading; }
    }

    private sealed class LinkIcon : Control
    {
        public LinkIcon() { Width = 16; Height = 14; }
        public override void Render(DrawingContext context)
        {
            var pen = new Pen(Skin.LabelBrush, 1.3);
            context.DrawEllipse(null, pen, new Point(5, 9), 3.5, 3);
            context.DrawEllipse(null, pen, new Point(11, 5), 3.5, 3);
            context.DrawLine(pen, new Point(6, 8), new Point(10, 6));
        }
    }
}
