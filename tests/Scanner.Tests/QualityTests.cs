using SocScanner.Scanning;

namespace SocScanner.Tests;

/// <summary>Calidades, su ajuste a lo que admite cada dispositivo y los fallos de los escáneres.</summary>
public sealed class QualityTests
{
    private static DeviceCapabilities Multifuncion(bool usb) => usb
        ? new() { MinDpi = 100, MaxDpi = 1200, Brightness = new(-50, 50), Contrast = new(-50, 50), MaxWidthInches = 8.5, MaxHeightInches = 11.69 }
        : new() { MinDpi = 100, MaxDpi = 300, Brightness = new(-1000, 1000), Contrast = new(-1000, 1000), MaxWidthInches = 8.5, MaxHeightInches = 11.7 };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1000)]
    [InlineData(-100, -1000)]
    [InlineData(50, 500)]
    [InlineData(250, 1000)]   // fuera de rango: al tope
    [InlineData(-300, -1000)]
    public void FromPercent_lleva_el_porcentaje_al_intervalo_del_dispositivo(int percent, int expected) =>
        Assert.Equal(expected, new ValueRange(-1000, 1000).FromPercent(percent));

    [Fact]
    public void FromPercent_respeta_el_paso_y_los_intervalos_no_centrados()
    {
        Assert.Equal(20, new ValueRange(-50, 50, 10).FromPercent(33));   // 16,5 se queda en el paso de 10 más cercano
        Assert.Equal(50, new ValueRange(0, 100).FromPercent(0));
        Assert.Equal(100, new ValueRange(0, 100).FromPercent(100));
        Assert.Equal(0, new ValueRange(0, 100, 0).FromPercent(-100));   // paso 0 se trata como 1
    }

    [Fact]
    public void Los_atajos_tienen_valores_con_sentido()
    {
        Assert.Equal((300, ColorMode.BlackWhite), (ScanQuality.ForPreset(QualityPreset.Text).Dpi, ScanQuality.ForPreset(QualityPreset.Text).Color));
        Assert.Equal((200, ColorMode.Grayscale), (ScanQuality.ForPreset(QualityPreset.Document).Dpi, ScanQuality.ForPreset(QualityPreset.Document).Color));
        Assert.Equal(ColorMode.Color, ScanQuality.ForPreset(QualityPreset.ColorDocument).Color);
        Assert.Equal(600, ScanQuality.ForPreset(QualityPreset.Photo).Dpi);
        Assert.Equal(PageArea.Full, ScanQuality.ForPreset(QualityPreset.Photo).Area);
        Assert.Equal(200, ScanQuality.ForPreset(QualityPreset.Custom).Dpi);
    }

    [Fact]
    public void Normalize_corrige_lo_que_no_vale_y_Clone_es_independiente()
    {
        var q = new ScanQuality { Dpi = 99999, Color = (ColorMode)42, Area = (PageArea)9, Brightness = -500, Contrast = 500, JpegQuality = 3 }.Normalize();
        Assert.Equal(4800, q.Dpi);
        Assert.Equal(ColorMode.Grayscale, q.Color);
        Assert.Equal(PageArea.A4, q.Area);
        Assert.Equal((-100, 100, 40), (q.Brightness, q.Contrast, q.JpegQuality));
        Assert.True(q.HasAdjustments);
        var copy = q.Clone();
        copy.Dpi = 75;
        Assert.Equal(4800, q.Dpi);
        Assert.False(new ScanQuality().HasAdjustments);
    }

    [Fact]
    public void Medidas_y_estimacion_de_tamano()
    {
        var a4 = ScanQuality.AreaInches(PageArea.A4)!.Value;
        Assert.Equal(8.27, a4.Width, 2);
        Assert.Equal(11.69, a4.Height, 2);
        Assert.Equal((8.5, 11.0), ScanQuality.AreaInches(PageArea.Letter)!.Value);
        Assert.NotNull(ScanQuality.AreaInches(PageArea.A5));
        Assert.Null(ScanQuality.AreaInches(PageArea.Full));

        var (mp, mb) = ScanQuality.Estimate(300, ColorMode.Color, PageArea.A4);
        Assert.InRange(mp, 8.6, 8.8);
        Assert.InRange(mb, 24, 26);
        var (_, bw) = ScanQuality.Estimate(300, ColorMode.BlackWhite, PageArea.A4);
        var (_, gray) = ScanQuality.Estimate(300, ColorMode.Grayscale, PageArea.Full);
        Assert.True(bw < gray);
    }

    [Fact]
    public void Plan_baja_la_resolucion_a_lo_que_da_el_escaner_de_red_y_la_deja_en_el_usb()
    {
        var photo = ScanQuality.ForPreset(QualityPreset.Photo);
        Assert.Equal(300, ScanPlanner.Plan(photo, Multifuncion(usb: false), ScanSource.Flatbed, DeviceKind.Scanner).Dpi);
        Assert.Equal(600, ScanPlanner.Plan(photo, Multifuncion(usb: true), ScanSource.Flatbed, DeviceKind.Scanner).Dpi);
        var low = new ScanQuality { Dpi = 50 };
        Assert.Equal(100, ScanPlanner.Plan(low, Multifuncion(usb: true), ScanSource.Flatbed, DeviceKind.Scanner).Dpi);
    }

    [Fact]
    public void Plan_manda_brillo_y_contraste_al_escaner_si_los_tiene_y_si_no_a_la_imagen()
    {
        var q = new ScanQuality { Brightness = 50, Contrast = -100 };
        var withDevice = ScanPlanner.Plan(q, Multifuncion(usb: true), ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Equal(25, withDevice.DeviceBrightness);
        Assert.Equal(-50, withDevice.DeviceContrast);
        Assert.Equal((0, 0), (withDevice.SoftwareBrightness, withDevice.SoftwareContrast));

        var bare = ScanPlanner.Plan(q, new DeviceCapabilities(), ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Null(bare.DeviceBrightness);
        Assert.Equal((50, -100), (bare.SoftwareBrightness, bare.SoftwareContrast));

        var camera = ScanPlanner.Plan(q, Multifuncion(usb: true), ScanSource.Flatbed, DeviceKind.Camera);
        Assert.Null(camera.DeviceBrightness);   // la cámara siempre en la imagen
        Assert.Equal(50, camera.SoftwareBrightness);

        var none = ScanPlanner.Plan(new ScanQuality(), Multifuncion(usb: true), ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Null(none.DeviceBrightness);   // 0 no se manda: se queda lo del escáner
    }

    [Fact]
    public void Plan_recorta_la_zona_al_cristal_y_la_quita_en_camara_y_en_todo_el_cristal()
    {
        var caps = new DeviceCapabilities { MaxWidthInches = 8.0, MaxHeightInches = 10.0 };
        var r = ScanPlanner.Plan(new ScanQuality { Area = PageArea.A4 }, caps, ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Equal((8.0, 10.0), r.AreaInches);
        var a5 = ScanPlanner.Plan(new ScanQuality { Area = PageArea.A5 }, new DeviceCapabilities(), ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Equal(5.83, a5.AreaInches!.Value.Width, 2);
        Assert.Null(ScanPlanner.Plan(new ScanQuality { Area = PageArea.Full }, caps, ScanSource.Flatbed, DeviceKind.Scanner).AreaInches);
        Assert.Null(ScanPlanner.Plan(new ScanQuality { Area = PageArea.A4 }, caps, ScanSource.Flatbed, DeviceKind.Camera).AreaInches);
    }

    [Fact]
    public void Plan_elige_origen_y_resolucion_de_camara()
    {
        var flatbedOnly = new DeviceCapabilities { Sources = [ScanSource.Flatbed] };
        Assert.Equal(ScanSource.Flatbed, ScanPlanner.Plan(new ScanQuality(), flatbedOnly, ScanSource.Feeder, DeviceKind.Scanner).Source);
        var both = new DeviceCapabilities { Sources = [ScanSource.Flatbed, ScanSource.Feeder] };
        Assert.Equal(ScanSource.Feeder, ScanPlanner.Plan(new ScanQuality(), both, ScanSource.Feeder, DeviceKind.Scanner).Source);

        var cam = new DeviceCapabilities { CameraResolutions = [(1920, 1080), (1280, 720)] };
        Assert.Equal((1280, 720), ScanPlanner.Plan(new ScanQuality(), cam, ScanSource.Flatbed, DeviceKind.Camera, (1280, 720)).CameraResolution);
        Assert.Equal((1920, 1080), ScanPlanner.Plan(new ScanQuality(), cam, ScanSource.Flatbed, DeviceKind.Camera, (800, 600)).CameraResolution);
        Assert.Equal((1920, 1080), ScanPlanner.Plan(new ScanQuality(), cam, ScanSource.Flatbed, DeviceKind.Camera).CameraResolution);
        Assert.Null(ScanPlanner.Plan(new ScanQuality(), new DeviceCapabilities(), ScanSource.Flatbed, DeviceKind.Camera).CameraResolution);
    }

    [Theory]
    [InlineData(ColorMode.BlackWhite, new[] { ColorMode.Color, ColorMode.Grayscale }, ColorMode.Grayscale)]
    [InlineData(ColorMode.BlackWhite, new[] { ColorMode.Color }, ColorMode.Color)]
    [InlineData(ColorMode.Grayscale, new[] { ColorMode.Color }, ColorMode.Color)]
    [InlineData(ColorMode.Grayscale, new[] { ColorMode.BlackWhite }, ColorMode.BlackWhite)]
    [InlineData(ColorMode.Color, new[] { ColorMode.Color }, ColorMode.Color)]
    [InlineData(ColorMode.Color, new ColorMode[0], ColorMode.Color)]
    public void PickDeviceColor_pide_uno_del_que_se_pueda_sacar_el_final(ColorMode wanted, ColorMode[] supported, ColorMode expected) =>
        Assert.Equal(expected, ScanPlanner.PickDeviceColor(wanted, supported));

    [Fact]
    public void Plan_conserva_el_color_final_aunque_pida_otro()
    {
        var caps = new DeviceCapabilities { ColorModes = [ColorMode.Color] };
        var r = ScanPlanner.Plan(ScanQuality.ForPreset(QualityPreset.Text), caps, ScanSource.Flatbed, DeviceKind.Scanner);
        Assert.Equal(ColorMode.Color, r.DeviceColor);
        Assert.Equal(ColorMode.BlackWhite, r.FinalColor);
    }

    [Fact]
    public void DpiChoices_y_CameraDpi()
    {
        Assert.Equal([100, 150, 200, 300], ScanPlanner.DpiChoices(Multifuncion(usb: false)));
        Assert.Equal([100, 150, 200, 300, 400, 600, 1200], ScanPlanner.DpiChoices(Multifuncion(usb: true)));
        Assert.Equal([210], ScanPlanner.DpiChoices(new DeviceCapabilities { MinDpi = 210, MaxDpi = 220 }));
        Assert.Equal(131, ScanPlanner.CameraDpi(1920, 1080), 0);   // el lado corto ocupa el ancho de un A4
        Assert.Equal(72, ScanPlanner.CameraDpi(320, 240));
    }

    [Theory]
    [InlineData(ScanErrors.WiaPaperJam, "ErrPaperJam")]
    [InlineData(ScanErrors.WiaPaperEmpty, "ErrPaperEmpty")]
    [InlineData(ScanErrors.WiaPaperProblem, "ErrPaperProblem")]
    [InlineData(ScanErrors.WiaOffline, "ErrDeviceOffline")]
    [InlineData(ScanErrors.DeviceNotConnected, "ErrDeviceOffline")]
    [InlineData(ScanErrors.DeviceNotFound, "ErrDeviceOffline")]
    [InlineData(ScanErrors.WiaDeviceCommunication, "ErrDeviceOffline")]
    [InlineData(ScanErrors.WiaItemDeleted, "ErrDeviceOffline")]
    [InlineData(ScanErrors.WiaBusy, "ErrDeviceBusy")]
    [InlineData(ScanErrors.WiaDeviceLocked, "ErrDeviceBusy")]
    [InlineData(ScanErrors.WiaWarmingUp, "ErrWarmingUp")]
    [InlineData(ScanErrors.WiaLampOff, "ErrWarmingUp")]
    [InlineData(ScanErrors.WiaCoverOpen, "ErrCoverOpen")]
    [InlineData(ScanErrors.WiaUserIntervention, "ErrUserIntervention")]
    [InlineData(ScanErrors.WiaIncorrectSetting, "ErrBadSetting")]
    [InlineData(ScanErrors.WiaInvalidCommand, "ErrBadSetting")]
    [InlineData(ScanErrors.AccessDenied, "ErrCameraDenied")]
    [InlineData(ScanErrors.CameraInUse, "ErrCameraInUse")]
    [InlineData(ScanErrors.CameraNoFrames, "ErrCameraNoFrames")]
    public void Cada_fallo_tiene_su_explicacion(int hresult, string key)
    {
        Assert.Equal(key, ScanErrors.KeyFor(hresult));
        Assert.Equal(key, ScanErrors.KeyFor(new System.Runtime.InteropServices.COMException("x", hresult)));
    }

    [Fact]
    public void Fallos_que_no_son_de_WIA()
    {
        Assert.Null(ScanErrors.KeyFor(ScanErrors.WiaGeneral));
        Assert.Equal("ErrScanUnknown", ScanErrors.KeyFor(new InvalidOperationException()));
        Assert.Equal("ErrPaperJam", ScanErrors.KeyFor(new Exception("fuera", new System.Runtime.InteropServices.COMException("x", ScanErrors.WiaPaperJam))));
        Assert.Equal("ErrCameraDenied", ScanErrors.KeyFor(new UnauthorizedAccessException()));
        Assert.Equal("ErrCancelled", ScanErrors.KeyFor(new OperationCanceledException()));
        Assert.Equal("ErrNoPages", ScanErrors.KeyFor(new ScanException("ErrNoPages", "x")));
        Assert.True(ScanErrors.IsTransient(new System.Runtime.InteropServices.COMException("x", ScanErrors.WiaWarmingUp)));
        Assert.True(ScanErrors.IsTransient(new System.Runtime.InteropServices.COMException("x", ScanErrors.WiaBusy)));
        Assert.False(ScanErrors.IsTransient(new System.Runtime.InteropServices.COMException("x", ScanErrors.WiaPaperJam)));
        Assert.False(ScanErrors.IsTransient(new ScanException("ErrDeviceBusy", "x")));
    }
}
