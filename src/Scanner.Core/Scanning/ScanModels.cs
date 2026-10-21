using System.Windows.Media.Imaging;

namespace SocScanner.Scanning;

/// <summary>Qué clase de dispositivo es: un escáner, una cámara o el escáner simulado del modo aislado.</summary>
public enum DeviceKind
{
    Scanner,
    Camera,
    Simulated,
}

/// <summary>De dónde coge la hoja un escáner.</summary>
public enum ScanSource
{
    /// <summary>El cristal (cama plana).</summary>
    Flatbed,

    /// <summary>El alimentador automático (varias hojas de una vez).</summary>
    Feeder,
}

/// <summary>Color de la imagen.</summary>
public enum ColorMode
{
    Color,
    Grayscale,

    /// <summary>Blanco y negro puro (1 bit): el texto, en ficheros muy pequeños.</summary>
    BlackWhite,
}

/// <summary>Tamaño de la zona que se escanea.</summary>
public enum PageArea
{
    /// <summary>Todo el cristal.</summary>
    Full,
    A4,
    Letter,
    A5,
}

/// <summary>Un dispositivo de la lista: su identificador de Windows, su nombre y qué es.</summary>
public sealed record ScanDeviceInfo(string Id, string Name, DeviceKind Kind);

/// <summary>Lo que sabe hacer un dispositivo, tal como lo cuenta Windows.</summary>
public sealed record DeviceCapabilities
{
    public IReadOnlyList<ScanSource> Sources { get; init; } = [ScanSource.Flatbed];
    public int MinDpi { get; init; } = 75;
    public int MaxDpi { get; init; } = 600;
    public IReadOnlyList<ColorMode> ColorModes { get; init; } = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackWhite];

    /// <summary>Brillo y contraste que entiende el dispositivo (null: no se pueden tocar en él y se hacen en la imagen).</summary>
    public ValueRange? Brightness { get; init; }
    public ValueRange? Contrast { get; init; }

    /// <summary>Lo más grande que cabe en el cristal, en pulgadas (null si no lo dice).</summary>
    public double? MaxWidthInches { get; init; }
    public double? MaxHeightInches { get; init; }

    /// <summary>Las resoluciones de una cámara (ancho × alto), de mayor a menor. Vacío en un escáner.</summary>
    public IReadOnlyList<(int Width, int Height)> CameraResolutions { get; init; } = [];
}

/// <summary>Un intervalo entero con su paso (el brillo de un escáner va de -1000 a 1000, el de otro de -50 a 50).</summary>
public sealed record ValueRange(int Min, int Max, int Step = 1)
{
    /// <summary>
    /// Lleva un valor de -100 a 100 (lo que enseña la aplicación) al intervalo del dispositivo: 0 es
    /// el centro, ±100 los extremos, redondeado a su paso.
    /// </summary>
    public int FromPercent(int percent)
    {
        percent = Math.Clamp(percent, -100, 100);
        var center = (Min + Max) / 2.0;
        var half = (Max - Min) / 2.0;
        var raw = center + percent / 100.0 * half;
        var step = Math.Max(1, Step);
        var snapped = Min + Math.Round((raw - Min) / step) * step;
        return (int)Math.Clamp(snapped, Min, Max);
    }
}

/// <summary>Lo que se le pide al dispositivo en un escaneo, ya ajustado a lo que admite.</summary>
public sealed record ScanRequest
{
    public ScanSource Source { get; init; } = ScanSource.Flatbed;
    public int Dpi { get; init; } = 200;

    /// <summary>El color que se le pide al dispositivo (puede no ser el final: el blanco y negro se puede hacer después).</summary>
    public ColorMode DeviceColor { get; init; } = ColorMode.Color;

    /// <summary>El color que tendrá la imagen guardada.</summary>
    public ColorMode FinalColor { get; init; } = ColorMode.Color;

    /// <summary>Brillo y contraste en las unidades del dispositivo (null: no se tocan en él).</summary>
    public int? DeviceBrightness { get; init; }
    public int? DeviceContrast { get; init; }

    /// <summary>Brillo y contraste que se aplican a la imagen ya escaneada (-100 a 100; 0 = nada).</summary>
    public int SoftwareBrightness { get; init; }
    public int SoftwareContrast { get; init; }

    /// <summary>Zona en pulgadas desde la esquina (null: todo el cristal).</summary>
    public (double Width, double Height)? AreaInches { get; init; }

    /// <summary>Resolución pedida a una cámara (null: la mayor).</summary>
    public (int Width, int Height)? CameraResolution { get; init; }
}

/// <summary>Una hoja escaneada: la imagen (congelada, se puede usar desde cualquier hilo), sus ppp y su color.</summary>
public sealed record ScannedPage(BitmapSource Image, double Dpi, ColorMode Color)
{
    public int PixelWidth => Image.PixelWidth;
    public int PixelHeight => Image.PixelHeight;
}

/// <summary>Un fallo previsible del dispositivo, con la clave de <c>Loc</c> de su explicación (General §6.9).</summary>
public sealed class ScanException(string messageKey, string detail, Exception? inner = null) : Exception(detail, inner)
{
    public string MessageKey { get; } = messageKey;
}

/// <summary>Un dispositivo del que se pueden sacar hojas: un escáner, una cámara o el simulado.</summary>
public interface IScanDevice : IDisposable
{
    ScanDeviceInfo Info { get; }

    Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Saca las hojas (una del cristal o de la cámara; todas las del alimentador). Los fallos
    /// previsibles salen como <see cref="ScanException"/>.
    /// </summary>
    Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct);
}

/// <summary>Un dispositivo con imagen en directo (las cámaras).</summary>
public interface ILivePreview
{
    /// <summary>Llega cada imagen nueva (en el hilo de la cámara): BGRA de 32 bits, ancho, alto y bytes por fila.</summary>
    event Action<byte[], int, int, int>? FrameArrived;

    Task StartPreviewAsync(DeviceCapabilities caps, (int Width, int Height)? resolution, CancellationToken ct = default);

    Task StopPreviewAsync();
}
