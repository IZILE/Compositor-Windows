using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class FontLibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Compositor-font-tests-" + Guid.NewGuid().ToString("N"));
    private string Font(string name = "sample.ttf")
    {
        Directory.CreateDirectory(_folder);
        using var face = SKTypeface.FromFamilyName("Arial"); using var stream = face.OpenStream(); using var data = SKData.Create(stream);
        var path = Path.Combine(_folder, name); File.WriteAllBytes(path, data.ToArray()); return path;
    }
    [Fact]
    public void ImportedFontHasAStableNameAndCanBeResolvedAfterRestart()
    {
        var library = new FontLibrary(Path.Combine(_folder, "library")); var font = Assert.Single(library.Import(new[] { Font() }));
        Assert.Contains("sample", font.Name); Assert.NotNull(library.Resolve(font.Name));
        var reopened = new FontLibrary(Path.Combine(_folder, "library"));
        Assert.Equal(font, Assert.Single(reopened.Entries)); Assert.NotNull(reopened.Resolve(font.Name));
    }
    [Fact]
    public void IdenticalBytesUnderDifferentFilenamesAreDeduplicated()
    {
        var library = new FontLibrary(Path.Combine(_folder, "library"));
        var first = Assert.Single(library.Import(new[] { Font() }));
        Assert.Equal(first, Assert.Single(library.Import(new[] { Font("renamed.ttf") })));
        Assert.Single(library.Entries);
    }
    [Fact]
    public void AnInvalidFileInABatchCannotPublishHalfAFontCatalog()
    {
        var valid = Font(); var invalid = Path.Combine(_folder, "broken.otf"); File.WriteAllText(invalid, "This is not a font.");
        var library = new FontLibrary(Path.Combine(_folder, "library"));
        Assert.Throws<InvalidDataException>(() => library.Import(new[] { valid, invalid })); Assert.Empty(library.Entries);
        Assert.False(File.Exists(Path.Combine(_folder, "library", "fonts.json")));
    }
    [Fact]
    public void CatalogPathsCannotEscapeTheFontDirectory()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "fonts.json"), "[{\"Name\":\"Bad\",\"FileName\":\"../outside.ttf\",\"Hash\":null}]");
        var library = new FontLibrary(_folder); Assert.NotNull(library.LoadError); Assert.Null(library.Resolve("Bad"));
        Assert.Throws<IOException>(() => library.Import(new[] { Font() }));
    }
    [Fact]
    public void MissingAndTamperedFontFilesFallBackSafely()
    {
        var library = new FontLibrary(Path.Combine(_folder, "library")); var font = Assert.Single(library.Import(new[] { Font() }));
        File.WriteAllText(Path.Combine(_folder, "library", font.FileName), "changed");
        Assert.Null(library.Resolve(font.Name)); File.Delete(Path.Combine(_folder, "library", font.FileName));
        Assert.Null(library.Resolve(font.Name));
    }
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); }
}
