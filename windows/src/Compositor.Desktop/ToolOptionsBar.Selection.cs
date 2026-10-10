using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Compositor.Core.Document;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

internal enum SelectionAdjustment { Expand, Contract, Feather }

internal sealed partial class ToolOptionsBar
{
    private readonly SegmentedChoice _selectionMode = new("New", "Add", "Subtract");
    private readonly InlineNumber _wandTolerance = new("Tolerance", 0, 255, fieldWidth: 48);
    private readonly ComboBox _wandSample = new StableChoice { Width = 160 };
    private readonly InlineNumber _selectionExpand = new("", 1, SelectionEdits.MaxAmount, unit: "px", fieldWidth: 48);
    private readonly InlineNumber _selectionContract = new("", 1, SelectionEdits.MaxAmount, unit: "px", fieldWidth: 48);
    private readonly InlineNumber _selectionFeather = new("", 1, SelectionEdits.MaxFeather, unit: "px", fieldWidth: 48);
    private readonly Button[] _selectionAdjustments = new Button[3];
    private readonly Button _deselectSelection = new() { [!ContentControl.ContentProperty] = UiText.Bind("Deselect"), Focusable = false };
    internal event Action<SelectionAdjustment, int>? SelectionAdjusted;
    internal event Action? SelectionDeselected;
    internal SegmentedChoice SelectionModeControl => _selectionMode;
    internal InlineNumber WandToleranceControl => _wandTolerance;
    internal ComboBox WandSampleControl => _wandSample;
    internal Button SelectionAdjustmentButton(SelectionAdjustment adjustment) => _selectionAdjustments[(int)adjustment];
    internal InlineNumber SelectionAmountControl(SelectionAdjustment adjustment) => adjustment switch
    { SelectionAdjustment.Expand => _selectionExpand, SelectionAdjustment.Contract => _selectionContract, _ => _selectionFeather };
    internal Button DeselectButton => _deselectSelection;

    private void BuildSelection()
    {
        _selectionMode.Changed += index => Set(ref _options.SelectionMode, (SelectionMode)index);
        _wandTolerance.Changed += value => Set(ref _options.Wand, _options.Wand with { Tolerance = (int)value });
        _wandSample.ItemsSource = new[] { "Point Sample", "3 × 3 Average", "5 × 5 Average", "11 × 11 Average", "31 × 31 Average", "51 × 51 Average" };
        _wandSample.SelectionChanged += (_, _) =>
        {
            if (_loading || _wandSample.SelectedIndex < 0) return;
            var radii = new[] { 0, 1, 2, 5, 15, 25 };
            if (_wandSample.SelectedIndex >= radii.Length) return;
            Set(ref _options.Wand, _options.Wand with { Radius = radii[_wandSample.SelectedIndex] });
        };
        _selectionExpand.Changed += value => { if (!_loading) _options.SelectionExpand = (int)value; };
        _selectionContract.Changed += value => { if (!_loading) _options.SelectionContract = (int)value; };
        _selectionFeather.Changed += value => { if (!_loading) _options.SelectionFeather = (int)value; };
        Cell("selection", _selectionMode);
        foreach (var adjustment in Enum.GetValues<SelectionAdjustment>())
        {
            var field = SelectionAmountControl(adjustment);
            var button = new Button { [!ContentControl.ContentProperty] = UiText.Bind(adjustment.ToString()), Focusable = false };
            button.Click += (_, _) => SelectionAdjusted?.Invoke(adjustment, (int)field.Value);
            _selectionAdjustments[(int)adjustment] = button;
            Cell("selection", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { button, field } });
        }
        _deselectSelection.Click += (_, _) => SelectionDeselected?.Invoke();
        Cell("selection", _deselectSelection);
    }

    internal void ShowSelection(SelectionMode mode, bool hasSelection)
    {
        _selectionMode.SelectedIndex = (int)mode;
        foreach (var button in _selectionAdjustments) button.IsEnabled = hasSelection;
        _deselectSelection.IsEnabled = hasSelection;
    }

    private void RefreshSelection()
    {
        _selectionMode.SelectedIndex = (int)_options.SelectionMode;
        _wandTolerance.Value = _options.Wand.Tolerance;
        var radius = _options.Wand.Radius;
        var radii = new[] { 0, 1, 2, 5, 15, 25 };
        var index = Array.IndexOf(radii, radius);
        // A custom radius entered in the Tools menu remains a real choice when the strip is reopened.
        var samples = new List<string> { "Point Sample", "3 × 3 Average", "5 × 5 Average", "11 × 11 Average", "31 × 31 Average", "51 × 51 Average" };
        if (index < 0) { samples.Add($"{radius * 2 + 1} × {radius * 2 + 1} Average"); index = samples.Count - 1; }
        if (_wandSample.ItemsSource is not string[] current || !current.SequenceEqual(samples)) _wandSample.ItemsSource = samples.ToArray();
        _wandSample.SelectedIndex = index;
        _selectionExpand.Value = _options.SelectionExpand;
        _selectionContract.Value = _options.SelectionContract;
        _selectionFeather.Value = _options.SelectionFeather;
    }
}
