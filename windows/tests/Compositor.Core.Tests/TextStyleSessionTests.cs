using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class TextStyleSessionTests
{
    [Fact]
    public void CancellingTextEditsPreservesAnExistingRedoChain()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 240);
        var id = TextEdits.Add(document, new LayerTextStyle { Content = "Original", FontName = "Arial", FontSize = 24 }, new SKPoint(20, 30))!.Value;
        var history = new DocumentHistory(); history.Begin("Size", document, id);
        Assert.True(TextEdits.SetStyle(document, id, new LayerTextStyle { Content = "Original", FontName = "Arial", FontSize = 30 }));
        history.End(document, id); document.Adopt(history.Undo()!.Value.Document!);
        Assert.True(history.CanRedo);
        var session = TextSession.Editing(document.Layers.Single()); history.Begin("Edit", document, id);
        Assert.True(session.Type(document, "changed")); Assert.True(session.ChangeStyle(document, style => style.FontSize = 40));
        Assert.True(session.Cancel(document)); history.End(document, id);
        Assert.True(history.CanRedo); Assert.Equal(0, history.UndoCount);
        document.Adopt(history.Redo()!.Value.Document!);
        Assert.Equal(30, document.Layers.Single().Text!.Style.FontSize);
    }

    [Fact]
    public void AnEmptyDraftCanBeStyledWithoutCreatingAnEmptyLayer()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 240);
        var session = TextSession.New(new LayerTextStyle { Content = "", FontName = "Arial" }, new SKPoint(20, 30));
        Assert.True(session.ChangeStyle(document, style => { style.FontSize = 36; style.Tracking = 2; style.Red = 1; }));
        Assert.Empty(document.Layers);
        Assert.Null(session.LayerID);
        Assert.True(session.Type(document, "你好"));
        Assert.Equal(36, document.Layers.Single().Text!.Style.FontSize);
        Assert.Equal(2, document.Layers.Single().Text!.Style.Tracking);
        Assert.Equal(1, document.Layers.Single().Text!.Style.Red);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AStyleChangeKeepsTheWordsCaretAndLayerIdentity(int property)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 240);
        var session = TextSession.New(new LayerTextStyle { Content = "", FontName = "Arial", FontSize = 24 }, new SKPoint(20, 30));
        Assert.True(session.Type(document, "Hello 世界"));
        Assert.True(session.MoveCaret(TextSession.TextMove.Left));
        var caret = session.CaretIndex;
        var layer = session.LayerID;
        Assert.True(session.ChangeStyle(document, style =>
        {
            switch (property)
            {
                case 0: style.FontSize = 38; break;
                case 1: style.Tracking = 4; break;
                case 2: style.Leading = 50; break;
                case 3: style.Alignment = TextAlignment.Right; break;
                case 4: style.Red = 1; style.Blue = .4; break;
            }
            style.Content = "must not replace content";
        }));
        Assert.Equal("Hello 世界", session.Content);
        Assert.Equal(caret, session.CaretIndex);
        Assert.Equal(layer, session.LayerID);
        Assert.Single(document.Layers);
        Assert.True(session.Type(document, "!"));
        Assert.Equal("Hello 世!界", session.Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidOrUnrenderableStyleLeavesTheLayerAndDraftUntouched(int failure)
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 240);
        var session = TextSession.New(new LayerTextStyle { Content = "", FontName = "Arial", FontSize = 24 }, new SKPoint(20, 30));
        Assert.True(session.Type(document, "Hello"));
        var style = session.Style;
        var pixels = document.Layers.Single().Asset!.Image.GetPixelSpan().ToArray();
        Assert.False(session.ChangeStyle(document, wanted =>
        {
            if (failure == 0) wanted.FontSize = double.NaN;
            else if (failure == 1) wanted.FontName = " ";
            else { wanted.FontSize = 2000; wanted.Tracking = 1000; wanted.Content = new string('a', 100_000); wanted.BoxSize = new JsonSize { Width = 20_000, Height = 20_000 }; }
        }));
        Assert.Same(style, session.Style);
        Assert.Equal("Hello", session.Content);
        Assert.Equal(pixels, document.Layers.Single().Asset!.Image.GetPixelSpan().ToArray());
    }

    [Fact]
    public void CancelRestoresMixedFontAndColorRunsAfterLiveStyling()
    {
        using var document = new CanvasDocument(Guid.NewGuid(), 400, 240);
        var original = new LayerTextStyle { Content = "Hello", FontName = "Arial", FontSize = 24,
            ColorRuns = [new() { Location = 1, Length = 2, Red = 1 }],
            FontRuns = [new() { Location = 2, Length = 2, FontName = "Consolas" }] };
        var id = TextEdits.Add(document, original, new SKPoint(20, 30))!.Value;
        var before = document.Clone();
        var history = new DocumentHistory(); history.Begin("Edit Text", document, id);
        var session = TextSession.Editing(document.Layers.Single());
        Assert.True(session.ChangeStyle(document, style => { style.ColorRuns![0].Red = 0; style.FontRuns![0].FontName = "Arial"; style.FontSize = 36; }));
        Assert.Equal(1, original.ColorRuns![0].Red);
        Assert.Equal("Consolas", original.FontRuns![0].FontName);
        Assert.True(session.Cancel(document));
        var restored = document.Layers.Single(layer => layer.ID == id).Text!.Style;
        Assert.Equal(24, restored.FontSize);
        Assert.Equal(1, restored.ColorRuns![0].Red);
        Assert.Equal("Consolas", restored.FontRuns![0].FontName);
        Assert.True(document.SameAs(before));
        history.End(document, id); Assert.Equal(0, history.UndoCount);
    }
}
