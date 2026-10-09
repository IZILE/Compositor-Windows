using Avalonia.Media;

namespace Compositor.Desktop;

internal static class UiFonts
{
    internal static FontManagerOptions Options() => new()
    {
        FontFallbacks =
        [
            new FontFallback { FontFamily = new FontFamily("Microsoft YaHei UI") },
            new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") },
            new FontFallback { FontFamily = new FontFamily("Segoe UI Symbol") },
        ],
    };
}
