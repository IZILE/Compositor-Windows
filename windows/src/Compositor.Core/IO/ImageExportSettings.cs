namespace Compositor.Core.IO;

public enum ImageExportFormat { Png, Jpeg }
public enum PngCompression { Fast, Balanced, Smallest }

public sealed record ImageExportSettings(ImageExportFormat Format, int Quality = ImageWriter.DefaultQuality,
    PngCompression Compression = PngCompression.Balanced);
