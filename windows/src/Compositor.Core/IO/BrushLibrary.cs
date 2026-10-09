using System.Text.Json;
using Compositor.Core.Document;

namespace Compositor.Core.IO;

/// <summary>User-owned brush masks, outside any project and kept across app upgrades.</summary>
public static class BrushLibrary
{
    public static string DefaultPath => UserData.File("brushes.json");
    private sealed record Entry(string Name, int Width, int Height, double Spacing, byte[] Alpha);
    private sealed record Store(int Version, Entry[] Tips);

    public static IReadOnlyList<BrushTip> Load(string path)
    {
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > BrushImport.MaxFileBytes * 3L / 2)
            throw new InvalidDataException("The brush library is larger than its supported size.");
        var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(path));
        if (store is null || store.Version != 1 || store.Tips is null) throw new InvalidDataException("The brush library format is not supported.");
        if (store.Tips.Length > BrushImport.MaxTips || store.Tips.Any(entry => entry is null || entry.Alpha is null)
            || store.Tips.Sum(entry => (long)entry.Alpha.Length) > BrushImport.MaxFileBytes)
            throw new InvalidDataException("The brush library is incomplete or exceeds its supported size.");
        var tips = store.Tips.Select(entry => new BrushTip(entry.Name, entry.Width, entry.Height, entry.Alpha, entry.Spacing)).ToArray();
        Validate(tips); return tips;
    }

    public static void Save(string path, IReadOnlyList<BrushTip> tips)
    {
        Validate(tips);
        var store = new Store(1, tips.Select(tip => new Entry(tip.Name, tip.Width, tip.Height, tip.Spacing, tip.Alpha.ToArray())).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(store)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(IReadOnlyList<BrushTip> tips)
    {
        if (tips.Count > BrushImport.MaxTips || tips.Sum(tip => (long)tip.Width * tip.Height) > BrushImport.MaxFileBytes)
            throw new InvalidDataException("The brush library exceeds 256 tips or 64 MB.");
    }
}
