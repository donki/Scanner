using System.Windows.Media.Imaging;
using SocScanner.Imaging;
using SocScanner.Scanning;
using SocScanner.Settings;
using Windows.Devices.Enumeration;
using Windows.Devices.Scanners;
using Windows.Storage;
using WinRect = Windows.Foundation.Rect;

namespace SocScanner.Engine;

/// <summary>
/// Un escáner con la API de Windows (Windows.Devices.Scanners): vale para cualquiera que Windows
/// reconozca (WIA por USB, WSD y eSCL por red), del cristal o del alimentador. Se pide en mapa de bits
/// (DIB) siempre que se pueda, que es lo que admiten todos y no pierde calidad; el formato final lo
/// pone la aplicación.
/// </summary>
public sealed class WinRtScanner(ScanDeviceInfo info) : IScanDevice
{
    private DeviceCapabilities? _caps;

    public ScanDeviceInfo Info { get; } = info;

    public static async Task<IReadOnlyList<ScanDeviceInfo>> ListAsync()
    {
        var found = await DeviceInformation.FindAllAsync(ImageScanner.GetDeviceSelector());
        return [.. found.Where(d => d.IsEnabled).Select(d => new ScanDeviceInfo(d.Id, d.Name, DeviceKind.Scanner))];
    }

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (_caps is not null)
            return _caps;
        var scanner = await OpenAsync(ct);
        var sources = new List<ScanSource>();
        if (scanner.IsScanSourceSupported(ImageScannerScanSource.Flatbed)) sources.Add(ScanSource.Flatbed);
        if (scanner.IsScanSourceSupported(ImageScannerScanSource.Feeder)) sources.Add(ScanSource.Feeder);
        IImageScannerSourceConfiguration? config = sources.Contains(ScanSource.Flatbed) ? scanner.FlatbedConfiguration
            : sources.Contains(ScanSource.Feeder) ? scanner.FeederConfiguration : null;
        if (config is null)
        {
            // Solo «configurado por el dispositivo» (algunos de red): se escanea con lo suyo.
            _caps = new DeviceCapabilities { Sources = [ScanSource.Flatbed], MinDpi = 100, MaxDpi = 300, ColorModes = [ColorMode.Color] };
            return _caps;
        }
        var modes = new List<ColorMode>();
        if (config.IsColorModeSupported(ImageScannerColorMode.Color)) modes.Add(ColorMode.Color);
        if (config.IsColorModeSupported(ImageScannerColorMode.Grayscale)) modes.Add(ColorMode.Grayscale);
        if (config.IsColorModeSupported(ImageScannerColorMode.Monochrome)) modes.Add(ColorMode.BlackWhite);
        _caps = new DeviceCapabilities
        {
            Sources = sources,
            MinDpi = (int)Math.Round(config.MinResolution.DpiX),
            MaxDpi = (int)Math.Round(config.MaxResolution.DpiX),
            ColorModes = modes.Count > 0 ? modes : [ColorMode.Color],
            Brightness = config.MaxBrightness > config.MinBrightness ? new ValueRange(config.MinBrightness, config.MaxBrightness, (int)Math.Max(1, config.BrightnessStep)) : null,
            Contrast = config.MaxContrast > config.MinContrast ? new ValueRange(config.MinContrast, config.MaxContrast, (int)Math.Max(1, config.ContrastStep)) : null,
            MaxWidthInches = config.MaxScanArea.Width > 0 ? config.MaxScanArea.Width : null,
            MaxHeightInches = config.MaxScanArea.Height > 0 ? config.MaxScanArea.Height : null,
        };
        return _caps;
    }

    public async Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        var scanner = await OpenAsync(ct);
        var source = request.Source == ScanSource.Feeder ? ImageScannerScanSource.Feeder : ImageScannerScanSource.Flatbed;
        if (!scanner.IsScanSourceSupported(source))
            source = scanner.IsScanSourceSupported(ImageScannerScanSource.Flatbed) ? ImageScannerScanSource.Flatbed
                : scanner.IsScanSourceSupported(ImageScannerScanSource.Feeder) ? ImageScannerScanSource.Feeder
                : ImageScannerScanSource.AutoConfigured;
        IImageScannerSourceConfiguration? config = source switch
        {
            ImageScannerScanSource.Flatbed => scanner.FlatbedConfiguration,
            ImageScannerScanSource.Feeder => scanner.FeederConfiguration,
            _ => null,
        };
        if (config is not null)
            Configure(config, request);
        if (config is ImageScannerFeederConfiguration feeder)
        {
            Try("páginas del alimentador", () => feeder.MaxNumberOfPages = 0);   // todas las que haya
            if (feeder.CanScanDuplex) Try("doble cara", () => feeder.Duplex = false);
        }

        var temp = Path.Combine(Path.GetTempPath(), "sOCScanner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(temp);
            var op = scanner.ScanFilesToFolderAsync(source, folder);
            var winrtProgress = new Progress<uint>(p => progress?.Report((int)Math.Min(100, p)));
            ImageScannerScanResult result;
            using (ct.Register(op.Cancel))
                result = await op.AsTask(ct, winrtProgress);
            var pages = new List<ScannedPage>();
            foreach (var file in result.ScannedFiles)
            {
                var image = Decode(file.Path);
                image = ImageOps.WithDpi(image, request.Dpi);
                image = ImageOps.Finish(image, request);
                pages.Add(new ScannedPage(image, request.Dpi, request.FinalColor));
            }
            return pages;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (Exception ex) { AppLog.Write($"no se pudo borrar el temporal {temp}: {ex.Message}"); }
        }
    }

    private static void Configure(IImageScannerSourceConfiguration config, ScanRequest r)
    {
        // Mapa de bits si lo tiene (no pierde nada); si no, PNG, TIFF o JPEG, por ese orden.
        foreach (var f in new[] { ImageScannerFormat.DeviceIndependentBitmap, ImageScannerFormat.Png, ImageScannerFormat.Tiff, ImageScannerFormat.Jpeg })
            if (config.IsFormatSupported(f))
            {
                config.Format = f;
                break;
            }
        var mode = r.DeviceColor switch
        {
            ColorMode.Grayscale => ImageScannerColorMode.Grayscale,
            ColorMode.BlackWhite => ImageScannerColorMode.Monochrome,
            _ => ImageScannerColorMode.Color,
        };
        if (config.IsColorModeSupported(mode))
            Try("modo de color", () => config.ColorMode = mode);
        // Cada opcion dentro de lo que dice el propio escaner; lo que no tiene, no se pide.
        var dpi = Math.Clamp((float)r.Dpi, config.MinResolution.DpiX, Math.Max(config.MinResolution.DpiX, config.MaxResolution.DpiX));
        Try("resolución", () => config.DesiredResolution = new ImageScannerResolution { DpiX = dpi, DpiY = dpi });
        if (r.DeviceBrightness is { } b && config.MaxBrightness > config.MinBrightness)
            Try("brillo", () => config.Brightness = Math.Clamp(b, config.MinBrightness, config.MaxBrightness));
        if (r.DeviceContrast is { } c && config.MaxContrast > config.MinContrast)
            Try("contraste", () => config.Contrast = Math.Clamp(c, config.MinContrast, config.MaxContrast));
        if (r.AreaInches is var (w, h))
        {
            var max = config.MaxScanArea;
            if (max.Width > 0 && max.Height > 0)
                Try("zona de escaneo", () => config.SelectedScanRegion = new WinRect(0, 0, Math.Min(w, max.Width), Math.Min(h, max.Height)));
        }
        if (config.IsAutoCroppingModeSupported(ImageScannerAutoCroppingMode.Disabled))
            Try("recorte automático", () => config.AutoCroppingMode = ImageScannerAutoCroppingMode.Disabled);
    }

    private static BitmapSource Decode(string path)
    {
        using var fs = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        // Una copia de los pixeles, no el fotograma: el fotograma sigue atado al hilo de su decodificador
        // aunque se congele, y al guardar en segundo plano WPF lo rechazaba («different thread owns it»).
        return ImageOps.Materialize(decoder.Frames[0]);
    }

    private async Task<ImageScanner> OpenAsync(CancellationToken ct)
    {
        ImageScanner? scanner;
        try
        {
            scanner = await ImageScanner.FromIdAsync(Info.Id).AsTask(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ScanException(ScanErrors.KeyFor(ex) is "ErrScanUnknown" ? "ErrDeviceOffline" : ScanErrors.KeyFor(ex), $"abrir {Info.Name}: {ex.Message}", ex);
        }
        return scanner ?? throw new ScanException("ErrDeviceOffline", $"{Info.Name} no responde");
    }

    /// <summary>Una opción que el controlador rechaza no impide escanear: se escanea con la suya.</summary>
    private static void Try(string option, Action set)
    {
        try { set(); }
        catch (Exception ex) { AppLog.Write($"opción del escáner no admitida ({option}): {ex.Message.Trim()}"); }
    }

    public void Dispose()
    {
    }
}
