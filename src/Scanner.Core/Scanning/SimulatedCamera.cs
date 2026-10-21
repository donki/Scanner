using SocScanner.Imaging;

namespace SocScanner.Scanning;

/// <summary>
/// Cámara de mentira para el modo aislado (General §8.4) y el banco de pruebas: da imágenes en
/// directo (una hoja gris que se mueve un poco, para ver que llegan) y la última al «escanear», sin
/// encender ninguna cámara de verdad.
/// </summary>
public sealed class SimulatedCamera : IScanDevice, ILivePreview
{
    public const string DeviceId = "simulated-camera";

    private readonly object _lock = new();
    private CancellationTokenSource? _running;
    private byte[]? _last;
    private int _width, _height;
    private int _tick;

    public ScanDeviceInfo Info { get; }

    public event Action<byte[], int, int, int>? FrameArrived;

    /// <summary>Imágenes por segundo de la vista en directo.</summary>
    public int Fps { get; set; } = 15;

    public bool IsRunning => _running is not null;

    public SimulatedCamera(string name = "Simulated camera") => Info = new ScanDeviceInfo(DeviceId, name, DeviceKind.Camera);

    public Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default) => Task.FromResult(new DeviceCapabilities
    {
        Sources = [ScanSource.Flatbed],
        MinDpi = 72,
        MaxDpi = 72,
        ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackWhite],
        CameraResolutions = [(640, 480), (320, 240)],
    });

    public async Task StartPreviewAsync(DeviceCapabilities caps, (int Width, int Height)? resolution, CancellationToken ct = default)
    {
        await StopPreviewAsync();
        var (w, h) = resolution is { } r && caps.CameraResolutions.Contains(r) ? r : caps.CameraResolutions[0];
        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _running = cts;
            (_width, _height) = (w, h);
        }
        Emit();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(1000 / Math.Max(1, Fps), cts.Token);
                    Emit();
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>Una imagen: papel gris claro con una banda oscura que baja un poco en cada una.</summary>
    private void Emit()
    {
        int w, h;
        lock (_lock)
        {
            if (_running is null)
                return;
            (w, h) = (_width, _height);
        }
        var stride = w * 4;
        var data = new byte[stride * h];
        var band = (Interlocked.Increment(ref _tick) * 4) % h;
        for (var y = 0; y < h; y++)
        {
            var v = (byte)(Math.Abs(y - band) < h / 20 ? 40 : 225);
            for (var x = 0; x < w; x++)
            {
                var o = y * stride + x * 4;
                data[o] = v; data[o + 1] = v; data[o + 2] = v; data[o + 3] = 255;
            }
        }
        lock (_lock)
            _last = data;
        FrameArrived?.Invoke(data, w, h, stride);
    }

    public Task StopPreviewAsync()
    {
        lock (_lock)
        {
            _running?.Cancel();
            _running = null;
            _last = null;
        }
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        if (!IsRunning)
            await StartPreviewAsync(await GetCapabilitiesAsync(ct), request.CameraResolution, ct);
        byte[] data;
        int w, h;
        lock (_lock)
        {
            data = _last ?? throw new ScanException("ErrCameraNoFrames", "sin imagen");
            (w, h) = (_width, _height);
        }
        progress?.Report(100);
        var dpi = ScanPlanner.CameraDpi(w, h);
        var image = ImageOps.Finish(ImageOps.FromBgra(data, w, h, w * 4, dpi), request);
        return [new ScannedPage(image, dpi, request.FinalColor)];
    }

    public void Dispose() => _ = StopPreviewAsync();
}
