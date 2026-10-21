using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Tests;

/// <summary>
/// Carpeta temporal propia de cada prueba, con los ajustes y el registro dentro (nunca los del
/// usuario), que se borra al terminar.
/// </summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sOCScanner-tests", Guid.NewGuid().ToString("N"));

    public TempFolder()
    {
        Directory.CreateDirectory(Path);
        ScannerSettings.FilePath = System.IO.Path.Combine(Path, "settings.json");
        ScannerSettings.DefaultFolder = System.IO.Path.Combine(Path, "scans");
        AppLog.Folder = Path;
    }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public static class Images
{
    /// <summary>Una imagen de color con dos mitades: izquierda oscura y derecha clara.</summary>
    public static BitmapSource TwoTone(int width = 40, int height = 20, byte dark = 30, byte light = 220, double dpi = 200)
    {
        var stride = width * 3;
        var px = new byte[stride * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var v = x < width / 2 ? dark : light;
                var o = y * stride + x * 3;
                px[o] = v; px[o + 1] = v; px[o + 2] = v;
            }
        var bmp = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr24, null, px, stride);
        bmp.Freeze();
        return bmp;
    }

    public static ScannedPage Page(ColorMode color = ColorMode.Color, int width = 40, int height = 60, double dpi = 100)
    {
        var image = SimulatedScanner.Draw(width, height, 1, color);
        return new ScannedPage(image, dpi, color);
    }

    public static byte[] Pixels(BitmapSource image)
    {
        var stride = (image.PixelWidth * image.Format.BitsPerPixel + 7) / 8;
        var px = new byte[stride * image.PixelHeight];
        image.CopyPixels(px, stride, 0);
        return px;
    }

    public static BitmapFrame Decode(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        return BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    public static int FrameCount(string path)
    {
        using var fs = System.IO.File.OpenRead(path);
        return BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Count;
    }
}

/// <summary>Un dispositivo de pruebas que devuelve lo que se le diga y apunta lo que se le pidió.</summary>
public sealed class FakeDevice(DeviceCapabilities caps, DeviceKind kind = DeviceKind.Scanner) : IScanDevice
{
    public ScanDeviceInfo Info { get; } = new("fake", "Fake", kind);
    public List<ScanRequest> Requests { get; } = [];
    public Queue<Func<ScanRequest, IReadOnlyList<ScannedPage>>> Results { get; } = new();

    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => Task.FromResult(caps);

    public Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        Requests.Add(request);
        var next = Results.Count > 0 ? Results.Dequeue() : _ => [Images.Page()];
        return Task.FromResult(next(request));
    }

    public void Dispose()
    {
    }
}
