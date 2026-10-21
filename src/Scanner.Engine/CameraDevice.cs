using System.Runtime.InteropServices.WindowsRuntime;
using SocScanner.Imaging;
using SocScanner.Scanning;
using SocScanner.Settings;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace SocScanner.Engine;

/// <summary>
/// Una cámara (la del portátil, una webcam, una cámara de documentos) con Windows.Media.Capture: imagen
/// en directo para encuadrar la hoja y, al «escanear», la última imagen a la resolución elegida. Las
/// imágenes llegan en BGRA por un lector de fotogramas en memoria.
/// </summary>
public sealed class CameraDevice(ScanDeviceInfo info) : IScanDevice, ILivePreview
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _frameLock = new();
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private DeviceCapabilities? _caps;
    private byte[]? _lastFrame;
    private int _lastWidth, _lastHeight, _lastStride;
    private TaskCompletionSource? _firstFrame;

    public ScanDeviceInfo Info { get; } = info;

    public event Action<byte[], int, int, int>? FrameArrived;

    public static async Task<IReadOnlyList<ScanDeviceInfo>> ListAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        return [.. groups
            .Where(g => g.SourceInfos.Any(IsColor))
            .Select(g => new ScanDeviceInfo(g.Id, g.DisplayName, DeviceKind.Camera))];
    }

    private static bool IsColor(MediaFrameSourceInfo s) =>
        s.SourceKind == MediaFrameSourceKind.Color && s.MediaStreamType is MediaStreamType.VideoPreview or MediaStreamType.VideoRecord;

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(CancellationToken ct = default)
    {
        if (_caps is not null)
            return _caps;
        await _gate.WaitAsync(ct);
        try
        {
            if (_caps is not null)
                return _caps;
            var (capture, source) = await OpenAsync();
            try
            {
                var sizes = source.SupportedFormats
                    .Where(f => f.VideoFormat is { Width: > 0, Height: > 0 })
                    .Select(f => ((int)f.VideoFormat.Width, (int)f.VideoFormat.Height))
                    .Distinct()
                    .OrderByDescending(s => s.Item1 * s.Item2)
                    .ToList();
                _caps = new DeviceCapabilities
                {
                    Sources = [ScanSource.Flatbed],
                    MinDpi = 72,
                    MaxDpi = 72,
                    ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackWhite],
                    CameraResolutions = sizes,
                };
            }
            finally
            {
                capture.Dispose();
            }
            return _caps;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartPreviewAsync(DeviceCapabilities caps, (int Width, int Height)? resolution, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await StopCoreAsync();
            var (capture, source) = await OpenAsync();
            _capture = capture;
            var wanted = resolution ?? caps.CameraResolutions.FirstOrDefault();
            // De los formatos de ese tamaño, el de más imágenes por segundo.
            var format = source.SupportedFormats
                .Where(f => f.VideoFormat.Width == wanted.Width && f.VideoFormat.Height == wanted.Height)
                .OrderByDescending(f => f.FrameRate.Denominator == 0 ? 0 : f.FrameRate.Numerator / (double)f.FrameRate.Denominator)
                .FirstOrDefault();
            if (format is not null)
            {
                try { await source.SetFormatAsync(format); }
                catch (Exception ex) { AppLog.Write($"la cámara no acepta {wanted.Width}x{wanted.Height}: {ex.Message}"); }
            }
            _firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            _reader.FrameArrived += OnFrame;
            var status = await _reader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
                throw new ScanException(status == MediaFrameReaderStartStatus.ExclusiveControlNotAvailable ? "ErrCameraInUse" : "ErrCameraNoFrames", $"lector de la cámara: {status}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnFrame(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
                return;
            using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 ? SoftwareBitmap.Copy(bitmap) : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var data = new byte[stride * h];
            bgra.CopyToBuffer(data.AsBuffer());
            lock (_frameLock)
            {
                _lastFrame = data;
                (_lastWidth, _lastHeight, _lastStride) = (w, h, stride);
            }
            _firstFrame?.TrySetResult();
            FrameArrived?.Invoke(data, w, h, stride);
        }
        catch (Exception ex)
        {
            AppLog.Write($"fotograma de la cámara: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<ScannedPage>> ScanAsync(ScanRequest request, IProgress<int>? progress, CancellationToken ct)
    {
        if (_reader is null)
            await StartPreviewAsync(await GetCapabilitiesAsync(ct), request.CameraResolution, ct);
        if (_firstFrame is { } first)
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(8), ct);
            if (await Task.WhenAny(first.Task, timeout) == timeout)
            {
                ct.ThrowIfCancellationRequested();
                throw new ScanException("ErrCameraNoFrames", "la cámara no da imagen");
            }
        }
        progress?.Report(50);
        byte[] data;
        int w, h, stride;
        lock (_frameLock)
        {
            data = _lastFrame ?? throw new ScanException("ErrCameraNoFrames", "sin fotograma");
            (w, h, stride) = (_lastWidth, _lastHeight, _lastStride);
        }
        var page = await Task.Run(() =>
        {
            var dpi = ScanPlanner.CameraDpi(w, h);
            var image = ImageOps.FromBgra(data, w, h, stride, dpi);
            image = ImageOps.Finish(image, request);
            return new ScannedPage(image, dpi, request.FinalColor);
        }, ct);
        progress?.Report(100);
        return [page];
    }

    private async Task<(MediaCapture Capture, MediaFrameSource Source)> OpenAsync()
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var group = groups.FirstOrDefault(g => g.Id == Info.Id)
            ?? throw new ScanException("ErrDeviceOffline", $"cámara {Info.Name} no encontrada");
        var colorInfo = group.SourceInfos.Where(IsColor).OrderBy(s => s.MediaStreamType == MediaStreamType.VideoRecord ? 0 : 1).First();
        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
        }
        catch (Exception ex) when (ex is not UnauthorizedAccessException && ex.HResult != ScanErrors.AccessDenied)
        {
            // Otra aplicación la tiene: se mira sin poder cambiar la resolución.
            AppLog.Write($"cámara en exclusiva no disponible, se comparte: {ex.Message}");
            capture.Dispose();
            capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                SourceGroup = group,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                StreamingCaptureMode = StreamingCaptureMode.Video,
            });
        }
        catch (Exception)
        {
            capture.Dispose();
            throw;
        }
        if (!capture.FrameSources.TryGetValue(colorInfo.Id, out var source))
        {
            capture.Dispose();
            throw new ScanException("ErrCameraNoFrames", "la cámara no tiene imagen en color");
        }
        return (capture, source);
    }

    public async Task StopPreviewAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_reader is { } reader)
        {
            reader.FrameArrived -= OnFrame;
            try { await reader.StopAsync(); } catch (Exception ex) { AppLog.Write($"parar la cámara: {ex.Message}"); }
            reader.Dispose();
            _reader = null;
        }
        _capture?.Dispose();
        _capture = null;
        _firstFrame = null;
        lock (_frameLock)
            _lastFrame = null;
    }

    public void Dispose()
    {
        _ = Task.Run(StopPreviewAsync);
    }
}
