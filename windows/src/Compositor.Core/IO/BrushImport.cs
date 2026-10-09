using System.Buffers.Binary;
using System.Text;
using Compositor.Core.Document;
using Compositor.Core.IO.PSD;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record BrushImportResult(IReadOnlyList<BrushTip> Tips, int Skipped, bool StaticTipsOnly);

/// <summary>Imports PNG masks and sampled ABR tips, without changing the project format.</summary>
public static class BrushImport
{
    public const int MaxFileBytes = 64 * 1024 * 1024;
    public const int MaxTips = 256;

    public static BrushImportResult Read(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("The brush file is larger than 64 MB.");
        var bytes = File.ReadAllBytes(path);
        return Path.GetExtension(path).Equals(".abr", StringComparison.OrdinalIgnoreCase)
            ? ReadAbr(bytes, Path.GetFileNameWithoutExtension(path)) : ReadPng(bytes, Path.GetFileNameWithoutExtension(path));
    }

    public static BrushImportResult ReadPng(ReadOnlySpan<byte> bytes, string name)
    {
        if (bytes.Length > MaxFileBytes || bytes.Length < 8 || !bytes[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
            throw new InvalidDataException("Choose a PNG image or an ABR brush file.");
        using var stream = new MemoryStream(bytes.ToArray(), false);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("The PNG brush could not be decoded.");
        var info = codec.Info;
        ValidateDimensions(info.Width, info.Height);
        using var image = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (codec.GetPixels(image.Info, image.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The PNG brush is incomplete.");
        var pixels = image.GetPixelSpan(); var alpha = new byte[info.Width * info.Height];
        var transparent = false;
        for (var i = 0; i < alpha.Length; i++) if (pixels[i * 4 + 3] < 255) { transparent = true; break; }
        for (var i = 0; i < alpha.Length; i++)
        {
            var at = i * 4;
            alpha[i] = transparent ? pixels[at + 3] : (byte)(255 - (pixels[at] * 54 + pixels[at + 1] * 183 + pixels[at + 2] * 19 + 128) / 256);
        }
        return new BrushImportResult([new BrushTip(name, info.Width, info.Height, alpha, 0.15)], 0, false);
    }

    // Sample layout checked against ag-psd (MIT), src/abr.ts. Descriptor dynamics are not imported.
    public static BrushImportResult ReadAbr(ReadOnlySpan<byte> bytes, string name)
    {
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("The brush file is larger than 64 MB.");
        var reader = new Reader(bytes); var version = reader.U16(); var countOrMinor = reader.U16();
        var tips = new List<BrushTip>(); var skipped = 0; long pixels = 0;
        if (version is 1 or 2)
        {
            for (var i = 0; i < countOrMinor; i++)
            {
                var type = reader.U16(); var entry = reader.Section(reader.Length());
                if (type != 2) { skipped++; continue; }
                entry.Skip(4); var spacing = entry.U16() / 100.0;
                var title = version == 2 ? entry.Unicode() : $"{name} {i + 1}";
                entry.Skip(9);
                try { var tip = Sample(ref entry, title, spacing); Add(tip); }
                catch (NotSupportedException) { skipped++; }
            }
        }
        else if (version is 6 or 7 or 9 or 10)
        {
            if (countOrMinor is not (1 or 2)) throw new InvalidDataException("This ABR subversion is not supported.");
            while (reader.Remaining > 0)
            {
                if (reader.Remaining < 12 || reader.Signature() != "8BIM") throw new InvalidDataException("The ABR section header is invalid.");
                var tag = reader.Signature(); var length = reader.Length(); var block = reader.Section(length);
                if (tag == "samp")
                {
                    while (block.Remaining > 0)
                    {
                        var sampleLength = block.Length(); var entry = block.Section(sampleLength);
                        block.Skip((4 - sampleLength % 4) % 4);
                        entry.Skip(entry.Byte()); entry.Skip(countOrMinor == 1 ? 10 : 264);
                        try { var tip = Sample(ref entry, $"{name} {tips.Count + skipped + 1}", 0.25); Add(tip); }
                        catch (NotSupportedException) { skipped++; }
                    }
                }
                // Some older packs omit inter-section padding. Accept both only at a valid signature.
                var padding = (4 - length % 4) % 4;
                if (reader.Remaining > 0 && !reader.StartsWith("8BIM") && padding <= reader.Remaining) reader.Skip(padding);
            }
        }
        else throw new InvalidDataException($"ABR version {version} is not supported.");
        if (tips.Count == 0) throw new InvalidDataException("This pack has no supported sampled brush tips. Procedural and dynamic brushes are not supported.");
        return new BrushImportResult(tips, skipped, true);

        void Add(BrushTip tip)
        {
            pixels += (long)tip.Width * tip.Height;
            if (tips.Count >= MaxTips || pixels > MaxFileBytes) throw new InvalidDataException("The decoded brush pack exceeds 256 tips or 64 MB.");
            tips.Add(tip);
        }
    }

    private static BrushTip Sample(ref Reader reader, string name, double spacing)
    {
        var top = reader.I32(); var left = reader.I32(); var bottom = reader.I32(); var right = reader.I32();
        var width = (long)right - left; var height = (long)bottom - top;
        ValidateDimensions(width, height);
        var depth = reader.U16(); var compression = reader.Byte();
        if (depth is not (8 or 16) || compression > 1) throw new NotSupportedException();
        var decoded = PsdChannelCoder.Decode(compression, (int)width * (depth / 8), (int)height, reader.Rest(), false, null);
        var mask = depth == 8 ? decoded : new byte[(int)(width * height)];
        if (depth == 16) for (var i = 0; i < mask.Length; i++) mask[i] = decoded[i * 2];
        return new BrushTip(name, (int)width, (int)height, mask, spacing);
    }

    private static void ValidateDimensions(long width, long height)
    {
        if (width is < 1 or > BrushTip.MaxSide || height is < 1 or > BrushTip.MaxSide)
            throw new InvalidDataException("Brush dimensions must be between 1 and 4096 pixels.");
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;
        internal int Remaining => _data.Length - _position;
        internal ReadOnlySpan<byte> Take(int length)
        {
            if (length < 0 || length > Remaining) throw new InvalidDataException("The ABR brush file is incomplete.");
            var result = _data.Slice(_position, length); _position += length; return result;
        }
        internal void Skip(int count) => Take(count);
        internal int Byte() => Take(1)[0];
        internal int U16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        internal int I32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        internal int Length() { var value = I32(); if (value < 0) throw new InvalidDataException("Invalid ABR block length."); return value; }
        internal Reader Section(int length) => new(Take(length));
        internal string Signature() => Encoding.ASCII.GetString(Take(4));
        internal bool StartsWith(string signature) => Remaining >= 4 && Encoding.ASCII.GetString(_data.Slice(_position, 4)) == signature;
        internal string Unicode() { var count = Length(); if (count > Remaining / 2) throw new InvalidDataException("Invalid brush name."); return Encoding.BigEndianUnicode.GetString(Take(count * 2)).TrimEnd('\0'); }
        internal ReadOnlySpan<byte> Rest() => Take(Remaining);
    }
}
