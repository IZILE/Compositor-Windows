using Compositor.Core.Document;
using Compositor.Core.IO;

namespace Compositor.Core.Tests;

public sealed class SafeSaveTests : ProjectTestBase
{
    [Fact]
    public void SavingCannotReplaceAnUnrelatedFolder()
    {
        var target = PathIn("Personal files.comp");
        Directory.CreateDirectory(target);
        var original = Path.Combine(target, "keep.txt");
        File.WriteAllText(original, "Personal content");
        using var document = LayerPlacement.NewDocument(16, 12)!;
        Assert.Throws<IOException>(() => ProjectStore.Save(ProjectSnapshot.FromDocument(document), target));
        Assert.Equal("Personal content", File.ReadAllText(original));
        Assert.False(File.Exists(Path.Combine(target, ProjectStore.ManifestName)));
        Assert.Single(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void SavingPreservesUnrelatedFilesInsideAValidPackage()
    {
        var target = PathIn("My project.comp");
        using var document = LayerPlacement.NewDocument(16, 12)!;
        ProjectStore.Save(ProjectSnapshot.FromDocument(document), target);
        var notes = Path.Combine(target, "Notes", "Keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        File.WriteAllText(notes, "Keep this note");
        ProjectStore.Save(ProjectSnapshot.FromDocument(document), target);
        Assert.Equal("Keep this note", File.ReadAllText(notes));
        using var reopened = ProjectStore.Load(target);
        Assert.Equal(16, reopened.Manifest.Width);
        Assert.Single(Directory.GetFileSystemEntries(Root));
    }

    [Fact]
    public void SavingCannotReplaceAFile()
    {
        var target = PathIn("Important.comp");
        File.WriteAllText(target, "Keep this file");
        using var document = LayerPlacement.NewDocument(16, 12)!;
        Assert.Throws<IOException>(() => ProjectStore.Save(ProjectSnapshot.FromDocument(document), target));
        Assert.Equal("Keep this file", File.ReadAllText(target));
        Assert.Single(Directory.GetFileSystemEntries(Root));
    }
}
