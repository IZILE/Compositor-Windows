using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace Compositor.Core.IO;

public sealed record ImportedFont(string Name, string FileName, string Hash);

/// <summary>App-local fonts. Project styles refer to their stable names; the system font installation is untouched.</summary>
public sealed class FontLibrary
{
    public const int MaxFonts = 128;
    public const long MaxFontBytes = 64 * 1024 * 1024;
    public const long MaxLibraryBytes = 512 * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, FontLibrary> Libraries = new(StringComparer.OrdinalIgnoreCase);
    public static FontLibrary Current => Libraries.GetOrAdd(UserData.File("fonts"), path => new FontLibrary(path));
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Dictionary<string, SKTypeface> _faces = new(StringComparer.OrdinalIgnoreCase);
    private ImportedFont[] _entries = [];
    public string? LoadError { get; private set; }
    public IReadOnlyList<ImportedFont> Entries { get { lock (_gate) return _entries.ToArray(); } }

    public FontLibrary(string directory)
    {
        _directory = Path.GetFullPath(directory);
        var manifest = Path.Combine(_directory, "fonts.json");
        if (!File.Exists(manifest)) return;
        try
        {
            if (new FileInfo(manifest).Length > 256 * 1024) throw new InvalidDataException("The font catalog is too large.");
            var entries = JsonSerializer.Deserialize<ImportedFont[]>(File.ReadAllBytes(manifest)) ?? [];
            if (entries.Length > MaxFonts || entries.Any(entry => !Valid(entry))
                || entries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length
                || entries.Select(entry => entry.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidDataException("The font catalog is invalid.");
            _entries = entries;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { LoadError = error.Message; }
    }

    /// <summary>Validates the entire batch before publishing its catalog. Reimporting identical bytes is a no-op.</summary>
    public IReadOnlyList<ImportedFont> Import(IEnumerable<string> paths)
    {
        lock (_gate)
        {
            if (LoadError is not null) throw new IOException(LoadError);
            var files = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length > 32) throw new InvalidDataException("Import up to 32 fonts at a time.");
            var planned = _entries.ToList();
            var prepared = new List<(ImportedFont Entry, byte[] Bytes)>();
            var result = new List<ImportedFont>();
            long batchBytes = 0;
            foreach (var path in files)
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension is not (".ttf" or ".otf")) throw new InvalidDataException("Choose a TTF or OTF font.");
                var info = new FileInfo(path);
                if (info.Length < 12 || info.Length > MaxFontBytes || (batchBytes += info.Length) > 128 * 1024 * 1024)
                    throw new InvalidDataException("The font file is too large or invalid.");
                using var input = File.OpenRead(path);
                if (input.Length != info.Length) throw new IOException("The font file changed during import.");
                var bytes = new byte[checked((int)info.Length)];
                input.ReadExactly(bytes);
                if (input.ReadByte() != -1 || !(bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 1, 0, 0 })
                    || bytes.AsSpan(0, 4).SequenceEqual("OTTO"u8))) throw new InvalidDataException("Choose a valid TTF or OTF font.");
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (planned.FirstOrDefault(entry => entry.Hash == hash) is { } known)
                {
                    var stored = Path.Combine(_directory, known.FileName);
                    if (!prepared.Any(font => font.Entry.Hash == hash) && (!File.Exists(stored)
                        || new FileInfo(stored).Length != bytes.Length || Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(stored))) != hash))
                        prepared.Add((known, bytes));
                    result.Add(known); continue;
                }
                using var data = SKData.CreateCopy(bytes);
                using var face = SKTypeface.FromData(data);
                if (face is null || face.GlyphCount == 0) throw new InvalidDataException("That font could not be read.");
                var stem = string.Concat((face.FamilyName + " — " + Path.GetFileNameWithoutExtension(path))
                    .Where(character => !char.IsControl(character)));
                if (stem.Length > 160) stem = stem[..160];
                var name = stem;
                for (var suffix = 2; planned.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); suffix++) name = stem + $" ({suffix})";
                var entry = new ImportedFont(name, hash + extension, hash);
                planned.Add(entry); prepared.Add((entry, bytes)); result.Add(entry);
            }
            if (planned.Count > MaxFonts) throw new InvalidDataException("The font library is full.");
            var total = prepared.Sum(font => (long)font.Bytes.Length) + _entries.Where(entry => !prepared.Any(font => font.Entry.Hash == entry.Hash)).Sum(entry =>
                File.Exists(Path.Combine(_directory, entry.FileName)) ? new FileInfo(Path.Combine(_directory, entry.FileName)).Length : 0);
            if (total > MaxLibraryBytes) throw new InvalidDataException("The font library is full.");
            if (prepared.Count == 0) return result;
            Directory.CreateDirectory(_directory);
            foreach (var font in prepared) AtomicWrite(Path.Combine(_directory, font.Entry.FileName), font.Bytes);
            AtomicWrite(Path.Combine(_directory, "fonts.json"), JsonSerializer.SerializeToUtf8Bytes(planned));
            _entries = planned.ToArray();
            return result;
        }
    }

    /// <summary>Faces remain alive while the renderer's glyph cache uses them. Missing files fall back to system fonts.</summary>
    public SKTypeface? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(font => font.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;
            if (_faces.TryGetValue(entry.Hash, out var cached)) return cached;
            try
            {
                var path = Path.Combine(_directory, entry.FileName);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxFontBytes) return null;
                var bytes = File.ReadAllBytes(path);
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != entry.Hash) return null;
                using var data = SKData.CreateCopy(bytes);
                var face = SKTypeface.FromData(data);
                if (face is null) return null;
                _faces[entry.Hash] = face;
                return face;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    private static bool Valid(ImportedFont? entry) => entry is not null && !string.IsNullOrWhiteSpace(entry.Name)
        && entry.Name.Length <= 200 && !entry.Name.Any(char.IsControl) && entry.Hash is { Length: 64 }
        && entry.Hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f')
        && (entry.FileName == entry.Hash + ".ttf" || entry.FileName == entry.Hash + ".otf");

    private static void AtomicWrite(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
