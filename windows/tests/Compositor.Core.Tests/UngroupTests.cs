using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using LayerTransform = Compositor.Core.Model.LayerTransform;

namespace Compositor.Core.Tests;

public sealed class UngroupTests
{
    private static ImageLayer Layer(string name, Guid? parent = null, bool group = false) =>
        new(Guid.NewGuid(), null, new LayerTransform(0, 0, 64, 64), name) { ParentID = parent, IsGroup = group };

    [Fact]
    public void ChildrenReplaceTheirFolderBetweenItsSiblingsAndNestedChildrenStayInside()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var below = Layer("below"); var folder = Layer("folder", group: true); var above = Layer("above");
        var first = Layer("first", folder.ID); var nested = Layer("nested", folder.ID, true);
        var leaf = Layer("leaf", nested.ID);
        document.Layers.AddRange([below, folder, first, nested, leaf, above]);
        Assert.True(LayerPlacement.Ungroup(document, folder.ID));
        Assert.Equal(new[] { "below", "first", "nested", "leaf", "above" }, document.HierarchyEntries().Select(entry => entry.Layer.Name));
        Assert.Null(first.ParentID); Assert.Null(nested.ParentID); Assert.Equal(nested.ID, leaf.ParentID);
        LayerHierarchy.Validate(document.Layers.Select(CanvasDocument.Record).ToList());
    }

    [Fact]
    public void UngroupInsideAnotherFolderKeepsChildClippingButReleasesLinksLeftOutside()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var parent = Layer("parent", group: true); var folder = Layer("folder", parent.ID, true);
        var basis = Layer("base", folder.ID); var clipped = Layer("clipped", folder.ID); clipped.MaskSourceID = basis.ID;
        var outside = Layer("outside", parent.ID); outside.MaskSourceID = folder.ID;
        document.Layers.AddRange([parent, folder, basis, clipped, outside]);
        Assert.True(LayerPlacement.Ungroup(document, folder.ID));
        Assert.Equal(parent.ID, basis.ParentID); Assert.Equal(parent.ID, clipped.ParentID);
        Assert.Equal(basis.ID, clipped.MaskSourceID); Assert.Null(outside.MaskSourceID);
        LiveMaskGraph.Validate(document.Layers.Select(CanvasDocument.Record).ToList());
    }

    [Fact]
    public void UngroupIsOneUndoStepAndRestoresTheFoldersAppearance()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var folder = Layer("folder", group: true); folder.Opacity = 0.4; folder.IsVisible = false;
        var child = Layer("child", folder.ID); document.Layers.AddRange([folder, child]);
        var history = new DocumentHistory(); history.Begin("Ungroup", document, folder.ID);
        Assert.True(LayerPlacement.Ungroup(document, folder.ID)); history.End(document, child.ID);
        Assert.Equal(1, child.Opacity); Assert.True(child.IsVisible);
        var snapshot = history.Undo(); Assert.NotNull(snapshot?.Document);
        document.Adopt(snapshot!.Value.Document!);
        Assert.Equal(0.4, document.Layers.Single(layer => layer.ID == folder.ID).Opacity);
        Assert.False(document.Layers.Single(layer => layer.ID == folder.ID).IsVisible);
        Assert.Equal(folder.ID, document.Layers.Single(layer => layer.ID == child.ID).ParentID);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void EmptyFolderCanBeRemovedAndAnImageLayerCannotBeUngrouped()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 64, 64);
        var folder = Layer("empty", group: true); var image = Layer("image"); document.Layers.AddRange([folder, image]);
        Assert.True(LayerPlacement.Ungroup(document, folder.ID));
        Assert.False(LayerPlacement.Ungroup(document, image.ID));
        Assert.Single(document.Layers); Assert.Same(image, document.Layers[0]);
    }
}
