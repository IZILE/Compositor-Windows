using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>A compact number, optional slider and scrubbable label in the Mac tool header.</summary>
internal sealed class InlineNumber : StackPanel
{
    private readonly double _minimum;
    private readonly double _maximum;
    private readonly double _step;
    private readonly string _format;
    private readonly TextBlock _label;
    private readonly TextBlock? _unitLabel;
    private readonly Slider? _slider;
    private readonly Slider? _popupSlider;
    private readonly Grid? _sliderHost;
    private readonly Button? _sliderButton;
    private readonly Popup? _popup;
    private readonly bool _logarithmic;
    private bool _expanded = true;
    private bool _sliderDrag;
    private bool _loading;
    private double _value;
    private string _displayedText = "";
    private (double X, double Value)? _scrub;
    internal TextBox Field { get; }
    internal Slider? Slider => _slider;
    internal Slider? PopupSlider => _popupSlider;
    internal Button? SliderButton => _sliderButton;
    internal Popup? SliderPopup => _popup;
    internal bool HasAdaptiveSlider => _sliderHost is not null;
    internal bool SliderExpanded => _expanded;
    internal bool IsInteracting => _sliderDrag || _scrub is not null;
    internal double ExpandedWidthDifference => 82;
    internal double ExpandedNaturalWidth => CaptionWidth.Maximum(_label is StableCaption caption
            ? caption.Variants.Select(UiText.Get) : [_label.Text ?? ""],
        _label.FontFamily, _label.FontStyle, _label.FontWeight, _label.FontStretch, _label.FontSize)
        + 100 + Field.Width + (_unitLabel is null ? 12 : 18 + CaptionWidth.Maximum([_unitLabel.Text ?? ""],
            _unitLabel.FontFamily, _unitLabel.FontStyle, _unitLabel.FontWeight, _unitLabel.FontStretch, _unitLabel.FontSize));
    internal event Action<double>? Changed;
    internal event Action? EditingStarted;
    internal event Action? EditingFinished;
    internal event Action? EditingCommitted;

