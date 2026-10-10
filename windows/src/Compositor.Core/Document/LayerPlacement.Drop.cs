using Compositor.Core.Model;

namespace Compositor.Core.Document;

public static partial class LayerPlacement
{
    /// <summary>Selected folder descendants travel with their root, once, in visible stack order.</summary>
    public static List<Guid> DragRoots(CanvasDocument document, IReadOnlyCollection<Guid> ids)
    {
        var wanted = ids.ToHashSet();
        var byID = document.Layers.ToDictionary(layer => layer.ID);
        bool CarriedByParent(Guid id)
        {
            for (var depth = 0; depth < 64 && byID[id].ParentID is { } parent; depth++, id = parent)
                if (wanted.Contains(parent)) return true;
            return false;
        }
        return document.HierarchyEntries(topFirst: true).Select(entry => entry.Layer.ID)
            .Where(id => wanted.Contains(id) && !CarriedByParent(id)).ToList();
    }

    private static HashSet<Guid> CarriedIDs(CanvasDocument document, IReadOnlyCollection<Guid> roots)
    {
        var carried = roots.ToHashSet();
        foreach (var entry in document.HierarchyEntries())
            if (entry.Layer.ParentID is { } parent && carried.Contains(parent)) carried.Add(entry.Layer.ID);
        return carried;
    }

    public static bool CanDrop(CanvasDocument document, IReadOnlyCollection<Guid> ids, Guid? parent,
        Guid? above = null, bool atBottom = false, bool copying = false)
    {
        var roots = DragRoots(document, ids);
        var byID = document.Layers.ToDictionary(layer => layer.ID);
        if (roots.Count == 0 || ids.Any(id => !byID.ContainsKey(id))) return false;
        var carried = CarriedIDs(document, roots);
        if (parent is { } folder && (carried.Contains(folder) || !byID.TryGetValue(folder, out var targetFolder) || !targetFolder.IsGroup)) return false;
        if (above is { } anchor && (carried.Contains(anchor)
            || document.Layers.All(layer => layer.ID != anchor || layer.ParentID != parent))) return false;
        if (!copying) return true;
        if (document.Layers.Count + carried.Count > MaxLayers) return false;
        long pixels = 0;
        foreach (var layer in document.Layers.Where(layer => carried.Contains(layer.ID)))
        {
            if (layer.Asset is { } image) pixels += (long)image.Width * image.Height;
            if (layer.Mask is { } mask) pixels += (long)mask.Asset.Width * mask.Asset.Height;
        }
        return pixels <= document.RemainingImagePixels;
    }

    /// <summary>Plans the complete move or copy before changing the document, including clipping and folder trees.</summary>
    public static IReadOnlyList<Guid> Drop(CanvasDocument document, IReadOnlyCollection<Guid> ids, Guid? parent,
        Guid? above = null, bool atBottom = false, bool copying = false)
    {
        if (!CanDrop(document, ids, parent, above, atBottom, copying)) return [];
        var roots = DragRoots(document, ids);
        var byID = document.Layers.ToDictionary(layer => layer.ID);
        var rootSet = roots.ToHashSet();
        var parents = new Dictionary<Guid, Guid?>();
        var sources = new Dictionary<Guid, Guid?>();
        var planned = document.Layers.Where(layer => copying || !rootSet.Contains(layer.ID)).ToList();
        var map = new Dictionary<Guid, Guid>();
        var moved = new List<ImageLayer>();
        if (copying)
        {
            var carried = CarriedIDs(document, roots);
            foreach (var id in carried) map[id] = Guid.NewGuid();
            Guid? Remap(Guid? id) => id is { } original ? map.GetValueOrDefault(original, original) : null;
            var copies = document.Layers.Where(layer => carried.Contains(layer.ID)).Select(layer =>
                layer.Copy(map[layer.ID], rootSet.Contains(layer.ID) ? layer.Name + " copy" : layer.Name,
                    rootSet.Contains(layer.ID) ? parent : Remap(layer.ParentID), Remap(layer.MaskSourceID))).ToList();
            var copiesByID = copies.ToDictionary(layer => layer.ID);
            var copiedRoots = roots.Select(id => map[id]).ToHashSet();
            moved.AddRange(roots.Select(id => copiesByID[map[id]]));
            planned.AddRange(copies.Where(layer => !copiedRoots.Contains(layer.ID)));
        }
        else
        {
            moved.AddRange(roots.Select(id => byID[id]));
            foreach (var layer in moved) parents[layer.ID] = parent;
        }
        var insertion = above is { } target ? planned.FindIndex(layer => layer.ID == target) + 1
            : atBottom ? 0 : planned.Count;
        // Input is top first; the document stores siblings bottom first.
        planned.InsertRange(insertion, moved.AsEnumerable().Reverse());

        // A batch inserted into an existing clipping stack adopts it as one run, unless it has a base of its own.
        var placedIDs = moved.Select(layer => layer.ID).ToHashSet();
        var siblings = planned.Where(layer => Parent(planned, parents, layer) == parent).ToList();
        var first = siblings.FindIndex(layer => placedIDs.Contains(layer.ID));
        var last = siblings.FindLastIndex(layer => placedIDs.Contains(layer.ID));
        if (first > 0 && last + 1 < siblings.Count && moved.All(layer => !layer.IsGroup)
            && !moved.Any(layer => layer.MaskSourceID is { } id && placedIDs.Contains(id))
            && siblings[last + 1].MaskSourceID is { } source
            && (siblings[first - 1].ID == source || siblings[first - 1].MaskSourceID == source))
            foreach (var layer in moved) sources[layer.ID] = source;
        ReleaseDetached(planned, parents, sources);
        var unchanged = !copying && document.Layers.SequenceEqual(planned)
            && parents.All(pair => byID[pair.Key].ParentID == pair.Value)
            && sources.All(pair => byID[pair.Key].MaskSourceID == pair.Value);
        if (unchanged || !Adopt(document, planned, parents, sources)) return [];
        return moved.Select(layer => layer.ID).ToArray();
    }
}
