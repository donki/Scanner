using System.Text.Json.Serialization;

namespace SocScanner.Scanning;

/// <summary>Los atajos de calidad; <see cref="Custom"/> usa las opciones a mano.</summary>
public enum QualityPreset
{
    /// <summary>Texto en blanco y negro, 300 ppp: lo más ligero y nítido para papeles con letra.</summary>
    Text,

    /// <summary>Documento en gris, 200 ppp: papeles normales con sellos, firmas o algún dibujo.</summary>
    Document,

    /// <summary>Documento en color, 200 ppp.</summary>
    ColorDocument,

    /// <summary>Foto en color, 600 ppp (o lo más que dé el dispositivo).</summary>
    Photo,

    Custom,
}

/// <summary>
/// Las opciones de calidad de un escaneo. Se guardan en los ajustes y, al escanear, se ajustan a lo
/// que admita el dispositivo con <see cref="ScanPlanner"/>.
/// </summary>
public sealed class ScanQuality
{
    public static readonly int[] StandardDpis = [75, 100, 150, 200, 300, 400, 600, 1200];

    public int Dpi { get; set; } = 200;
    public ColorMode Color { get; set; } = ColorMode.Grayscale;

    /// <summary>-100 a 100; 0 es lo que da el dispositivo.</summary>
    public int Brightness { get; set; }
    public int Contrast { get; set; }

    public PageArea Area { get; set; } = PageArea.A4;

    /// <summary>Calidad JPEG (también la de las fotos dentro del PDF): 40 a 100.</summary>
    public int JpegQuality { get; set; } = 85;

    public static ScanQuality ForPreset(QualityPreset preset) => preset switch
    {
        QualityPreset.Text => new() { Dpi = 300, Color = ColorMode.BlackWhite, JpegQuality = 85 },
        QualityPreset.Document => new() { Dpi = 200, Color = ColorMode.Grayscale, JpegQuality = 80 },
        QualityPreset.ColorDocument => new() { Dpi = 200, Color = ColorMode.Color, JpegQuality = 80 },
        QualityPreset.Photo => new() { Dpi = 600, Color = ColorMode.Color, JpegQuality = 92, Area = PageArea.Full },
        _ => new(),
    };

    public ScanQuality Clone() => (ScanQuality)MemberwiseClone();

    /// <summary>Corrige lo que no valga (al cargar unos ajustes viejos o tocados a mano).</summary>
    public ScanQuality Normalize()
    {
        Dpi = Math.Clamp(Dpi, 50, 4800);
        if (!Enum.IsDefined(Color)) Color = ColorMode.Grayscale;
        if (!Enum.IsDefined(Area)) Area = PageArea.A4;
        Brightness = Math.Clamp(Brightness, -100, 100);
        Contrast = Math.Clamp(Contrast, -100, 100);
        JpegQuality = Math.Clamp(JpegQuality, 40, 100);
        return this;
    }

    [JsonIgnore]
    public bool HasAdjustments => Brightness != 0 || Contrast != 0;

    /// <summary>Medidas de la zona en pulgadas (null: todo el cristal).</summary>
    public static (double Width, double Height)? AreaInches(PageArea area) => area switch
    {
        PageArea.A4 => (210 / 25.4, 297 / 25.4),
        PageArea.Letter => (8.5, 11.0),
        PageArea.A5 => (148 / 25.4, 210 / 25.4),
        _ => null,
    };

    /// <summary>Megapíxeles y megas aproximados de una hoja sin comprimir (para avisar de un escaneo enorme).</summary>
    public static (double Megapixels, double RawMegabytes) Estimate(int dpi, ColorMode color, PageArea area)
    {
        var (w, h) = AreaInches(area) ?? (8.5, 11.7);
        var pixels = w * dpi * h * dpi;
        var bytesPerPixel = color switch { ColorMode.Color => 3.0, ColorMode.Grayscale => 1.0, _ => 1 / 8.0 };
        return (pixels / 1e6, pixels * bytesPerPixel / (1024 * 1024));
    }
}

