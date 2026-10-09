namespace Compositor.Core.IO;

/// <summary>Installed and desktop copies share stable settings; portable copies keep settings beside the executable.</summary>
public static class UserData
{
    public static string DirectoryPath => Environment.GetEnvironmentVariable("COMPOSITOR_DATA_DIR") is { Length: > 0 } path
        ? Path.GetFullPath(path)
        : DefaultDirectory(Environment.ProcessPath, AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            installed: HasInstallMarker(Environment.ProcessPath, AppContext.BaseDirectory));

    /// <summary>A single-file app may extract libraries into a temporary folder; its settings must stay outside it.</summary>
    public static string DefaultDirectory(string? processPath, string applicationDirectory, string desktopDirectory,
        string localApplicationDirectory, bool installed = false)
    {
        var executableDirectory = processPath is not null
            && Path.GetFileNameWithoutExtension(processPath) != "dotnet"
            ? Path.GetDirectoryName(Path.GetFullPath(processPath)) ?? applicationDirectory
            : applicationDirectory;
        if ((installed || !string.IsNullOrWhiteSpace(desktopDirectory)
            && string.Equals(Path.GetFullPath(executableDirectory).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(desktopDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(localApplicationDirectory))
            return Path.Combine(localApplicationDirectory, "Compositor-Windows", "data");
        return Path.Combine(executableDirectory, "data");
    }

    public static bool HasInstallMarker(string? processPath, string applicationDirectory)
    {
        var directory = processPath is not null && Path.GetFileNameWithoutExtension(processPath) != "dotnet"
            ? Path.GetDirectoryName(Path.GetFullPath(processPath)) ?? applicationDirectory : applicationDirectory;
        return System.IO.File.Exists(Path.Combine(directory, "compositor.install.json"));
    }

    public static string File(string name) => Path.Combine(DirectoryPath, name);
}
