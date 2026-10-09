using Avalonia.Controls;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Compositor.Desktop;

internal static partial class Program
{
    /// <summary>Check the actual Windows window-icon loader, not just the EXE's icon resources.</summary>
    private static int NativeIcons(string output)
    {
        Directory.CreateDirectory(output); Build().SetupWithoutStarting();
        var platform = typeof(WindowIcon).GetProperty("PlatformImpl", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var report = new List<string>(); var frames = new List<object>();
        void Check(bool good, string name)
        { if (!good) throw new InvalidOperationException("NATIVE ICON FAILED: " + name); report.Add("PASS: " + name); }
        object LoadSmall(WindowIcon icon, double scale) => platform.GetValue(icon)!.GetType().GetMethod("LoadSmallIcon")!
            .Invoke(platform.GetValue(icon), [scale])!;
        (bool IsIcon, int Width, int Height, byte[] Pixels) Read(object native)
        {
            var handle = (IntPtr)native.GetType().GetProperty("Handle")!.GetValue(native)!;
            if (!GetIconInfo(handle, out var info)) throw new System.ComponentModel.Win32Exception();
            var dc = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                if (GetObject(info.Color, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var width = bitmap.Width; var height = Math.Abs(bitmap.Height);
                var bitmapInfo = new NativeBitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
                var pixels = new byte[width * height * 4];
                if (GetDIBits(dc, info.Color, 0, (uint)height, pixels, ref bitmapInfo, 0) != height) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return (info.IsIcon, width, height, pixels);
            }
            finally { DeleteDC(dc); DeleteObject(info.Color); DeleteObject(info.Mask); }
        }
        using (var saved = new MemoryStream())
        {
            AppIcon.Value.Save(saved); var bytes = saved.ToArray();
            Check(bytes.Length > 6 && BitConverter.ToUInt16(bytes, 0) == 0 && BitConverter.ToUInt16(bytes, 2) == 1
                && BitConverter.ToUInt16(bytes, 4) == 15, "window icon is the embedded fifteen-frame ICO");
        }
        foreach (var side in new[] { 16, 20, 24, 28, 32, 36, 40, 48, 56, 64 })
        {
            using var native = (IDisposable)LoadSmall(AppIcon.Value, side / 16.0);
            var data = Read(native);
            Check(data.IsIcon && data.Width == side && data.Height == side, $"Windows title-bar loader selects a real {side}px icon");
            var left = side; var top = side; var right = -1; var bottom = -1;
            for (var y = 0; y < side; y++) for (var x = 0; x < side; x++)
                if (data.Pixels[(y * side + x) * 4 + 3] >= 128)
                { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            Check(left > 0 && top > 0 && right < side - 1 && bottom < side - 1
                && Math.Abs((left + right + 1) / 2.0 - side / 2.0) <= 0.5
                && Math.Abs((top + bottom + 1) / 2.0 - side / 2.0) <= 0.5,
                $"{side}px title-bar artwork is centered with its solid body clear of the frame edges ({left},{top},{right},{bottom})");
            using var bitmap = new SKBitmap(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul));
            Marshal.Copy(data.Pixels, 0, bitmap.GetPixels(), data.Pixels.Length);
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(output, $"titlebar-{side}.png")); png.SaveTo(file);
            frames.Add(new { side, body_bounds = new[] { left, top, right + 1, bottom + 1 } });
        }
        // Keep a diagnostic comparison of the previous PNG path. It is not used by the editor.
        using var pngStream = typeof(AppIcon).Assembly.GetManifestResourceStream("Compositor.Desktop.AppIcon.png")!;
        using var previousNative = (IDisposable)LoadSmall(new WindowIcon(pngStream), 1);
        var previous = Read(previousNative);
        File.WriteAllText(Path.Combine(output, "native-icons.json"), JsonSerializer.Serialize(new
        { current = "ICO", frames, previous_png = new { previous.IsIcon, previous.Width, previous.Height } }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(string.Join(Environment.NewLine, report)); return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIconInfo { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint HotX, HotY; public IntPtr Mask, Color; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, ImageSize; public int XResolution, YResolution; public uint Colors, ImportantColors, ColorTable; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap { public int Type, Width, Height, Stride; public ushort Planes, BitCount; public IntPtr Bits; }
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out NativeIconInfo info);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)] private static extern int GetObject(IntPtr bitmap, int count, out NativeBitmap data);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[]? pixels, ref NativeBitmapInfo info, uint usage);
}
