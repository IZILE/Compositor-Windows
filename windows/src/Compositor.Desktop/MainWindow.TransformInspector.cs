using Avalonia.Input;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Guid? _numberTransformLayer;
    private CanvasDocument? _transformPreview;
    private LayerTransform? _pendingTransformBox;
    private Guid[] _pendingTransformSelection = [];
    private bool _autoSelect;

    private static LayerTransform? InspectorBox(CanvasDocument document, IReadOnlyCollection<Guid> selection)
    {
        var members = TransformEdits.GroupMembers(document, selection);
        return members.Count == 1 ? members[0].Transform : TransformEdits.GroupBox(document, selection);
    }

    private void WireTransformInspector()
    {
        _autoSelect = _tools.TransformAutoSelect;
        _canvas.TransformLockRatio = _tools.LockTransformRatio;
        _canvas.TransformSelectionRequested = SelectForTransform;
        _optionsBar.TransformNumberStarted += BeginNumberTransform;
        _optionsBar.TransformNumberFinished += FinishNumberTransform;
        _optionsBar.TransformNumberChanged += NumberTransformChanged;
        _optionsBar.AutoSelectChanged += value => { _autoSelect = value; KeepSwitches(); };
        _optionsBar.RatioChanged += value => { _canvas.TransformLockRatio = value; KeepSwitches(); };
        _optionsBar.ShowControlsChanged += value => { if (value != _transformShown) ShowTransformControls(); };
        _optionsBar.TransformApplied += ApplyPersistentTransform;
        _optionsBar.TransformCancelled += CancelPersistentTransform;
    }

    private void ShowTransformInspector()
    {
        var document = _transformPreview ?? _document;
        var box = document is null ? null : _pendingTransformBox ?? InspectorBox(document, SelectedLayers);
        var members = document is null ? [] : TransformEdits.GroupMembers(document,
            _transformPreview is not null ? _pendingTransformSelection : SelectedLayers);
        var asset = members.Count == 1 ? members[0].Asset : null;
        _optionsBar.ShowTransform(box, asset?.Width ?? box?.Width ?? 1, asset?.Height ?? box?.Height ?? 1,
            _autoSelect, _transformShown, _canvas.TransformLockRatio, _transformPreview is not null);
    }

    private void BeginNumberTransform()
    {
        if (_transformPreview is not null || _numberTransformLayer is not null || _document is not { } document
            || Selected is not { } id) return;
        _numberTransformLayer = id;
        _history.Begin("Transform", document, id);
    }

    private void FinishNumberTransform()
    {
        if (_numberTransformLayer is not { } id || _document is not { } document) return;
        _numberTransformLayer = null;
        _history.End(document, id);
        Refresh();
    }

    private static bool PlaceInspectorBox(CanvasDocument document, IReadOnlyCollection<Guid> selection, LayerTransform wanted)
    {
        if (!wanted.IsValid || InspectorBox(document, selection) is not { } from) return false;
        var members = TransformEdits.GroupMembers(document, selection);
        if (members.Count == 1)
        {
            if (members[0].Transform == wanted) return false;
            members[0].Transform = wanted; return true;
        }
        var originals = members.ToDictionary(layer => layer.ID, layer => layer.Transform);
        var changed = TransformEdits.Carry(document, originals, from, wanted);
        if (wanted.Sampling != from.Sampling)
            foreach (var member in members) { member.Transform = member.Transform with { Sampling = wanted.Sampling }; changed = true; }
        return changed;
    }

    private void NumberTransformChanged(LayerTransform wanted)
    {
        if (_transformPreview is { } preview)
        {
            PlaceInspectorBox(preview, _pendingTransformSelection, wanted);
            _pendingTransformBox = wanted;
            _canvas.PreviewDocument = preview;
        }
        else if (_document is { } document)
        {
            var automatic = _numberTransformLayer is null;
            if (automatic) BeginNumberTransform();
            PlaceInspectorBox(document, SelectedLayers, wanted);
            if (automatic) FinishNumberTransform();
        }
        ShowTransformBox(); ShowTransformInspector(); _canvas.InvalidateVisual();
    }

    /// <summary>Ctrl+T keeps the proposed transforms in a document copy until Apply or Cancel.</summary>
    private void BeginPersistentTransform()
    {
        FinishNumberTransform();
        if (_transformPreview is not null) return;
        if (_document is not { } document || InspectorBox(document, SelectedLayers) is not { } box) return;
        CloseCameraRaw(); StopPreview(); SetTool(Tool.Move);
        _transformPreview = document.Clone(); _pendingTransformBox = box;
        _pendingTransformSelection = SelectedLayers.ToArray();
        _canvas.PreviewDocument = _transformPreview;
        ShowTransformBox(); ShowTransformInspector();
        _canvas.Focus();
    }

    private void ApplyPersistentTransform()
    {
        if (_transformPreview is not { } preview || _document is not { } document) return;
        FinishNumberTransform();
        var changes = TransformEdits.GroupMembers(preview, _pendingTransformSelection).ToDictionary(layer => layer.ID, layer => layer.Transform);
        _transformPreview = null; _pendingTransformBox = null;
        _pendingTransformSelection = [];
        _canvas.PreviewDocument = null;
        preview.Dispose();
        Edit("Transform", () =>
        {
            var changed = false;
            foreach (var layer in document.Layers)
                if (changes.TryGetValue(layer.ID, out var wanted) && layer.Transform != wanted)
                { layer.Transform = wanted; changed = true; }
            return changed;
        });
        ShowTransformInspector();
    }

    private void CancelPersistentTransform()
    {
        if (_transformPreview is not { } preview) return;
        _transformPreview = null; _pendingTransformBox = null;
        _pendingTransformSelection = [];
        _canvas.PreviewDocument = null;
        preview.Dispose();
        _canvas.CancelDraft();
        ShowTransformBox(); ShowTransformInspector(); _canvas.InvalidateVisual();
    }

    private void SelectForTransform(SKPoint point, KeyModifiers held)
    {
        if (_transformPreview is not null || _document is not { } document) return;
        var picks = _autoSelect != held.HasFlag(KeyModifiers.Control);
        if (!picks && !(held.HasFlag(KeyModifiers.Control) && held.HasFlag(KeyModifiers.Shift))) return;
        var byID = document.Layers.ToDictionary(layer => layer.ID);
        var under = document.HierarchyEntries(topFirst: true).FirstOrDefault(entry => entry.Visible
            && byID[entry.Layer.ID] is { Asset: not null, IsGroup: false } layer && layer.Transform.Contains(point));
        if (under.Layer is null) return;
        FinishNumberTransform();
        var id = under.Layer.ID;
        if (held.HasFlag(KeyModifiers.Control) && held.HasFlag(KeyModifiers.Shift))
        {
            var row = _rows.IndexOf(id);
            if (row >= 0 && _layers.ItemsSource is IEnumerable<Avalonia.Controls.ListBoxItem> items)
            {
                var item = items.ElementAt(row);
                if (_layers.SelectedItems?.Contains(item) == true) _layers.SelectedItems.Remove(item);
                else _layers.SelectedItems?.Add(item);
            }
        }
        else Reselect(id);
        ShowTransformBox(); ShowTransformInspector();
    }
}
