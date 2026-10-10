using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly Grid _layerDragHost = new() { ClipToBounds = true };
    private readonly SelectionIndicator _layerDropMarker = new() { IsVisible = false, CornerRadius = new CornerRadius(5),
        Background = new SolidColorBrush(Skin.Accent, .2), BorderBrush = Skin.AccentBrush, BorderThickness = new Thickness(1), ZIndex = 20 };
    private readonly TextBlock _layerDragText = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Border _layerDragBadge = new() { IsVisible = false, IsHitTestVisible = false, ZIndex = 30,
        CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 5), Background = Skin.ChromeBrush,
        BorderBrush = Skin.AccentBrush, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform() };
    private readonly DispatcherTimer _layerDragScroll = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IPointer? _layerDragPointer;
    private Guid? _layerDragSource;
    private CanvasDocument? _layerDragDocument;
    private Guid _layerDragRevision;
    private (Guid? Parent, Guid? Above, bool Bottom, Guid? MaskTarget, bool Copying)? _layerValidatedDrop;
    private bool _layerDropValid;
    private List<Guid> _layerDraggedIDs = [];
    private Point _layerDragStart, _layerDragPosition;
    private KeyModifiers _layerDragKeys;
    private bool _layerDragging, _layerMaskDragging;
    private (Guid? Parent, Guid? Above, bool Bottom, Guid? MaskTarget)? _layerDrop;
    private ScrollViewer? _layerDragViewer;
    private (CanvasDocument Document, Guid ID)? _copiedMask;

    private Control LayerDragPanel()
    {
        _layerDragBadge.Child = _layerDragText;
        _layerDragHost.Children.Add(_layers); _layerDragHost.Children.Add(_layerDropMarker); _layerDragHost.Children.Add(_layerDragBadge);
        _layers.AddHandler(PointerPressedEvent, LayerDragPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _layers.AddHandler(PointerMovedEvent, LayerDragMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _layers.AddHandler(PointerReleasedEvent, LayerDragReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _layers.PointerCaptureLost += (_, _) => CancelLayerDrag();
        AddHandler(KeyDownEvent, (_, e) => { if (_layerDragSource is not null && e.Key == Key.Escape) { CancelLayerDrag(); e.Handled = true; } }, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => CancelLayerDrag();
        _layerDragScroll.Tick += (_, _) =>
        {
            if (!_layerDragging || _layerDragViewer is null) return;
            var delta = _layerDragPosition.Y < 28 ? -12 : _layerDragPosition.Y > _layerDragHost.Bounds.Height - 28 ? 12 : 0;
            if (delta == 0) return;
            var max = Math.Max(0, _layerDragViewer.Extent.Height - _layerDragViewer.Viewport.Height);
            _layerDragViewer.Offset = new Vector(_layerDragViewer.Offset.X, Math.Clamp(_layerDragViewer.Offset.Y + delta, 0, max));
            UpdateLayerDrop();
        };
        return _layerDragHost;
    }

    private void LayerDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_document is null || !e.GetCurrentPoint(_layers).Properties.IsLeftButtonPressed) return;
        var visual = e.Source as Visual;
        var card = visual?.GetSelfAndVisualAncestors().OfType<LayerCard>().FirstOrDefault();
        var cell = visual?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
        var row = cell?.Content as LayerRow ?? _layerRowItems.FirstOrDefault(item => ReferenceEquals(item.Card, card));
        if (row is null) return;
        card = row.Card;
        var button = visual?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        var mask = button == card.MaskButton && e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (button is not null && !mask) return;
        _layerDragSource = row.ID; _layerMaskDragging = mask; _layerDragKeys = e.KeyModifiers;
        _layerDragDocument = _document; _layerValidatedDrop = null;
        _layerDraggedIDs = SelectedLayers.Contains(row.ID) ? SelectedLayers : [row.ID];
        _layerDragStart = _layerDragPosition = e.GetPosition(_layerDragHost);
        _layerDragPointer = e.Pointer; e.Pointer.Capture(_layers); e.Handled = true;
    }

    private void LayerDragMoved(object? sender, PointerEventArgs e)
    {
        if (_layerDragSource is null) return;
        if (!e.GetCurrentPoint(_layers).Properties.IsLeftButtonPressed) { CancelLayerDrag(); return; }
        _layerDragPosition = e.GetPosition(_layerDragHost); _layerDragKeys = e.KeyModifiers;
        if (!_layerDragging && Math.Abs(_layerDragPosition.X - _layerDragStart.X) < 5 && Math.Abs(_layerDragPosition.Y - _layerDragStart.Y) < 5) return;
        if (!_layerDragging)
        {
            CommitText(); FinishNumberTransform(); ApplyPersistentTransform();
            _layerDragRevision = _history.CurrentRevision;
            _layerDragging = true; _layerDragBadge.IsVisible = true;
            _layerDragViewer = _layers.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            _layerDragScroll.Start();
            if (!_layerMaskDragging) SelectLayerRows(_layerDraggedIDs);
        }
        UpdateLayerDrop(); e.Handled = true;
    }

    private void UpdateLayerDrop()
    {
        _layerDrop = null;
        if (_document is not { } document || _layerDragSource is not { } source) return;
        if (!ReferenceEquals(document, _layerDragDocument) || _history.CurrentRevision != _layerDragRevision)
        { CancelLayerDrag(); return; }
        var point = _layerDragPosition;
        var copying = !_layerMaskDragging && _layerDragKeys.HasFlag(KeyModifiers.Control);
        bool Valid(Guid? parent, Guid? above = null, bool bottom = false, Guid? maskTarget = null)
        {
            var key = (parent, above, bottom, maskTarget, copying);
            if (_layerValidatedDrop != key)
            {
                _layerValidatedDrop = key;
                _layerDropValid = maskTarget is { } id ? LayerMaskEdits.CanCopy(document, source, id)
                    : LayerPlacement.CanDrop(document, _layerDraggedIDs, parent, above, bottom, copying);
            }
            return _layerDropValid;
        }
        _layerDragText.Text = _layerMaskDragging ? UiText.Get("Copy Mask")
            : UiText.Format(copying ? "Copy {0} layers" : "Move {0} layers", _layerDraggedIDs.Count);
        _layerDragBadge.MaxWidth = Math.Max(40, _layerDragHost.Bounds.Width - 12);
        var badge = (TranslateTransform)_layerDragBadge.RenderTransform!;
        badge.X = Math.Clamp(point.X + 10, 4, Math.Max(4, _layerDragHost.Bounds.Width - _layerDragBadge.DesiredSize.Width - 4));
        badge.Y = Math.Clamp(point.Y + 14, 4, Math.Max(4, _layerDragHost.Bounds.Height - 32));
        if (point.X < 0 || point.X > _layerDragHost.Bounds.Width || point.Y < 0 || point.Y > _layerDragHost.Bounds.Height)
        { _layerDropMarker.IsVisible = false; return; }
        var cells = _layers.GetVisualDescendants().OfType<ListBoxItem>()
            .Select(cell => (Cell: cell, Row: cell.Content as LayerRow, At: cell.TranslatePoint(default, _layerDragHost)))
            .Where(value => value.Row is not null && value.At is not null).OrderBy(value => value.At!.Value.Y).ToList();
        var hit = cells.FirstOrDefault(value => point.Y >= value.At!.Value.Y && point.Y <= value.At.Value.Y + value.Cell.Bounds.Height);
        Rect destination;
        if (hit.Row is { } target)
        {
            var layer = document.Layers.First(item => item.ID == target.ID);
            var y = hit.At!.Value.Y; var height = hit.Cell.Bounds.Height;
            if (_layerMaskDragging)
            {
                if (Valid(null, maskTarget: layer.ID)) _layerDrop = (null, null, false, layer.ID);
                destination = new Rect(5, y, Math.Max(0, _layerDragHost.Bounds.Width - 10), height);
            }
            else if (layer.IsGroup && point.Y > y + height * .25 && point.Y < y + height * .75)
            {
                if (Valid(layer.ID)) _layerDrop = (layer.ID, null, false, null);
                destination = new Rect(5, y, Math.Max(0, _layerDragHost.Bounds.Width - 10), height);
            }
            else
            {
                var index = _rows.IndexOf(layer.ID);
                if (point.Y > y + height / 2) index++;
                var next = index < _rows.Count ? document.Layers.First(item => item.ID == _rows[index]) : null;
                if (Valid(next?.ParentID, next?.ID, next is null))
                    _layerDrop = (next?.ParentID, next?.ID, next is null, null);
                destination = new Rect(5, point.Y > y + height / 2 ? y + height - 1 : y - 1, Math.Max(0, _layerDragHost.Bounds.Width - 10), 3);
            }
        }
        else
        {
            var first = cells.FirstOrDefault(); var last = cells.LastOrDefault();
            var before = first.Row is not null && point.Y < first.At!.Value.Y ? first.Row.ID : (Guid?)null;
            var parent = before is { } id ? document.Layers.First(layer => layer.ID == id).ParentID : null;
            if (!_layerMaskDragging && Valid(parent, before, before is null))
                _layerDrop = (parent, before, before is null, null);
            destination = new Rect(5, before is not null ? first.At!.Value.Y : last.Cell is not null ? last.At!.Value.Y + last.Cell.Bounds.Height : 0,
                Math.Max(0, _layerDragHost.Bounds.Width - 10), 3);
        }
        if (_layerDrop is not null)
        { _layerDropMarker.MoveTo(destination, animate: _layerDropMarker.IsVisible); _layerDropMarker.IsVisible = true; }
        else _layerDropMarker.IsVisible = false;
    }

    private void LayerDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_layerDragSource is not { } source) return;
        if (_layerDragging) { _layerDragKeys = e.KeyModifiers; _layerDragPosition = e.GetPosition(_layerDragHost); UpdateLayerDrop(); }
        var dragged = _layerDragging; var mask = _layerMaskDragging; var ids = _layerDraggedIDs.ToArray();
        var drop = _layerDrop; var keys = _layerDragKeys; var owner = _layerDragDocument;
        CancelLayerDrag(); e.Handled = true;
        if (!ReferenceEquals(_document, owner)) return;
        if (!dragged)
        {
            if (mask) SelectLayerTarget(source, true, keys);
            else if (keys.HasFlag(KeyModifiers.Shift))
            {
                var from = _layers.SelectedIndex; var to = _rows.IndexOf(source);
                SelectLayerRows(_rows.Skip(Math.Max(0, Math.Min(from, to))).Take(Math.Abs(to - from) + 1));
            }
            else if (keys.HasFlag(KeyModifiers.Control))
            {
                var chosen = SelectedLayers.ToHashSet(); if (!chosen.Add(source)) chosen.Remove(source); SelectLayerRows(chosen);
            }
            else SelectLayerTarget(source, false, keys);
            return;
        }
        if (_document is not { } document || drop is not { } target) return;
        if (target.MaskTarget is { } maskTarget) CopyLayerMask(source, maskTarget);
        else
        {
            IReadOnlyList<Guid> placed = [];
            var copying = keys.HasFlag(KeyModifiers.Control);
            Edit(copying ? "Duplicate Layers" : "Move Layers", () =>
            { placed = LayerPlacement.Drop(document, ids, target.Parent, target.Above, target.Bottom, copying); return placed.Count > 0; });
            if (placed.Count > 0)
            {
                if (target.Parent is { } parent) _open.CollapsedGroups.Remove(parent);
                ShowLayers(document); SelectLayerRows(placed); Refresh();
            }
        }
        _canvas.Focus();
    }

    private void CancelLayerDrag()
    {
        var pointer = _layerDragPointer;
        _layerDragSource = null; _layerDragPointer = null; _layerDragging = false; _layerDrop = null;
        _layerDragDocument = null; _layerValidatedDrop = null;
        _layerDragScroll.Stop(); _layerDropMarker.IsVisible = _layerDragBadge.IsVisible = false;
        _layerDragViewer = null; pointer?.Capture(null);
    }

    private void SelectLayerRows(IEnumerable<Guid> ids)
    {
        var wanted = ids.ToHashSet();
        _showingLayers = true;
        try
        {
            _layers.SelectedItems?.Clear();
            foreach (var row in _layerRowItems.Where(row => wanted.Contains(row.ID))) _layers.SelectedItems?.Add(row);
        }
        finally { _showingLayers = false; }
        _open.MaskTarget = null; SynchronizeLayerTarget(); UpdateLayerMenu(); ShowTransformBox(); ShowTransformInspector();
    }

    private void CopyLayerMask(Guid source, Guid target)
    {
        if (_document is not { } document) return;
        if (Edit(document.Layers.FirstOrDefault(layer => layer.ID == target)?.Mask is null ? "Copy Layer Mask" : "Replace Layer Mask",
            () => LayerMaskEdits.Copy(document, source, target))) SelectLayerTarget(target, true, KeyModifiers.None);
    }
}
