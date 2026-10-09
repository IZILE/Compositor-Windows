using Compositor.Core.IO;

namespace Compositor.Core.Tests;

public sealed class UserDataTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "compositor-path-check");

    [Fact]
    public void DesktopSingleFileSettingsSurviveChangesToTheExtractionDirectory()
    {
        var desktop = Path.Combine(Root, "Desktop");
        var local = Path.Combine(Root, "Local");
        var exe = Path.Combine(desktop, "Compositor.exe");
        var first = UserData.DefaultDirectory(exe, Path.Combine(Root, "extracted-old"), desktop, local);
        var next = UserData.DefaultDirectory(exe, Path.Combine(Root, "extracted-new"), desktop, local);
        Assert.Equal(Path.Combine(local, "Compositor-Windows", "data"), first);
        Assert.Equal(first, next);
    }

    [Fact]
    public void PortableSingleFileKeepsSettingsBesideTheExecutable()
    {
        var portable = Path.Combine(Root, "Portable");
        var actual = UserData.DefaultDirectory(Path.Combine(portable, "Compositor.exe"),
            Path.Combine(Root, "extracted"), Path.Combine(Root, "Desktop"), Path.Combine(Root, "Local"));
        Assert.Equal(Path.Combine(portable, "data"), actual);
    }

    [Fact]
    public void DevelopmentHostDoesNotWriteSettingsIntoTheDotnetToolchain()
    {
        var app = Path.Combine(Root, "Build");
        var actual = UserData.DefaultDirectory(Path.Combine(Root, "Toolchain", "dotnet.exe"), app,
            Path.Combine(Root, "Desktop"), Path.Combine(Root, "Local"));
        Assert.Equal(Path.Combine(app, "data"), actual);
    }

    [Fact]
    public void InstalledAppUsesTheExistingDesktopSettingsEvenAtACustomInstallLocation()
    {
        var local = Path.Combine(Root, "Local");
        var desktop = Path.Combine(Root, "Desktop");
        var before = UserData.DefaultDirectory(Path.Combine(desktop, "Compositor.exe"), "extracted", desktop, local);
        var after = UserData.DefaultDirectory(Path.Combine(Root, "Installed", "Compositor.exe"), "extracted-new", desktop, local, installed: true);
        Assert.Equal(before, after);
        Assert.Equal(Path.Combine(local, "Compositor-Windows", "data"), after);
    }

    [Fact]
    public void InstallMarkerIsReadBesideTheActualExecutableAndNotInTheExtractionCache()
    {
        var root = Path.Combine(Path.GetTempPath(), "compositor-marker-" + Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(root, "Compositor.exe");
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "compositor.install.json");
        try
        {
            Assert.False(UserData.HasInstallMarker(exe, Path.Combine(root, "extracted")));
            File.WriteAllText(marker, "{}");
            Assert.True(UserData.HasInstallMarker(exe, Path.Combine(root, "extracted-new")));
            Assert.False(UserData.HasInstallMarker(Path.Combine(root, "Portable", "Compositor.exe"), root));
        }
        finally { File.Delete(marker); Directory.Delete(root); }
    }
}
