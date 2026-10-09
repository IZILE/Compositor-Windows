using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Compositor.Desktop;

/// <summary>Mac-style tab capsules: oldest tabs overflow first, the selected one stays, and dragging reorders.</summary>
internal sealed class ProjectTabs : Panel
{
    internal sealed record Entry(Guid ID, string Title, bool Dirty, bool Selected, Action Select, Action Close);
    private readonly Dictionary<Guid, Border> _pills = [];
    private List<Entry> _entries = [];
    private readonly Button _overflow = new() { Height = 28, CornerRadius = new CornerRadius(14) };
    private List<Entry> _hidden = [];
    private Guid? _pressed;
    private Point _start;
    private double _origin;
    private bool _dragging;
    private int _target;
    private List<Guid> _others = [];
    private Action<Guid, int>? _reorder;
    internal int HiddenCount => _hidden.Count;
    internal IReadOnlyList<Guid> VisibleIDs => _entries.Where(entry => _pills[entry.ID].IsVisible).Select(entry => entry.ID).ToList();
    internal Control? Pill(Guid id) => _pills.GetValueOrDefault(id);

    internal ProjectTabs()
    {
        Height = 34;
        ClipToBounds = true;
        Children.Add(_overflow);
        _overflow.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var entry in _hidden)
            {
                var item = new MenuItem { Header = (entry.Dirty ? "• " : "") + entry.Title };
                item.Click += (_, _) => entry.Select();
                menu.Items.Add(item);
            }
            menu.Open(_overflow);
        };
    }

    internal void Update(IEnumerable<Entry> entries, Action<Guid, int> reorder)
    {
        _entries = entries.ToList();
        _reorder = reorder;
        foreach (var id in _pills.Keys.Where(id => !_entries.Any(entry => entry.ID == id)).ToList())
        {
            Children.Remove(_pills[id]);
            _pills.Remove(id);
        }
        foreach (var entry in _entries)
        {
            if (!_pills.TryGetValue(entry.ID, out var pill))
            {
                var name = new Button { Classes = { "plain" }, Height = 28, Padding = new Thickness(11, 0, 8, 0),
                    HorizontalContentAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) };
                var close = new Button { Classes = { "plain" }, Content = "×", FontSize = 11,
                    Width = 21, Height = 28, Foreground = Skin.SecondaryBrush, Padding = new Thickness(0, 0, 5, 0),
                    CornerRadius = new CornerRadius(0, 14, 14, 0) };
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,21"), Children = { name, close } };
                Grid.SetColumn(close, 1);
                pill = new Border { Child = row, Height = 28, CornerRadius = new CornerRadius(14),
                    BorderThickness = new Thickness(1), RenderTransform = new TranslateTransform(),
                    RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative), Tag = entry.ID };
                pill.Transitions = new Transitions
                {
                    new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, 0.58, 1) },
                    new BrushTransition { Property = Border.BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(140), Easing = new SplineEasing(0, 0, 0.58, 1) },
                };
                var id = entry.ID;
                name.Click += (_, _) => _entries.FirstOrDefault(value => value.ID == id)?.Select();
                close.Click += (_, _) => _entries.FirstOrDefault(value => value.ID == id)?.Close();
                name.AddHandler(PointerPressedEvent, (_, args) => Press(id, args), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                pill.AddHandler(PointerMovedEvent, (_, args) => Move(id, args), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                pill.AddHandler(PointerReleasedEvent, (_, args) => Release(id, args), Avalonia.Interactivity.RoutingStrategies.Tunnel);
                pill.PointerCaptureLost += (_, _) => { if (_dragging && _pressed == id) EndDrag(); };
                _pills.Add(id, pill);
                Children.Add(pill);
            }
            var controls = ((Grid)pill.Child!).Children.OfType<Button>().ToArray();
            controls[0].Content = new TextBlock { Text = (entry.Dirty ? "• " : "") + entry.Title,
                TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12,
                FontWeight = entry.Selected ? FontWeight.SemiBold : FontWeight.Medium };
            ToolTip.SetTip(controls[0], entry.Title);
            ToolTip.SetTip(controls[1], UiText.Format("Close {0}", entry.Title));
            pill.Background = entry.Selected ? Skin.TabFront : Skin.TabBack;
            pill.BorderBrush = entry.Selected ? Skin.TabFrontEdge : Skin.TabBackEdge;
        }
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var entry in _entries)
        {
            var button = ((Grid)_pills[entry.ID].Child!).Children.OfType<Button>().First();
            var label = new FormattedText(entry.Title, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(button.FontFamily,
                    weight: entry.Selected ? FontWeight.SemiBold : FontWeight.Medium), 12, Skin.LabelBrush);
            _pills[entry.ID].Width = Math.Clamp(Math.Ceiling(label.Width) + (entry.Dirty ? 10 : 0), 35, 155) + 42;
            _pills[entry.ID].Measure(new Size(double.PositiveInfinity, 28));
        }
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Span(_entries), 34);
    }

    private double Span(IEnumerable<Entry> entries)
    {
        var list = entries.ToList();
        return list.Sum(entry => _pills[entry.ID].Width) + Math.Max(0, list.Count - 1) * 6;
    }
    private double OverflowWidth(int hidden)
    {
        _overflow.Content = (hidden == 1 ? UiText.Get("1 more tab") : UiText.Format("{0} more tabs", hidden)) + " ⌄";
        _overflow.Measure(new Size(double.PositiveInfinity, 28));
        return _overflow.DesiredSize.Width;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = _entries.ToList();
        _hidden = [];
        if (Span(visible) > finalSize.Width)
        {
            while (visible.Count > 1 && OverflowWidth(_hidden.Count) + 6 + Span(visible) > finalSize.Width)
            { _hidden.Add(visible[0]); visible.RemoveAt(0); }
            if (_hidden.FirstOrDefault(entry => entry.Selected) is { } selected)
            {
                _hidden.Remove(selected);
                _hidden.Add(visible[0]);
                visible[0] = selected;
                while (visible.Count > 1 && OverflowWidth(_hidden.Count) + 6 + Span(visible) > finalSize.Width)
                { _hidden.Add(visible[1]); visible.RemoveAt(1); }
            }
        }
        _overflow.IsVisible = _hidden.Count > 0;
        var x = _hidden.Count > 0 ? OverflowWidth(_hidden.Count) + 6 : 0;
        _overflow.Arrange(new Rect(0, 3, Math.Max(0, x - 6), 28));
        foreach (var entry in _entries) _pills[entry.ID].IsVisible = visible.Contains(entry);
        if (_dragging && _pressed is { } dragged)
        {
            visible = visible.Where(entry => entry.ID != dragged).ToList();
            if (_entries.FirstOrDefault(entry => entry.ID == dragged) is { } held)
                visible.Insert(Math.Clamp(_target, 0, visible.Count), held);
        }
        foreach (var entry in visible)
        {
            var pill = _pills[entry.ID];
            var width = Math.Min(pill.Width, Math.Max(0, finalSize.Width - x));
            pill.Arrange(new Rect(0, 3, width, 28));
            if (!_dragging || entry.ID != _pressed) ((TranslateTransform)pill.RenderTransform!).X = x;
            x += width + 6;
        }
        return finalSize;
    }

    private Point At(PointerEventArgs args) => args.GetPosition(TopLevel.GetTopLevel(this));
    private void Press(Guid id, PointerPressedEventArgs args)
    {

        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pressed = id; _start = At(args); _origin = ((TranslateTransform)_pills[id].RenderTransform!).X;
    }
    private void Move(Guid id, PointerEventArgs args)
    {

        if (_pressed != id || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var delta = At(args).X - _start.X;
        if (!_dragging && Math.Abs(delta) < 3) return;
        if (!_dragging)
        {
            _dragging = true;
            _others = VisibleIDs.Where(value => value != id).ToList();
            args.Pointer.Capture(_pills[id]);
            foreach (var (other, pill) in _pills)
                ((TranslateTransform)pill.RenderTransform!).Transitions = other == id ? null : new Transitions
                { new DoubleTransition { Property = TranslateTransform.XProperty, Duration = TimeSpan.FromSeconds(0.15),
                    Easing = new SplineEasing(0, 0, 0.58, 1) } };
            _entries.First(entry => entry.ID == id).Select();
        }
        var x = Math.Clamp(_origin + delta, _overflow.IsVisible ? _overflow.Bounds.Width + 6 : 0,
            Math.Max(0, Bounds.Width - _pills[id].Bounds.Width));
        ((TranslateTransform)_pills[id].RenderTransform!).X = x;
        var positions = new List<double>();
        var first = _overflow.IsVisible ? _overflow.Bounds.Width + 6 : 0;
        foreach (var other in _others) { positions.Add(first); first += _pills[other].Width + 6; }
        positions.Add(first);
        _target = Enumerable.Range(0, positions.Count).MinBy(index => Math.Abs(positions[index] - x));
        InvalidateArrange();
        args.Handled = true;
    }
    private void Release(Guid id, PointerReleasedEventArgs args)
    {

        if (_pressed != id) return;
        if (_dragging)
        {
            var before = _target < _others.Count ? _others[_target] : (Guid?)null;
            var target = before is { } neighbor ? _entries.FindIndex(entry => entry.ID == neighbor)
                : _others.Count > 0 ? _entries.FindIndex(entry => entry.ID == _others[^1]) + 1 : 0;
            if (_entries.FindIndex(entry => entry.ID == id) < target) target--;
            var callback = _reorder;
            EndDrag();
            args.Pointer.Capture(null);
            callback?.Invoke(id, target);
            args.Handled = true;
        }
        _pressed = null;
    }
    private void EndDrag()
    {
        _dragging = false; _pressed = null;
        foreach (var pill in _pills.Values) ((TranslateTransform)pill.RenderTransform!).Transitions = null;
        InvalidateArrange();
    }
}
