using System.Text.Json;
using Compositor.Core.Document;
using Compositor.Core.Format;
using SkiaSharp;

namespace Compositor.Core.IO;

public static class MaterialLibrary
{
    private sealed record Store<T>(int Version, T[] Items);
    public static T[] Load<T>(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("The material library exceeds 64 MB.");
        var store = JsonSerializer.Deserialize<Store<T>>(File.ReadAllText(path));
        if (store is null || store.Version != 1 || store.Items is null) throw new InvalidDataException("The material library format is not supported.");
        Validate(store.Items); return store.Items;
    }
    public static void Save<T>(string path, T[] items)
    {
        Validate(items);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Store<T>(1, items));
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("The material library exceeds 64 MB.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Validate<T>(T[] items)
    {
        if (items.Length > 2048) throw new InvalidDataException("The material library exceeds 2048 items.");
        foreach (var item in items)
        {
            var valid = item switch {
                GradientPreset g => g.Name is { Length: > 0 and <= 256 } && g.Stops is { Length: >= 2 and <= 1024 }
                    && g.Stops.All(stop => stop is not null && double.IsFinite(stop.Offset) && stop.Offset is >= 0 and <= 1)
                    && g.Stops.Zip(g.Stops.Skip(1)).All(pair => pair.First.Offset <= pair.Second.Offset),
                PatternPreset p => p.Name is { Length: > 0 and <= 256 } && p.Width is >= 1 and <= 2048 && p.Height is >= 1 and <= 2048 && p.Pixels?.LongLength == (long)p.Width*p.Height,
                ShapePreset s => ValidShape(s),
                ColorPreset c => c.Name is { Length: > 0 and <= 256 },
                _ => false
            };
            if (!valid) throw new InvalidDataException("The material library contains an invalid preset.");
        }
    }
    private static bool ValidShape(ShapePreset shape)
    {
        if (shape.Name is not { Length: > 0 and <= 256 } || !Enum.IsDefined(shape.Kind)) return false;
        if (shape.Kind != ShapeKind.Custom) return shape.PathData is null;
        if (shape.PathData is not { Length: > 0 and <= 2 * 1024 * 1024 }) return false;
        using var path = SKPath.ParseSvgPathData(shape.PathData);
        return path is { IsEmpty: false } && path.Points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && Math.Abs(p.X) <= 2 && Math.Abs(p.Y) <= 2);
    }
}
