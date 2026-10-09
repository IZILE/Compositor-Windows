using Avalonia.Controls;

namespace Compositor.Desktop;

internal static class AppIcon
{
    internal static WindowIcon Value { get; } = Load();

    private static WindowIcon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Compositor.Desktop.AppIcon.png")
            ?? throw new InvalidOperationException("The application icon is missing.");
        return new WindowIcon(stream);
    }
}