    internal InlineNumber(string name, double minimum, double maximum, double step = 1,
        string unit = "", bool slider = false, double fieldWidth = 42, double? sliderMaximum = null, string? format = null,
        string[]? alternateLabels = null, bool adaptiveSlider = false, bool logarithmicSlider = false)
    {
        _minimum = minimum; _maximum = maximum; _step = step;
        _logarithmic = logarithmicSlider;
        _format = format ?? (step < 1 ? "0.#" : "0");
        Orientation = Orientation.Horizontal; Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center; Focusable = true;
        _label = alternateLabels is null ? new TextBlock() : new StableCaption { Variants = alternateLabels };
        UiText.Set(_label, TextBlock.TextProperty, name);
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        // Reserve enough room for the largest valid number, including its sign and decimal places.
        var longest = Math.Max(minimum.ToString(_format, CultureInfo.InvariantCulture).Length,
            maximum.ToString(_format, CultureInfo.InvariantCulture).Length);
        var decimalPlaces = _format.Contains('.') ? _format.Split('.')[1].Count(character => character is '0' or '#') : 0;
        var integerPlaces = Math.Ceiling(Math.Max(Math.Abs(minimum), Math.Abs(maximum))).ToString("0", CultureInfo.InvariantCulture).Length;
        longest = Math.Max(longest, integerPlaces + (minimum < 0 ? 1 : 0) + (decimalPlaces > 0 ? decimalPlaces + 1 : 0));
        Field = new TextBox { Classes = { "numeric" }, Width = Math.Max(fieldWidth, longest * 8 + 12), Height = 24, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Automation.AutomationProperties.SetName(Field, name);
        Children.Add(_label);
        if (slider)
        {
            Slider MakeSlider(double width)
            {
                var control = new Slider { Width = width, Minimum = _logarithmic ? 0 : minimum,
                    Maximum = _logarithmic ? 1000 : sliderMaximum ?? maximum,
                    VerticalAlignment = VerticalAlignment.Center };
                UiText.Set(control, Avalonia.Automation.AutomationProperties.NameProperty, name);
                control.PropertyChanged += (_, args) =>
                {
                    if (args.Property != RangeBase.ValueProperty || _loading) return;
                    var number = _logarithmic ? Math.Round(Math.Exp(control.Value / 1000 * Math.Log(maximum / minimum)) * minimum) : control.Value;
                    Commit(number);
                    Avalonia.Automation.AutomationProperties.SetHelpText(control, _displayedText + " " + unit);
                };
                control.AddHandler(PointerPressedEvent, (_, _) => { _sliderDrag = true; EditingStarted?.Invoke(); }, RoutingStrategies.Bubble, true);
                control.AddHandler(PointerReleasedEvent, (_, _) => { _sliderDrag = false; EditingFinished?.Invoke(); }, RoutingStrategies.Bubble, true);
                control.PointerCaptureLost += (_, _) => { _sliderDrag = false; };
                return control;
            }
            _slider = MakeSlider(100);
            if (adaptiveSlider)
            {
                _sliderButton = new Button { Content = "⌄", Width = 18, Height = 24, MinHeight = 24,
                    Padding = new Thickness(0), CornerRadius = new CornerRadius(6), IsVisible = false };
                UiText.Set(_sliderButton, Avalonia.Automation.AutomationProperties.NameProperty, name);
                UiText.Set(_sliderButton, ToolTip.TipProperty, "Adjust with slider");
                _popupSlider = MakeSlider(220);
                _popup = new Popup { PlacementTarget = _sliderButton, Placement = PlacementMode.BottomEdgeAlignedLeft,
                    IsLightDismissEnabled = true, Child = new Border { Background = Skin.ChromeBrush,
                        BorderBrush = new SolidColorBrush(Colors.White, 0.12), BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Child = _popupSlider } };
                PopupMotion.SetEnabled(_popup, true);
                _sliderButton.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;
                _sliderHost = new Grid { Width = 100, Children = { _slider, _sliderButton, _popup } };
                Children.Add(_sliderHost);
            }
            else Children.Add(_slider);
        }
        Children.Add(Field);
        if (unit.Length > 0)
        {
            _unitLabel = new TextBlock { Text = unit, Foreground = Skin.SecondaryBrush,
                VerticalAlignment = VerticalAlignment.Center };
            Children.Add(_unitLabel);
        }
        Field.GotFocus += (_, _) => EditingStarted?.Invoke();
        Field.LostFocus += (_, _) => { CommitText(); EditingFinished?.Invoke(); };
        Field.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { CommitText(); EditingFinished?.Invoke(); Focus(); EditingCommitted?.Invoke(); args.Handled = true; }
            else if (args.Key == Key.Escape) { Show(_value); EditingFinished?.Invoke(); Focus(); EditingCommitted?.Invoke(); args.Handled = true; }
            else if (args.Key is Key.Up or Key.Down)
            {
                var value = ReadText();
                Commit(value + (args.Key == Key.Up ? 1 : -1) * _step * (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1));
                args.Handled = true;
            }
        };
        _label.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(_label).Properties.IsLeftButtonPressed) return;
            EditingStarted?.Invoke();
            _scrub = (args.GetPosition(TopLevel.GetTopLevel(this)).X, _value);
            args.Pointer.Capture(_label); args.Handled = true;
        };
        _label.PointerMoved += (_, args) =>
        {
            if (_scrub is not { } scrub) return;
            Commit(scrub.Value + (args.GetPosition(TopLevel.GetTopLevel(this)).X - scrub.X) * _step);
            args.Handled = true;
        };
        _label.PointerReleased += (_, args) =>
        {
            if (_scrub is null) return;
            _scrub = null; EditingFinished?.Invoke(); args.Pointer.Capture(null); args.Handled = true;
        };
        _label.PointerCaptureLost += (_, _) =>
        {
            if (_scrub is null) return;
            _scrub = null; EditingFinished?.Invoke();
        };
        Show(minimum);
        PropertyChanged += (_, args) => { if (args.Property == IsVisibleProperty && !IsVisible && _popup is not null) _popup.IsOpen = false; };
        DetachedFromVisualTree += (_, _) => { if (_popup is not null) _popup.IsOpen = false; };
    }

    internal double Value { get => _value; set => Show(value); }
    internal void ExpandSlider(bool expanded)
    {
        if (_sliderHost is null || _expanded == expanded || IsInteracting) return;
        _expanded = expanded;
        _slider!.IsVisible = expanded;
        _sliderButton!.IsVisible = !expanded;
        _sliderHost.Width = expanded ? 100 : 18;
        if (expanded) _popup!.IsOpen = false;
    }
    internal void SetLabel(string name) => UiText.Set(_label, TextBlock.TextProperty, name);
    private double ReadText()
    {
        if (Field.Text == _displayedText) return _value;
        return double.TryParse(Field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
            || double.TryParse(Field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : _value;
    }
    private void CommitText() => Commit(ReadText());
    private void Commit(double value)
    {
        if (!double.IsFinite(value)) value = _value;
        value = Math.Clamp(value, _minimum, _maximum);
        var changed = Math.Abs(value - _value) > 0.000001;
        Show(value);
        if (changed) Changed?.Invoke(value);
    }
    private void Show(double value)
    {
        _loading = true;
        try
        {
            _value = double.IsFinite(value) ? Math.Clamp(value, _minimum, _maximum) : _minimum;
            _displayedText = _value.ToString(_format, CultureInfo.InvariantCulture);
            Field.Text = _displayedText;
            foreach (var control in new[] { _slider, _popupSlider })
                if (control is not null) control.Value = _logarithmic
                    ? Math.Log(_value / _minimum) / Math.Log(_maximum / _minimum) * 1000
                    : Math.Clamp(_value, control.Minimum, control.Maximum);
        }
        finally { _loading = false; }
    }
}
