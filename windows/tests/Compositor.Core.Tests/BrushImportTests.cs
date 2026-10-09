using System.Buffers.Binary;
using System.Text;
using Compositor.Core.Document;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public class BrushImportTests
{
    private static readonly byte[] Mask = [0, 128, 255, 255, 128, 0];
    public static IEnumerable<object[]> ModernCases =>
        from version in new[] {6, 7, 9, 10} from minor in new[] {1, 2}
        from depth in new[] {8, 16} from compression in new[] {0, 1}
        select new object[] {version, minor, depth, compression};

    [Theory, MemberData(nameof(ModernCases))]
    public void SampledAbrTipsKeepTheirMaskAndHandleRawAndRle(int version, int minor, int depth, int compression)
    {
        var pack = Abr(version, minor, depth, compression);
        var result = BrushImport.ReadAbr(pack, "Pack"); var tip = Assert.Single(result.Tips);
        Assert.Equal(3, tip.Width); Assert.Equal(2, tip.Height); Assert.Equal(Mask, tip.Alpha.ToArray());
        Assert.True(result.StaticTipsOnly); Assert.Equal(0, result.Skipped);
    }

    [Theory]
    [InlineData(1, 8, 0)] [InlineData(1, 8, 1)] [InlineData(1, 16, 0)] [InlineData(1, 16, 1)]
    [InlineData(2, 8, 0)] [InlineData(2, 8, 1)] [InlineData(2, 16, 0)] [InlineData(2, 16, 1)]
    public void LegacySampledAbrTipsKeepSpacingAndUnicodeNames(int version, int depth, int compression)
    {
        using var data = new MemoryStream(); var writer = new Writer(data);
        writer.I32(0); writer.U16(37);
        if (version == 2) { writer.I32(3); data.Write(Encoding.BigEndianUnicode.GetBytes("笔刷\0")); }
        data.Write(new byte[9]); Sample(writer, depth, compression);
        using var pack = new MemoryStream(); var header = new Writer(pack);
        header.U16(version); header.U16(1); header.U16(2); header.I32((int)data.Length); pack.Write(data.ToArray());
        var tip = Assert.Single(BrushImport.ReadAbr(pack.ToArray(), "Pack").Tips);
        Assert.Equal(Mask, tip.Alpha.ToArray()); Assert.Equal(0.37, tip.Spacing);
        if (version == 2) Assert.Equal("笔刷", tip.Name);
    }

    [Fact]
    public void OddMetadataBlocksCanBePaddedOrUnpadded()
    {
        var sample = Abr(6, 1, 8, 0);
        foreach (var padded in new[] {false, true})
        {
            using var pack = new MemoryStream(); pack.Write(sample.AsSpan(0, 4)); var writer = new Writer(pack);
            pack.Write(Encoding.ASCII.GetBytes("8BIMdesc")); writer.I32(3); pack.Write(new byte[3]);
            if (padded) pack.WriteByte(0);
            pack.Write(sample.AsSpan(4)); Assert.Equal(Mask, Assert.Single(BrushImport.ReadAbr(pack.ToArray(), "Pack").Tips).Alpha.ToArray());
        }
    }

    [Fact]
    public void TruncationNeverReturnsAFalseSuccessfulBrush()
    {
        var pack = Abr(6, 2, 16, 1);
        for (var length = 0; length < pack.Length; length++)
        {
            var truncated = pack.AsSpan(0, length).ToArray();
            Assert.ThrowsAny<Exception>(() => BrushImport.ReadAbr(truncated, "Pack"));
        }
    }

    [Fact]
    public void ExcessiveDimensionsAreRejectedBeforeAllocatingPixels()
    {
        var bytes = Abr(6, 1, 8, 0);
        // Header, tagged block, sample length, Pascal ID and ten-byte v1 prefix.
        var bounds = 4 + 12 + 4 + 3 + 10;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(bounds + 12, 4), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => BrushImport.ReadAbr(bytes, "Pack"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void PngUsesAlphaForTransparentImagesAndDarkInkForOpaqueImages(bool transparent)
    {
        using var image = new SKBitmap(3, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        image.SetPixel(0, 0, new SKColor(255, 255, 255, transparent ? (byte)0 : (byte)255));
        image.SetPixel(1, 0, new SKColor(0, 0, 0, transparent ? (byte)128 : (byte)255));
        image.SetPixel(2, 0, new SKColor(128, 128, 128, 255));
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var tip = Assert.Single(BrushImport.ReadPng(data.ToArray(), "PNG").Tips);
        Assert.Equal(transparent ? new byte[] {0, 128, 255} : new byte[] {0, 255, 127}, tip.Alpha.ToArray());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void ImportedTipsWorkIncrementallyWithEraserSelectionTransformsAndOneUndo(bool erase, bool mask)
    {
        using var complete = Picture(mask); using var incremental = Picture(mask);
        var tip = new BrushTip("Pattern", 3, 2, Mask);
        var settings = new BrushSettings(Diameter:19, Opacity:0.4, Erasing:erase, Red:1, Green:0.2, Tip:tip);
        SKPoint[] path = [new(12, 14), new(30, 28), new(39, 17), new(13, 23)];
        var layer = incremental.Layers[0]; var original = layer.Asset; var originalMask = layer.Mask;
        using var prepared = BrushEdits.Prepare(incremental, layer.ID, settings, mask)!;
        foreach (var point in path) prepared.Append(point);
        var history = new DocumentHistory(); history.Begin("Imported brush", incremental, layer.ID);
        Assert.True(prepared.TryCommit(incremental, layer.ID, settings, mask)); history.End(incremental, layer.ID);
        if (mask) Assert.True(BrushEdits.PaintMask(complete, complete.Layers[0].ID, path, settings));
        else Assert.True(BrushEdits.Paint(complete, complete.Layers[0].ID, path, settings));
        byte[] Pixels(CanvasDocument document) => (mask ? document.Layers[0].Mask!.Asset : document.Layers[0].Asset)!.Image.GetPixelSpan().ToArray();
        Assert.Equal(Pixels(complete), Pixels(incremental)); Assert.Equal(1, history.UndoCount);
        var undo = history.Undo()!.Value.Document!.Layers[0]; Assert.Same(original, undo.Asset); Assert.Same(originalMask, undo.Mask);
        Assert.Equal(Pixels(incremental), Pixels(history.Redo()!.Value.Document!));
    }

    [Fact]
    public void RectangularTipCornersAreNotClippedIntoACircle()
    {
        using var document = Picture(false, false); var layer = document.Layers[0];
        var tip = new BrushTip("Square", 4, 4, Enumerable.Repeat((byte)255, 16).ToArray());
        Assert.True(BrushEdits.Paint(document, layer.ID, [new SKPoint(20.5f, 20.5f)], new BrushSettings(Diameter:8, Red:1, Tip:tip)));
        Assert.Equal(new SKColor(255, 0, 0), layer.Asset!.Image.GetPixel(17, 17));
        Assert.NotEqual(new SKColor(255, 0, 0), layer.Asset.Image.GetPixel(15, 15));
    }

    [Fact]
    public void LibraryAndEditableProjectRoundTripKeepTheImportedMaskAndPixels()
    {
        var root = Path.Combine(Path.GetTempPath(), "compositor-brush-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var tip = new BrushTip("中文笔刷", 3, 2, Mask, 0.37); var library = Path.Combine(root, "brushes.json");
            BrushLibrary.Save(library, [tip]); var reloaded = Assert.Single(BrushLibrary.Load(library));
            Assert.Equal(tip.Name, reloaded.Name); Assert.Equal(tip.ID, reloaded.ID); Assert.Equal(tip.Spacing, reloaded.Spacing);
            using var document = Picture(false, false); var layer = document.Layers[0];
            Assert.True(BrushEdits.Paint(document, layer.ID, [new SKPoint(20, 20)], new BrushSettings(Tip:reloaded, Red:1)));
            var project = Path.Combine(root, "brush.comp"); ProjectStore.Save(ProjectSnapshot.FromDocument(document), project);
            using var snapshot = ProjectStore.Load(project); using var loaded = snapshot.ToDocument();
            Assert.Equal(layer.Asset!.Image.GetPixelSpan().ToArray(), loaded.Layers[0].Asset!.Image.GetPixelSpan().ToArray());
            var previous = File.ReadAllBytes(library);
            Assert.Throws<InvalidDataException>(() => BrushLibrary.Save(library, Enumerable.Repeat(tip, 257).ToArray()));
            Assert.Equal(previous, File.ReadAllBytes(library));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidLibraryEntriesAndEmptyTipsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new BrushTip("Empty", 1, 1, new byte[1]));
        Assert.Throws<InvalidDataException>(() => new BrushTip("Wrong size", 3, 2, new byte[1]));
        var file = Path.Combine(Path.GetTempPath(), "compositor-brush-bad-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(file, "{\"Version\":1,\"Tips\":[null]}"); Assert.Throws<InvalidDataException>(() => BrushLibrary.Load(file)); }
        finally { File.Delete(file); }
    }

    private static CanvasDocument Picture(bool mask, bool transformed = true)
    {
        var document = new CanvasDocument(Guid.NewGuid(), 48, 40);
        var image = new SKBitmap(Bitmaps.ColorInfo(48, 40)); image.Erase(new SKColor(70, 120, 180, 190));
        var layer = new ImageLayer(Guid.NewGuid(), ImportedImage.Create(image, "Pixels"), new LayerTransform(0, 0, 48, 40, transformed ? 13 : 0), "Pixels");
        if (mask) layer.Mask = LayerMask.Solid(true);
        document.Layers.Add(layer);
        if (transformed) document.Selection = DocumentSelection.Rectangular(SKRectI.Create(5, 4, 36, 28), 48, 40).WithFeather(2);
        return document;
    }

    private static byte[] Abr(int version, int minor, int depth, int compression)
    {
        using var sample = new MemoryStream(); var entry = new Writer(sample);
        sample.WriteByte(2); sample.Write(Encoding.ASCII.GetBytes("id")); sample.Write(new byte[minor == 1 ? 10 : 264]); Sample(entry, depth, compression);
        using var block = new MemoryStream(); var blockWriter = new Writer(block);
        blockWriter.I32((int)sample.Length); block.Write(sample.ToArray()); while (block.Length % 4 != 0) block.WriteByte(0);
        using var pack = new MemoryStream(); var header = new Writer(pack); header.U16(version); header.U16(minor);
        pack.Write(Encoding.ASCII.GetBytes("8BIMsamp")); header.I32((int)block.Length); pack.Write(block.ToArray()); return pack.ToArray();
    }
    private static void Sample(Writer writer, int depth, int compression)
    {
        writer.I32(0); writer.I32(0); writer.I32(2); writer.I32(3); writer.U16(depth); writer.Stream.WriteByte((byte)compression);
        var raw = depth == 8 ? Mask : Mask.SelectMany(value => new[] {value, value}).ToArray();
        if (compression == 0) writer.Stream.Write(raw);
        else
        {
            var width = 3 * (depth / 8); writer.U16(width + 1); writer.U16(width + 1);
            for (var y = 0; y < 2; y++) { writer.Stream.WriteByte((byte)(width - 1)); writer.Stream.Write(raw.AsSpan(y * width, width)); }
        }
    }
    private sealed class Writer(MemoryStream stream)
    {
        internal MemoryStream Stream => stream;
        internal void U16(int value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value); stream.Write(bytes); }
        internal void I32(int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
    }
}
