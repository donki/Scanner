using SocScanner.Engine;
using SocScanner.Scanning;

namespace SocScanner.Tests;

/// <summary>
/// Con hardware de verdad, solo con <c>SOC_SCAN_INTEGRATION=1</c> (General §8.6): escanea una vez
/// con cada escáner que vea Windows (a 100 ppp, en gris, lo que haya en el cristal) y toma una
/// imagen con la primera cámara. Nada se guarda en disco: las hojas se miran en memoria y se tiran.
/// </summary>
[Collection("Estado global")]
public sealed class IntegrationTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("SOC_SCAN_INTEGRATION") == "1";

    [Fact]
    public async Task Escanea_con_cada_escaner_real()
    {
        if (!Enabled)
            return;
        using var tmp = new TempFolder();
        var scanners = await WinRtScanner.ListAsync();
        Assert.NotEmpty(scanners);
        foreach (var info in scanners)
        {
            using var device = new WinRtScanner(info);
            var caps = await device.GetCapabilitiesAsync();
            Assert.True(caps.MaxDpi >= caps.MinDpi, info.Name);
            Assert.Same(caps, await device.GetCapabilitiesAsync());   // se pregunta una vez
            var request = ScanPlanner.Plan(new ScanQuality { Dpi = 100, Color = ColorMode.Grayscale, Area = PageArea.A5 }, caps, ScanSource.Flatbed, DeviceKind.Scanner);
            var progress = new List<int>();
            IReadOnlyList<ScannedPage> pages;
            try
            {
                pages = await device.ScanAsync(request, new Progress<int>(progress.Add), CancellationToken.None);
            }
            catch (Exception ex) when (ScanErrors.IsTransient(ex))
            {
                // Recién usado por la tanda anterior: ocupado o calentando, como reintenta la aplicación.
                await Task.Delay(TimeSpan.FromSeconds(6));
                pages = await device.ScanAsync(request, new Progress<int>(progress.Add), CancellationToken.None);
            }
            var page = Assert.Single(pages);
            Assert.Equal(System.Windows.Media.PixelFormats.Gray8, page.Image.Format);
            Assert.InRange(page.PixelWidth, 500, 900);   // un A5 (5,8") a 100 ppp, o todo el ancho si el escáner no recorta
            Assert.Equal(100, page.Image.DpiX, 0);
        }
    }

    [Fact]
    public async Task Toma_una_imagen_con_la_primera_camara()
    {
        if (!Enabled)
            return;
        using var tmp = new TempFolder();
        var cameras = await CameraDevice.ListAsync();
        if (cameras.Count == 0)
            return;   // sin cámara no hay nada que probar
        using var camera = new CameraDevice(cameras[0]);
        var caps = await camera.GetCapabilitiesAsync();
        Assert.NotEmpty(caps.CameraResolutions);
        var frames = 0;
        camera.FrameArrived += (_, _, _, _) => Interlocked.Increment(ref frames);
        var smallest = caps.CameraResolutions[^1];
        await camera.StartPreviewAsync(caps, smallest);
        try
        {
            var request = ScanPlanner.Plan(new ScanQuality { Color = ColorMode.BlackWhite }, caps, ScanSource.Flatbed, DeviceKind.Camera, smallest);
            var page = Assert.Single(await camera.ScanAsync(request, null, CancellationToken.None));
            Assert.Equal(System.Windows.Media.PixelFormats.BlackWhite, page.Image.Format);
            Assert.True(frames > 0);
        }
        finally
        {
            await camera.StopPreviewAsync();
        }
    }
}
