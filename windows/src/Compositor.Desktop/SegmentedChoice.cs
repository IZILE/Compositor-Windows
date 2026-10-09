using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>A localized compact segmented picker, as used by the Mac tool options.</summary>
internal sealed class SegmentedChoice : Border
{
    private readonly Button[] _buttons;
    private readonly SelectionIndicator _selection;
    private int _selected;
    internal event Action<int>? Changed;
    internal SegmentedChoice(params string[] choices)
    {
        ArgumentOutOfRangeException.ThrowIfZero(choices.Length);
        CornerRadius = new CornerRadius(12);
        Background = new SolidColorBrush(Color.FromRgb(53, 53, 53));
        BorderBrush = new SolidColorBrush(Colors.White, 0.08); BorderThickness = new Thickness(0.5);
        _selection = new SelectionIndicator { Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(41, 121, 245)) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        _buttons = choices.Select((name, index) =>
        {
            var button = new Button { Classes = { "plain" }, Padding = new Thickness(8, 0),
                MinHeight = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Brushes.Transparent,
                Content = new TextBlock { [!TextBlock.TextProperty] = UiText.Bind(name) } };
            button.Click += (_, _) => { if (_selected == index) return; SelectedIndex = index; Changed?.Invoke(index); };
            row.Children.Add(button); return button;
        }).ToArray();
        Child = new Grid { Children = { _selection, row } };
        row.SizeChanged += (_, _) => PlaceSelection();
        foreach (var button in _buttons) button.SizeChanged += (_, _) => PlaceSelection();
        SelectedIndex = 0;
    }
    internal int SelectedIndex
    {
        get => _selected;
        set
        {
            _selected = Math.Clamp(value, 0, _buttons.Length - 1);
            PlaceSelection();
        }
    }
    internal Control SelectionHighlight => _selection;
    internal SelectionIndicator Indicator => _selection;
    internal int Count => _buttons.Length;
    internal Button ButtonAt(int index) => _buttons[index];
    private void PlaceSelection()
    {
        if (_buttons[_selected].Bounds.Width <= 0) return;
        _selection.MoveTo(new Rect(_buttons[_selected].Bounds.X, 0, _buttons[_selected].Bounds.Width, 22));
    }
}
