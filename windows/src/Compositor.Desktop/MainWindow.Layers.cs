using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Compositor.Core.Document;
using Compositor.Core.Model;
using SelectionMode = Compositor.Core.Document.SelectionMode;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private sealed record LayerRow(Guid ID, LayerCard Card);
    private bool _showingLayers;
    private readonly System.Collections.ObjectModel.ObservableCollection<LayerRow> _layerRowItems = [];

    private void UpdateLayerRows(CanvasDocument document)
    {
        if (_showingLayers) return;
        _layerCount.Text = document.Layers.Count.ToString();
        var selected = _layers.SelectedItems?.OfType<LayerRow>().Select(item => item.ID).ToArray() ?? [];
        var anchor = Selected;
        var oldIndex = _layers.SelectedIndex;
        var live = document.Layers.ToDictionary(layer => layer.ID);
        var rows = new List<LayerRow>();
        _showingLayers = true;
        try
        {
            _open.CollapsedGroups.RemoveWhere(id => !live.TryGetValue(id, out var layer) || !layer.IsGroup);
            foreach (var stale in _open.LayerRows.Keys.Where(id => !live.ContainsKey(id)).ToArray()) _open.LayerRows.Remove(stale);
            foreach (var entry in document.HierarchyEntries(_open.CollapsedGroups, topFirst: true))
            {
                var id = entry.Layer.ID;
                if (!_open.LayerRows.TryGetValue(id, out var row))
                {
                    var card = new LayerCard(() => ToggleLayerVisibility(id), () => ToggleGroupExpansion(id),
                        (mask, keys) => SelectLayerTarget(id, mask, keys), () => { SelectLayerTarget(id, true, KeyModifiers.None); ToggleMaskLink(); });
                    row = new LayerRow(id, card);
                    card.ContextRequested += (_, _) =>
                    {
                        if (!SelectedLayers.Contains(id)) SelectLayerTarget(id, false, KeyModifiers.None);
                        card.ContextMenu = LayerContextMenu();
                    };
                    _open.LayerRows[id] = row;
                }
                row.Card.Configure(document, live[id], entry.Depth, entry.Visible, _open.CollapsedGroups.Contains(id));
                rows.Add(row);
            }
            _rows.Clear(); _rows.AddRange(rows.Select(row => row.ID));
            _layers.ItemTemplate ??= new Avalonia.Controls.Templates.FuncDataTemplate<LayerRow>((row, _) => row?.Card);
            if (!ReferenceEquals(_layers.ItemsSource, _layerRowItems))
            {
                _layers.ItemsSource = _layerRowItems;
            }
            if (!_layerRowItems.SequenceEqual(rows))
            {
                // Keep the collection and surviving containers attached when a folder expands or collapses.
                for (var index = 0; index < rows.Count; index++)
                {
                    if (index < _layerRowItems.Count && ReferenceEquals(_layerRowItems[index], rows[index])) continue;
                    var existing = _layerRowItems.IndexOf(rows[index]);
                    if (existing >= 0) _layerRowItems.Move(existing, index);
                    else _layerRowItems.Insert(index, rows[index]);
                }
                while (_layerRowItems.Count > rows.Count) _layerRowItems.RemoveAt(_layerRowItems.Count - 1);
                var remaining = selected.Where(_rows.Contains).ToHashSet();
                _layers.SelectedItems?.Clear();
                if (anchor is { } active && remaining.Contains(active)) _layers.SelectedItems?.Add(rows[_rows.IndexOf(active)]);
                foreach (var row in rows.Where(row => remaining.Contains(row.ID) && !_layers.SelectedItems!.Contains(row)))
                    _layers.SelectedItems?.Add(row);
                if (remaining.Count == 0) _layers.SelectedIndex = rows.Count > 0 ? Math.Clamp(oldIndex, 0, rows.Count - 1) : -1;
            }
        }
        finally { _showingLayers = false; }
        SynchronizeLayerTarget();
    }

    private void LayerSelectionChanged()
    {
        if (_showingLayers) return;
        FinishNumberTransform(); ApplyPersistentTransform();
        SynchronizeLayerTarget(); UpdateLayerMenu(); ShowTransformBox(); ShowTransformInspector();
        _canvas.InvalidateVisual();
    }

    private void SynchronizeLayerTarget()
    {
        if (_open.MaskTarget is { } target && (target != Selected || SelectedLayers.Count != 1
            || _document?.Layers.FirstOrDefault(layer => layer.ID == target)?.Mask is null)) _open.MaskTarget = null;
        _options.PaintOnMask = _open.MaskTarget is not null;
        _paintOnMask.IsChecked = _options.PaintOnMask;
        SynchronizeMaskView();
        var active = SelectedLayers.Count == 1 ? Selected : null;
        foreach (var (id, row) in _open.LayerRows) row.Card.ShowTarget(id == active, _options.PaintOnMask);
        RefreshOptionsBar();
    }

    private void ToggleLayerVisibility(Guid id)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(layer => layer.ID == id) is not { } layer) return;
        Edit(layer.IsVisible ? "Hide Layer" : "Show Layer", () => LayerEdits.SetVisible(document, id, !layer.IsVisible));
    }

    private void ToggleGroupExpansion(Guid id)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(layer => layer.ID == id) is not { IsGroup: true }) return;
        FinishNumberTransform(); ApplyPersistentTransform();
        var collapse = _open.CollapsedGroups.Add(id);
        if (!collapse) _open.CollapsedGroups.Remove(id);
        var hiddenTarget = collapse && TransformEdits.GroupMembers(document, [id]).Any(layer => SelectedLayers.Contains(layer.ID));
        ShowLayers(document);
        if (hiddenTarget) _layers.SelectedIndex = _rows.IndexOf(id);
        Refresh();
    }

    private void SelectLayerTarget(Guid id, bool mask, KeyModifiers keys)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(layer => layer.ID == id) is not { } layer) return;
        if (keys.HasFlag(KeyModifiers.Control))
        {
            var mode = keys.HasFlag(KeyModifiers.Alt) ? SelectionMode.Subtract : keys.HasFlag(KeyModifiers.Shift) ? SelectionMode.Add : SelectionMode.Replace;
            Change(mask ? "Select Mask's Black Areas" : "Select Layer's Pixels", current => mask
                ? SelectionEdits.SelectMaskDark(current, id, mode) : SelectionEdits.SelectLayerPixels(current, id, mode));
            return;
        }
        FinishNumberTransform(); ApplyPersistentTransform();
        _layers.SelectedIndex = _rows.IndexOf(id);
        _open.MaskTarget = mask && layer.Mask is not null ? id : null;
        if (mask && keys.HasFlag(KeyModifiers.Alt) && layer.Mask is not null) _open.ViewsMaskAlone = !_open.ViewsMaskAlone;
        else if (!mask) _open.ViewsMaskAlone = false;
        if (mask && keys.HasFlag(KeyModifiers.Shift)) ToggleMask();
        SynchronizeLayerTarget(); UpdateLayerMenu();
        _canvas.Focus();
    }

    private void DeleteLayerOrMask()
    {
        if (_options.PaintOnMask) DeleteMask(); else DeleteLayer();
    }

    private void UngroupSelected()
    {
        if (_document is not { } document || Selected is not { } id) return;
        var children = document.Layers.Where(layer => layer.ParentID == id).Select(layer => layer.ID).ToHashSet();
        if (!Edit("Ungroup Layers", () => LayerPlacement.Ungroup(document, id))) return;
        _layers.SelectedItems?.Clear();
        foreach (var row in _layers.Items.OfType<LayerRow>().Where(row => children.Contains(row.ID))) _layers.SelectedItems?.Add(row);
        Refresh();
    }

    private ContextMenu LayerContextMenu()
    {
        var layer = _document?.Layers.FirstOrDefault(item => item.ID == Selected);
        var menu = new ContextMenu();
        menu.Items.Add(Command("Duplicate Layer", DuplicateLayer));
        menu.Items.Add(Command("Rename…", () => _ = RenameLayer()));
        menu.Items.Add(Command(_options.PaintOnMask ? "Delete Mask" : "Delete Layer", DeleteLayerOrMask));
        menu.Items.Add(new Separator());
        var clipping = Command(layer?.MaskSourceID is null ? "Create Clipping Mask" : "Release Clipping Mask", ToggleClipping);
        clipping.IsEnabled = layer is not null && _document is { } doc && LayerMaskEdits.CanToggle(doc, layer.ID); menu.Items.Add(clipping);
        menu.Items.Add(Command("Group Selected Layers", GroupSelected));
        if (layer?.IsGroup == true) menu.Items.Add(Command("Ungroup Layers", UngroupSelected));
        var move = Command("Move Out of Folder", MoveOutOfFolder); move.IsEnabled = layer?.ParentID is not null; menu.Items.Add(move);
        menu.Items.Add(new Separator());
        var add = new MenuItem { Header = UiText.Get("Add Mask"), IsEnabled = layer is { Mask: null } };
        add.Items.Add(Command("Reveal All (White)", () => AddMask(true, useSelection: false)));
        add.Items.Add(Command("Hide All (Black)", () => AddMask(false, useSelection: false)));
        if (_document?.Selection.Path is not null)
        {
            add.Items.Add(Command("Reveal Selection", () => AddMask(true)));
            add.Items.Add(Command("Hide Selection", () => AddMask(false)));
        }
        menu.Items.Add(add);
        if (layer?.Mask is not null)
        {
            menu.Items.Add(Command("Copy Mask", () => { if (_document is { } current) _copiedMask = (current, layer.ID); }));
            menu.Items.Add(Command(layer.Mask.IsEnabled ? "Disable Mask" : "Enable Mask", ToggleMask));
            menu.Items.Add(Command("Delete Mask", DeleteMask));
            menu.Items.Add(Command(layer.Mask.IsLinked ? "Unlink Mask" : "Link Mask", ToggleMaskLink));
        }
        var paste = Command("Paste Mask", () => { if (_copiedMask is { } copied && Selected is { } target) CopyLayerMask(copied.ID, target); });
        paste.IsEnabled = _copiedMask is { } saved && ReferenceEquals(saved.Document, _document)
            && layer is not null && LayerMaskEdits.CanCopy(saved.Document, saved.ID, layer.ID);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(Command(layer?.IsVisible == false ? "Show Layer" : "Hide Layer", () => { if (Selected is { } id) ToggleLayerVisibility(id); }));
        return menu;
    }
}
