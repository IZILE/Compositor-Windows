using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>A localized compact segmented picker, as used by the Mac tool options.</summary>
internal sealed class SegmentedChoice : Border
{
    private readonly Button[] _buttons;
    private readonly Border _selection;
    private readonly TranslateTransform _offset;
    private int _selected;
    internal event Action<int>? Changed;
    internal SegmentedChoice(params string[] choices)
    {
        CornerRadius = new CornerRadius(12);
        Background = new SolidColorBrush(Color.FromRgb(53, 53, 53));
        BorderBrush = new SolidColorBrush(Colors.White, 0.08); BorderThickness = new Thickness(0.5);
        _offset = new TranslateTransform { Transitions = new Transitions { new DoubleTransition
            { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromSeconds(0.14), Easing = new SplineEasing(0, 0, 0.58, 1) } } };
        _selection = new Border { Height = 22, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromRgb(41, 121, 245)), IsHitTestVisible = false,
            RenderTransform = _offset, Transitions = new Transitions { new DoubleTransition
                { Property = WidthProperty, Duration = TimeSpan.FromSeconds(0.14), Easing = new SplineEasing(0, 0, 0.58, 1) } } };
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
    internal Button ButtonAt(int index) => _buttons[index];
    private void PlaceSelection()
    {
        if (_buttons[_selected].Bounds.Width <= 0) return;
        _offset.X = _buttons[_selected].Bounds.X;
        _selection.Width = _buttons[_selected].Bounds.Width;
    }
}
