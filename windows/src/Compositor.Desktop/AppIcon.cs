using Avalonia.Controls;

namespace Compositor.Desktop;

internal static class AppIcon
{
    internal static WindowIcon Value { get; } = Load();

    private static WindowIcon Load()
    {
        // Windows selects the matching small title-bar and taskbar frame from the ICO.
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Compositor.Desktop.AppIcon.ico")
            ?? throw new InvalidOperationException("The application icon is missing.");
        return new WindowIcon(stream);
    }
}
