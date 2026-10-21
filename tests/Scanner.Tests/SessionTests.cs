using System.Runtime.InteropServices;
using SocScanner.Engine;
using SocScanner.Output;
using SocScanner.Scanning;
using SocScanner.Session;
using SocScanner.Settings;

namespace SocScanner.Tests;

/// <summary>El documento de varias hojas y el flujo de escanear, con el escáner simulado y uno de pruebas.</summary>
[Collection("Estado global")]
public sealed class SessionTests
{
    [Fact]
    public void Documento_anadir_girar_mover_quitar_y_vaciar_avisando_de_cada_cambio()
    {
        var doc = new ScanDocument();
        var changes = 0;
        doc.Changed += () => changes++;
        Assert.True(doc.IsEmpty);
        Assert.False(doc.HasUnsaved);
        var a = Images.Page(width: 40, height: 60);
        var b = Images.Page(width: 50, height: 70);
        var c = Images.Page(width: 60, height: 80);
        doc.Add([a, b, c]);
        doc.Add([]);   // nada nuevo: no avisa
        Assert.Equal((3, 1), (doc.Count, changes));
        Assert.True(doc.HasUnsaved);

        doc.Rotate(0, 90);
        Assert.Equal((60, 40), (doc.Pages[0].PixelWidth, doc.Pages[0].PixelHeight));
        doc.Rotate(9, 90);   // fuera: no hace nada
        Assert.Equal(2, changes);

        Assert.Equal(2, doc.Move(0, 5));   // al final como mucho
        Assert.Same(b, doc.Pages[0]);
        Assert.Equal(0, doc.Move(0, -1));   // ya está al principio: no cambia
        Assert.Equal(7, doc.Move(7, 1));
        Assert.Equal(3, changes);

        doc.RemoveAt(0);
        doc.RemoveAt(-1);
        Assert.Equal((2, 4), (doc.Count, changes));
        Assert.Same(c, doc.Pages[0]);
        doc.Clear();
        doc.Clear();
        Assert.Equal((0, 5), (doc.Count, changes));
    }

    private static ScannerSettings Settings(TempFolder tmp, ScanMode mode) => new()
    {
        Mode = mode,
        BatchFolder = tmp.File("serie"),
        BatchFormat = OutputFormat.Pdf,
        BatchPrefix = "Factura_",
        BatchDigits = 3,
        BatchNextNumber = 1,
        Preset = QualityPreset.Document,
    };

    [Fact]
    public async Task Una_a_una_las_hojas_van_al_documento_y_se_guardan_juntas()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        var flow = new ScanWorkflow(() => s);
        var scanner = new SimulatedScanner { Delay = TimeSpan.Zero };
        var progress = new List<int>();
        var outcome = await flow.ScanAsync(scanner, new SyncProgress(progress), CancellationToken.None);
        Assert.Single(outcome.Pages);
        Assert.Empty(outcome.Saved);
        Assert.Equal(200, outcome.Request.Dpi);
        Assert.Equal(ColorMode.Grayscale, outcome.Pages[0].Color);
        Assert.Equal(100, progress[^1]);
        await flow.ScanAsync(scanner, null, CancellationToken.None);
        Assert.Equal(2, flow.Document.Count);
        Assert.False(flow.IsBusy);