/// <summary>
/// Ajusta lo que pide el usuario a lo que admite el dispositivo: la resolución a la más cercana del
/// intervalo, el color a uno que tenga (el blanco y negro y el gris se pueden sacar después de una
/// imagen en color), el brillo y el contraste a sus unidades (o a la imagen si no los tiene) y la zona
/// a lo que cabe en el cristal.
/// </summary>
public static class ScanPlanner
{
    public static ScanRequest Plan(ScanQuality quality, DeviceCapabilities caps, ScanSource source, DeviceKind kind, (int Width, int Height)? cameraResolution = null)
    {
        quality = quality.Clone().Normalize();
        var dpi = Math.Clamp(quality.Dpi, Math.Min(caps.MinDpi, caps.MaxDpi), Math.Max(caps.MinDpi, caps.MaxDpi));
        var deviceColor = PickDeviceColor(quality.Color, caps.ColorModes);

        int? deviceBrightness = null, deviceContrast = null;
        var swBrightness = quality.Brightness;
        var swContrast = quality.Contrast;
        if (kind != DeviceKind.Camera)
        {
            if (caps.Brightness is { } b && quality.Brightness != 0)
            {
                deviceBrightness = b.FromPercent(quality.Brightness);
                swBrightness = 0;
            }
            if (caps.Contrast is { } c && quality.Contrast != 0)
            {
                deviceContrast = c.FromPercent(quality.Contrast);
                swContrast = 0;
            }
        }

        (double, double)? area = null;
        if (kind != DeviceKind.Camera && ScanQuality.AreaInches(quality.Area) is var (w, h))
        {
            if (caps.MaxWidthInches is { } mw) w = Math.Min(w, mw);
            if (caps.MaxHeightInches is { } mh) h = Math.Min(h, mh);
            area = (w, h);
        }

        (int, int)? resolution = null;
        if (kind == DeviceKind.Camera && caps.CameraResolutions.Count > 0)
            resolution = cameraResolution is { } wanted && caps.CameraResolutions.Contains(wanted) ? wanted : caps.CameraResolutions[0];

        return new ScanRequest
        {
            Source = caps.Sources.Contains(source) ? source : caps.Sources.FirstOrDefault(),
            Dpi = dpi,
            DeviceColor = deviceColor,
            FinalColor = quality.Color,
            DeviceBrightness = deviceBrightness,
            DeviceContrast = deviceContrast,
            SoftwareBrightness = swBrightness,
            SoftwareContrast = swContrast,
            AreaInches = area,
            CameraResolution = resolution,
        };
    }

    /// <summary>
    /// El color que se pide al dispositivo: el mismo si lo tiene; si no, uno del que se pueda sacar
    /// (el blanco y negro sale del gris o del color; el gris, del color).
    /// </summary>
    public static ColorMode PickDeviceColor(ColorMode wanted, IReadOnlyList<ColorMode> supported)
    {
        if (supported.Count == 0 || supported.Contains(wanted))
            return wanted;
        return wanted switch
        {
            ColorMode.BlackWhite when supported.Contains(ColorMode.Grayscale) => ColorMode.Grayscale,
            _ when supported.Contains(ColorMode.Color) => ColorMode.Color,
            _ => supported[0],
        };
    }

    /// <summary>Las resoluciones de la lista estándar que caben en el dispositivo (siempre al menos una).</summary>
    public static IReadOnlyList<int> DpiChoices(DeviceCapabilities caps)
    {
        var list = ScanQuality.StandardDpis.Where(d => d >= caps.MinDpi && d <= caps.MaxDpi).ToList();
        if (list.Count == 0)
            list.Add(Math.Max(caps.MinDpi, Math.Min(caps.MaxDpi, 200)));
        return list;
    }

    /// <summary>Los ppp que se le suponen a una foto de cámara para que el PDF salga del tamaño de un A4.</summary>
    public static double CameraDpi(int pixelWidth, int pixelHeight)
    {
        var shortSide = Math.Min(pixelWidth, pixelHeight);
        return Math.Max(72, Math.Round(shortSide / (210 / 25.4)));
    }
}
