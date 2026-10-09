using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    private readonly Slider? _slider;
    private bool _loading;
    private double _value;
    private string _displayedText = "";
    private (double X, double Value)? _scrub;
    internal TextBox Field { get; }
    internal Slider? Slider => _slider;
    internal event Action<double>? Changed;
    internal event Action? EditingStarted;
    internal event Action? EditingFinished;

    internal InlineNumber(string name, double minimum, double maximum, double step = 1,
        string unit = "", bool slider = false, double fieldWidth = 42, double? sliderMaximum = null, string? format = null,
        string[]? alternateLabels = null)
    {
        _minimum = minimum; _maximum = maximum; _step = step;
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
            _slider = new Slider { Width = 100, Minimum = minimum, Maximum = sliderMaximum ?? maximum,
                VerticalAlignment = VerticalAlignment.Center };
            _slider.PropertyChanged += (_, args) =>
            {
                if (args.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_loading)
                    Commit(_slider.Value);
            };
            Children.Add(_slider);
        }
        Children.Add(Field);
        if (unit.Length > 0) Children.Add(new TextBlock { Text = unit, Foreground = Skin.SecondaryBrush,
            VerticalAlignment = VerticalAlignment.Center });
        Field.GotFocus += (_, _) => EditingStarted?.Invoke();
        Field.LostFocus += (_, _) => { CommitText(); EditingFinished?.Invoke(); };
        Field.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { CommitText(); EditingFinished?.Invoke(); Focus(); args.Handled = true; }
            else if (args.Key == Key.Escape) { Show(_value); EditingFinished?.Invoke(); Focus(); args.Handled = true; }
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
    }

    internal double Value { get => _value; set => Show(value); }
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
            if (_slider is not null) _slider.Value = Math.Clamp(_value, _slider.Minimum, _slider.Maximum);
        }
        finally { _loading = false; }
    }
}