        var saved = await flow.SaveDocumentAsync(tmp.File("doc.pdf"), OutputFormat.Pdf, 80);
        Assert.Single(saved);
        Assert.True(File.Exists(saved[0]));
        Assert.True(flow.Document.IsEmpty);
        Assert.Empty(await flow.SaveDocumentAsync(tmp.File("vacio.pdf"), OutputFormat.Pdf, 80));
    }

    [Fact]
    public async Task El_alimentador_da_todas_sus_hojas()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        s.Source = ScanSource.Feeder;
        var flow = new ScanWorkflow(() => s);
        var outcome = await flow.ScanAsync(new SimulatedScanner { Delay = TimeSpan.Zero, FeederPages = 4 }, null, CancellationToken.None);
        Assert.Equal(4, outcome.Pages.Count);
        Assert.Equal(4, flow.Document.Count);
    }

    [Fact]
    public async Task En_serie_cada_hoja_se_guarda_con_el_numero_siguiente_sin_pisar_nada()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Batch);
        Directory.CreateDirectory(s.BatchFolder);
        File.WriteAllText(Path.Combine(s.BatchFolder, "Factura_002.pdf"), "ya estaba");
        var flow = new ScanWorkflow(() => s);
        var scanner = new SimulatedScanner { Delay = TimeSpan.Zero };

        Assert.Equal("Factura_001.pdf", ScanWorkflow.NextSeriesName(s));
        var first = await flow.ScanAsync(scanner, null, CancellationToken.None);
        Assert.Equal([Path.Combine(s.BatchFolder, "Factura_001.pdf")], first.Saved);
        Assert.Equal(2, s.BatchNextNumber);
        Assert.Equal("Factura_003.pdf", ScanWorkflow.NextSeriesName(s));   // el 2 ya existe

        var second = await flow.ScanAsync(scanner, null, CancellationToken.None);
        Assert.EndsWith("Factura_003.pdf", second.Saved[0]);
        Assert.Equal("ya estaba", File.ReadAllText(Path.Combine(s.BatchFolder, "Factura_002.pdf")));
        Assert.Equal(4, s.BatchNextNumber);
        Assert.True(flow.Document.IsEmpty);   // en serie no se acumula

        // El contador queda guardado para seguir otro día.
        Assert.Equal(4, ScannerSettings.Load().BatchNextNumber);

        s.Source = ScanSource.Feeder;
        var feeder = await flow.ScanAsync(new SimulatedScanner { Delay = TimeSpan.Zero, FeederPages = 2 }, null, CancellationToken.None);
        Assert.Equal(["Factura_004.pdf", "Factura_005.pdf"], feeder.Saved.Select(Path.GetFileName));
    }

    [Fact]
    public void NextSeriesName_con_la_carpeta_aun_sin_crear()
    {
        var s = new ScannerSettings { BatchFolder = @"Z:\no\existe", BatchPrefix = "A", BatchNextNumber = 9, BatchDigits = 2, BatchFormat = OutputFormat.Png };
        Assert.Equal("A09.png", ScanWorkflow.NextSeriesName(s));
    }

    [Fact]
    public async Task En_serie_con_una_carpeta_que_no_vale_no_escanea()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Batch);
        s.BatchFolder = "relativa";
        var scanner = new SimulatedScanner { Delay = TimeSpan.Zero };
        var ex = await Assert.ThrowsAsync<ScanException>(() => new ScanWorkflow(() => s).ScanAsync(scanner, null, CancellationToken.None));
        Assert.Equal("ErrFolderNotAbsolute", ex.MessageKey);
        Assert.Equal(0, scanner.ScanCount);
    }

    [Fact]
    public async Task Si_esta_ocupado_o_calentando_reintenta_una_vez()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        var flow = new ScanWorkflow(() => s) { RetryDelay = TimeSpan.Zero };
        var scanner = new SimulatedScanner { Delay = TimeSpan.Zero, FailWith = ScanErrors.WiaWarmingUp };
        var outcome = await flow.ScanAsync(scanner, null, CancellationToken.None);
        Assert.Single(outcome.Pages);
        Assert.Contains("se reintenta", File.ReadAllText(AppLog.FilePath));
    }

    [Fact]
    public async Task Un_fallo_de_papel_no_se_reintenta_y_deja_libre_el_flujo()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        var flow = new ScanWorkflow(() => s) { RetryDelay = TimeSpan.Zero };
        var scanner = new SimulatedScanner { Delay = TimeSpan.Zero, FailWith = ScanErrors.WiaPaperEmpty };
        var ex = await Assert.ThrowsAsync<COMException>(() => flow.ScanAsync(scanner, null, CancellationToken.None));
        Assert.Equal("ErrPaperEmpty", ScanErrors.KeyFor(ex));
        Assert.False(flow.IsBusy);
        Assert.Single((await flow.ScanAsync(scanner, null, CancellationToken.None)).Pages);
    }

    [Fact]
    public async Task Cancelar_corta_el_escaneo()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        var flow = new ScanWorkflow(() => s);
        using var cts = new CancellationTokenSource();
        var scan = flow.ScanAsync(new SimulatedScanner { Delay = TimeSpan.FromSeconds(5) }, null, cts.Token);
        cts.CancelAfter(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.True(flow.Document.IsEmpty);
    }

    [Fact]
    public async Task No_se_escanea_dos_veces_a_la_vez_y_sin_hojas_es_un_fallo()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        var flow = new ScanWorkflow(() => s);
        var slow = flow.ScanAsync(new SimulatedScanner { Delay = TimeSpan.FromMilliseconds(300) }, null, CancellationToken.None);
        Assert.True(flow.IsBusy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ScanAsync(new SimulatedScanner(), null, CancellationToken.None));
        await slow;

        var empty = new FakeDevice(new DeviceCapabilities());
        empty.Results.Enqueue(_ => []);
        var ex = await Assert.ThrowsAsync<ScanException>(() => flow.ScanAsync(empty, null, CancellationToken.None));
        Assert.Equal("ErrNoPages", ex.MessageKey);
    }

    [Fact]
    public async Task Se_pide_al_dispositivo_lo_que_admite()
    {
        using var tmp = new TempFolder();
        var s = Settings(tmp, ScanMode.Single);
        s.Preset = QualityPreset.Photo;
        var device = new FakeDevice(new DeviceCapabilities { MinDpi = 100, MaxDpi = 300, ColorModes = [ColorMode.Color] });
        await new ScanWorkflow(() => s).ScanAsync(device, null, CancellationToken.None);
        Assert.Equal(300, device.Requests[0].Dpi);
        Assert.Equal(ColorMode.Color, device.Requests[0].DeviceColor);
    }

    [Fact]
    public async Task El_escaner_simulado_respeta_resolucion_zona_y_color()
    {
        var sim = new SimulatedScanner { Delay = TimeSpan.Zero };
        var caps = await sim.GetCapabilitiesAsync();
        Assert.Equal(SimulatedScanner.MaxDpi, caps.MaxDpi);
        var pages = await sim.ScanAsync(new ScanRequest { Dpi = 600, AreaInches = (2, 3), DeviceColor = ColorMode.Grayscale, FinalColor = ColorMode.BlackWhite }, null, CancellationToken.None);
        Assert.Equal((600, 900), (pages[0].PixelWidth, pages[0].PixelHeight));   // a 300 ppp como mucho
        Assert.Equal(System.Windows.Media.PixelFormats.BlackWhite, pages[0].Image.Format);
        Assert.Equal(DeviceKind.Simulated, sim.Info.Kind);
        sim.Dispose();
    }

    [Fact]
    public async Task El_catalogo_en_modo_aislado_solo_tiene_los_simulados()
    {
        var sim = new SimulatedScanner("Prueba");
        var cam = new SimulatedCamera("Cámara");
        IScanDevice[] simulated = [sim, cam];
        var list = await DeviceCatalog.ListAsync(simulated);
        Assert.Equal(2, list.Count);
        Assert.Equal((SimulatedScanner.DeviceId, "Prueba", DeviceKind.Simulated), (list[0].Id, list[0].Name, list[0].Kind));
        Assert.Equal((SimulatedCamera.DeviceId, DeviceKind.Camera), (list[1].Id, list[1].Kind));
        Assert.Same(sim, DeviceCatalog.Open(list[0], simulated));
        Assert.Same(cam, DeviceCatalog.Open(list[1], simulated));
        Assert.True(DeviceCatalog.IsSimulated(cam, simulated));
        Assert.False(DeviceCatalog.IsSimulated(new SimulatedScanner(), simulated));
        Assert.False(DeviceCatalog.IsSimulated(sim, null));
        Assert.IsType<SimulatedScanner>(DeviceCatalog.Open(list[0]));
        using var realCam = DeviceCatalog.Open(new ScanDeviceInfo("x", "c", DeviceKind.Camera));
        Assert.IsType<CameraDevice>(realCam);
        using var scanner = DeviceCatalog.Open(new ScanDeviceInfo("y", "s", DeviceKind.Scanner));
        Assert.IsType<WinRtScanner>(scanner);
    }

    /// <summary>IProgress que apunta en el momento (Progress&lt;T&gt; lo haría más tarde, en otro hilo).</summary>
    private sealed class SyncProgress(List<int> into) : IProgress<int>
    {
        public void Report(int value) => into.Add(value);
    }
}

[CollectionDefinition("Estado global", DisableParallelization = true)]
public sealed class GlobalStateCollection;
