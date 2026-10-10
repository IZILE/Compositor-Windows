using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class LayerDropTests
{
    private static ImageLayer Layer(string name, bool folder = false, Guid? parent = null) =>
        new(Guid.NewGuid(), null, new LayerTransform(0, 0, 64, 64), name) { IsGroup = folder, ParentID = parent };
    private static CanvasDocument Document(params ImageLayer[] layers)
    { var document = new CanvasDocument(Guid.NewGuid(), 64, 64); document.Layers.AddRange(layers); return document; }
    private static string[] Names(CanvasDocument document) => document.Layers.Select(layer => layer.Name).ToArray();

    [Fact]
    public void MultipleLayersMoveAsOneOrderedRunAndUndoTogether()
    {
        var a = Layer("A"); var b = Layer("B"); var c = Layer("C"); var d = Layer("D");
        using var doc = Document(a, b, c, d); var history = new DocumentHistory();
        history.Begin("Move", doc, b.ID);
        Assert.Equal(new[] { c.ID, b.ID }, LayerPlacement.Drop(doc, new[] { b.ID, c.ID }, null, atBottom: true));
        history.End(doc, b.ID);
        Assert.Equal(new[] { "B", "C", "A", "D" }, Names(doc));
        Assert.Equal(1, history.UndoCount); doc.Adopt(history.Undo()!.Value.Document!);
        Assert.Equal(new[] { "A", "B", "C", "D" }, Names(doc));
    }

    [Fact]
    public void FolderAndSelectedChildTravelOnlyOnce()
    {
        var folder = Layer("Folder", true); var child = Layer("Child", parent: folder.ID); var other = Layer("Other");
        using var doc = Document(folder, child, other);
        Assert.Equal(new[] { folder.ID }, LayerPlacement.DragRoots(doc, new[] { child.ID, folder.ID }));
        var copy = Assert.Single(LayerPlacement.Drop(doc, new[] { child.ID, folder.ID }, null, copying: true));
        Assert.Equal(5, doc.Layers.Count);
        Assert.Single(doc.Layers, layer => layer.ParentID == copy);
    }

    [Fact]
    public void FolderCopyRemapsNestedParentsAndInternalClipping()
    {
        var folder = Layer("Folder", true); var inner = Layer("Inner", true, folder.ID);
        var bottom = Layer("Base", parent: inner.ID); var top = Layer("Clip", parent: inner.ID); top.MaskSourceID = bottom.ID;
        using var doc = Document(folder, inner, bottom, top);
        var copied = Assert.Single(LayerPlacement.Drop(doc, new[] { folder.ID }, null, copying: true));
        var copiedInner = Assert.Single(doc.Layers, layer => layer.ParentID == copied);
        var children = doc.Layers.Where(layer => layer.ParentID == copiedInner.ID).ToArray();
        Assert.Equal(2, children.Length); Assert.Equal(children[0].ID, children[1].MaskSourceID);
        Assert.Equal(bottom.ID, doc.Layers.Single(layer => layer.ID == top.ID).MaskSourceID);
    }

    [Fact]
    public void AFolderCannotEnterItselfOrItsDescendant()
    {
        var folder = Layer("Folder", true); var inner = Layer("Inner", true, folder.ID);
        using var doc = Document(folder, inner);
        Assert.False(LayerPlacement.CanDrop(doc, new[] { folder.ID }, inner.ID));
        Assert.Empty(LayerPlacement.Drop(doc, new[] { folder.ID }, folder.ID, copying: true));
        Assert.Equal(2, doc.Layers.Count);
    }

    [Fact]
    public void InvalidAnchorAndUnknownSourceLeaveTheStackUntouched()
    {
        var folder = Layer("Folder", true); var child = Layer("Child", parent: folder.ID); var other = Layer("Other");
        using var doc = Document(folder, child, other); var before = Names(doc);
        Assert.Empty(LayerPlacement.Drop(doc, new[] { other.ID }, null, child.ID));
        Assert.Empty(LayerPlacement.Drop(doc, new[] { Guid.NewGuid() }, null));
        Assert.Equal(before, Names(doc));
    }

    [Fact]
    public void UnchangedDropDoesNotCreateAnUndoStep()
    {
        var a = Layer("A"); var b = Layer("B"); using var doc = Document(a, b); var history = new DocumentHistory();
        history.Begin("Move", doc, b.ID);
        Assert.Empty(LayerPlacement.Drop(doc, new[] { b.ID }, null));
        history.End(doc, b.ID); Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void AWholeBatchInsertedIntoAClippingRunAdoptsItsBase()
    {
        var a = Layer("Base"); var b = Layer("Clip"); b.MaskSourceID = a.ID;
        var c = Layer("C"); var d = Layer("D"); using var doc = Document(a, b, c, d);
        Assert.Equal(2, LayerPlacement.Drop(doc, new[] { c.ID, d.ID }, null, a.ID).Count);
        Assert.Equal(new[] { "Base", "C", "D", "Clip" }, Names(doc));
        Assert.All(doc.Layers.Skip(1), layer => Assert.Equal(a.ID, layer.MaskSourceID));
    }

    [Fact]
    public void ClippingBaseAndItsClippedLayersKeepTheirLinkDuringABatchMove()
    {
        var a = Layer("Base"); var b = Layer("Clip"); b.MaskSourceID = a.ID; var c = Layer("C");
        using var doc = Document(a, b, c);
        Assert.Equal(2, LayerPlacement.Drop(doc, new[] { a.ID, b.ID }, null).Count);
        Assert.Equal(a.ID, doc.Layers.Single(layer => layer.ID == b.ID).MaskSourceID);
    }

    [Fact]
    public void MaskMetadataDoesNotLeakAcrossCopiesOrHistorySnapshots()
    {
        var a = Layer("A"); a.Mask = LayerMask.Solid(true); using var doc = Document(a); using var snapshot = doc.Clone();
        var copy = a.Copy(Guid.NewGuid(), "Copy", null, null);
        copy.Mask!.IsEnabled = false; copy.Mask.IsLinked = false; copy.Mask.Placement = new LayerTransform(5, 7, 10, 12);
        Assert.True(a.Mask!.IsEnabled); Assert.True(a.Mask.IsLinked); Assert.Null(a.Mask.Placement);
        var history = new DocumentHistory(); history.Begin("Disable Mask", doc, a.ID); a.Mask.IsEnabled = false; history.End(doc, a.ID);
        Assert.True(history.Undo()!.Value.Document!.Layers[0].Mask!.IsEnabled);
        Assert.True(snapshot.Layers[0].Mask!.IsEnabled);
    }

    [Fact]
    public void CopyingAMaskKeepsItsCanvasPlacementAndCanBeEditedIndependently()
    {
        var a = Layer("A"); var b = Layer("B"); var folder = Layer("Folder", true);
        a.Transform = new LayerTransform(12, 20, 30, 40); a.Mask = LayerMask.Solid(true);
        using var doc = Document(a, b, folder);
        Assert.True(LayerMaskEdits.Copy(doc, a.ID, b.ID));
        var target = doc.Layers.Single(layer => layer.ID == b.ID);
        Assert.Equal(a.MaskTransform, target.MaskTransform); Assert.NotSame(a.Mask, target.Mask);
        target.Mask!.IsEnabled = false; Assert.True(a.Mask!.IsEnabled);
        Assert.False(LayerMaskEdits.CanCopy(doc, a.ID, a.ID)); Assert.False(LayerMaskEdits.CanCopy(doc, a.ID, folder.ID));
    }
}
