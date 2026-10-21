using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.Imaging;

namespace SocScanner.Scanning;

/// <summary>
/// Escáner de mentira para el modo aislado (General §8.4) y el banco de pruebas: dibuja una hoja con
/// renglones grises y el número de hoja en barras, del tamaño y los ppp pedidos, sin tocar ningún
/// dispositivo. Con <see cref="FailWith"/> simula un fallo del escáner.
/// </summary>
public sealed class SimulatedScanner : IScanDevice
{
    public const string DeviceId = "simulated";

    /// <summary>Más ppp no merece la pena en una prueba: la hoja saldría de decenas de megas.</summary>
    public const int MaxDpi = 300;

    private int _count;

    public ScanDeviceInfo Info { get; }

    /// <summary>Si no es null, el próximo escaneo falla con ese código (y se limpia).</summary>
    public int? FailWith { get; set; }

    /// <summary>Hojas que da el alimentador de una vez.</summary>
    public int FeederPages { get; set; } = 3;

    /// <summary>Lo que tarda cada hoja (para probar la barra y el cancelar).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(150);

    public int ScanCount => _count;

    public SimulatedScanner(string name = "Simulated scanner") => Info = new ScanDeviceInfo(DeviceId, name, DeviceKind.Simulated);

    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => Task.FromResult(new DeviceCapabilities
    {
        Sources = [ScanSource.Flatbed, ScanSource.Feeder],
        MinDpi = 75,
        MaxDpi = MaxDpi,
        ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackWhite],
        Brightness = new ValueRange(-1000, 1000, 10),
        Contrast = new ValueRange(-1000, 1000, 10),
        MaxWidthInches = 8.5,
        MaxHeightInches = 11.7,
    });

    public async Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        if (FailWith is { } code)
        {
            FailWith = null;
            throw new System.Runtime.InteropServices.COMException("simulated failure", code);
        }
        var pages = request.Source == ScanSource.Feeder ? Math.Max(1, FeederPages) : 1;
        var list = new List<ScannedPage>();
        for (var i = 0; i < pages; i++)
        {
            for (var step = 1; step <= 4; step++)
            {
                await Task.Delay(Delay / 4, ct);
                progress?.Report((i * 100 + step * 25) / pages);
            }
            var number = Interlocked.Increment(ref _count);
            var dpi = Math.Min(request.Dpi, MaxDpi);
            var (w, h) = request.AreaInches ?? (8.5, 11.7);
            var image = Draw((int)Math.Round(w * dpi), (int)Math.Round(h * dpi), number, request.DeviceColor);
            image = ImageOps.Finish(image, request);
            list.Add(new ScannedPage(image, dpi, request.FinalColor));
        }
        return list;
    }

    /// <summary>Una hoja blanca con renglones y una tira de barras con su número (para saber cuál es cuál).</summary>
    public static BitmapSource Draw(int width, int height, int number, ColorMode color)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var stride = width * 3;
        var px = new byte[stride * height];
        Array.Fill(px, (byte)0xFF);
        var margin = width / 10;
        var line = Math.Max(2, height / 120);
        for (var y = height / 8; y < height * 7 / 8; y += line * 4)
        {
            var len = width - 2 * margin - ((y / line) % 5) * (width / 20);
            for (var dy = 0; dy < line && y + dy < height; dy++)
                for (var x = margin; x < margin + len; x++)
                {
                    var o = (y + dy) * stride + x * 3;
                    px[o] = 0x40; px[o + 1] = 0x40; px[o + 2] = 0x40;
                }
        }
        // El número de hoja, en barras azules arriba (una barra por hoja hasta 20).
        var bars = Math.Min(number, 20);
        var barW = Math.Max(2, width / 60);
        for (var b = 0; b < bars; b++)
            for (var y = height / 40; y < height / 15; y++)
                for (var x = margin + b * barW * 2; x < margin + b * barW * 2 + barW && x < width; x++)
                {
                    var o = y * stride + x * 3;
                    px[o] = 0xCD; px[o + 1] = 0x25; px[o + 2] = 0x35;   // BGR del índigo
                }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, px, stride);
        bmp.Freeze();
        return color switch
        {
            ColorMode.Grayscale => ImageOps.ToGray(bmp),
            ColorMode.BlackWhite => ImageOps.ToBlackWhite(bmp),
            _ => bmp,
        };
    }

    public void Dispose()
    {
    }
}
