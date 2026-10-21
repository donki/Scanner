using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace SocScanner.UITests;

/// <summary>
/// Recorridos de la interfaz sobre el exe Debug en modo aislado (General §8.7). Cada prueba arranca su
/// instancia con una carpeta propia; se escanea con el escáner simulado y los ficheros van a esa
/// carpeta, que se borra.
/// </summary>
public sealed class MainWindowTests
{
    private static bool Wait(Func<bool> condition) => Retry.WhileFalse(condition, ScanApp.Timeout).Result;

    [Fact]
    public void Arranca_en_modo_aislado_con_el_escaner_simulado()
    {
        using var app = ScanApp.Launch();
        Assert.Contains("[SOC_SANDBOX]", app.Main.Title);
        Assert.True(Wait(() => app.Button(app.Main, "ScanButton").IsEnabled), "el botón de escanear no se activa");
        Assert.Contains("simulado", app.ById(app.Main, "DeviceCombo").AsComboBox().SelectedItem?.Name ?? app.ById(app.Main, "DeviceCombo").Name);
        Assert.Contains("75 a 300 ppp", app.ById(app.Main, "DeviceInfoText").Name);
        Assert.False(app.Button(app.Main, "SaveButton").IsEnabled);
        Assert.Equal("200 ppp · Gris · A4", app.ById(app.Main, "QualitySummaryText").Name);
        app.Capture(app.Main, "principal");
    }

    [Fact]
    public void Una_a_una_escanear_dos_hojas_ordenar_girar_quitar_y_guardar_en_PDF()
    {
        using var app = ScanApp.Launch();
        var scan = app.Button(app.Main, "ScanButton");
        Assert.True(Wait(() => scan.IsEnabled));
        ScanApp.Press(scan);
        Assert.True(Wait(() => app.TryById(app.Main, "Page1") is not null && scan.IsEnabled), "no sale la primera hoja");
        ScanApp.Press(scan);
        Assert.True(Wait(() => app.TryById(app.Main, "Page2") is not null && scan.IsEnabled), "no sale la segunda hoja");
        Assert.Contains("2 en el documento", app.ById(app.Main, "StatusText").Name);
        app.Capture(app.Main, "dos-hojas");

        ScanApp.Select(app.ById(app.Main, "Page2"));
        ScanApp.Press(app.Button(app.Main, "MoveLeftButton"));
        ScanApp.Press(app.Button(app.Main, "RotateRightButton"));
        ScanApp.Press(app.Button(app.Main, "DeletePageButton"));
        Assert.True(Wait(() => app.TryById(app.Main, "Page2") is null), "no se quita la hoja");

        Assert.True(app.Button(app.Main, "SaveButton").IsEnabled);
        ScanApp.Press(app.Button(app.Main, "SaveButton"));
        var dialog = app.WaitModal();
        var path = Path.Combine(app.DataFolder, "documento.pdf");
        var name = Retry.WhileNull(() => dialog.FindFirstDescendant(app.Cf.ByAutomationId("1001")), ScanApp.Timeout).Result!;
        name.Patterns.Value.Pattern.SetValue(path);
        ScanApp.Press(dialog.FindFirstDescendant(app.Cf.ByAutomationId("1"))!.AsButton());
        Assert.True(Wait(() => File.Exists(path)), "no se guarda el PDF");
        Assert.StartsWith("%PDF", File.ReadAllText(path)[..4]);
        Assert.True(Wait(() => app.TryById(app.Main, "LastNameText")?.Name == "documento.pdf"));
        Assert.Null(app.TryById(app.Main, "Page1"));   // guardado: el documento se vacía
        Assert.False(app.Button(app.Main, "SaveButton").IsEnabled);
        app.Capture(app.Main, "guardado");
    }

    [Fact]
    public void Descartar_las_hojas_pide_confirmacion()
    {
        using var app = ScanApp.Launch();
        var scan = app.Button(app.Main, "ScanButton");
        Assert.True(Wait(() => scan.IsEnabled));
        ScanApp.Press(scan);
        Assert.True(Wait(() => app.TryById(app.Main, "Page1") is not null && scan.IsEnabled));
        ScanApp.Press(app.Button(app.Main, "ClearButton"));
        var confirm = app.WaitModal();
        ScanApp.Press(app.Button(confirm, "CancelButton"));
        app.WaitNoModal();
        Assert.NotNull(app.TryById(app.Main, "Page1"));
        ScanApp.Press(app.Button(app.Main, "ClearButton"));
        ScanApp.Press(app.Button(app.WaitModal(), "OkButton"));
        Assert.True(Wait(() => app.TryById(app.Main, "Page1") is null));
    }

    [Fact]
    public void En_serie_cada_hoja_se_guarda_sola_con_su_numero()
    {
        using var app = ScanApp.Launch();
        ScanApp.Select(app.ById(app.Main, "BatchTile"));
        Assert.True(Wait(() => !app.ById(app.Main, "BatchPrefixBox").IsOffscreen), "no sale el panel de la serie");
        Assert.True(Wait(() => app.Settings().Contains("\"mode\": \"batch\"")));
        app.ById(app.Main, "BatchPrefixBox").Patterns.Value.Pattern.SetValue("Factura_");
        app.ById(app.Main, "BatchSuffixBox").Patterns.Value.Pattern.SetValue("-B");
        Assert.True(Wait(() => app.ById(app.Main, "BatchNextText").Name == "Factura_0001-B.pdf"));

        var scan = app.Button(app.Main, "ScanButton");
        Assert.True(Wait(() => scan.IsEnabled));
        ScanApp.Press(scan);
        Assert.True(Wait(() => File.Exists(Path.Combine(app.ScansFolder, "Factura_0001-B.pdf"))), "no se guarda la primera");
        Assert.True(Wait(() => scan.IsEnabled));
        ScanApp.Press(scan);
        Assert.True(Wait(() => File.Exists(Path.Combine(app.ScansFolder, "Factura_0002-B.pdf"))), "no se guarda la segunda");
        Assert.True(Wait(() => app.ById(app.Main, "BatchNextText").Name == "Factura_0003-B.pdf"));
        Assert.True(Wait(() => app.ById(app.Main, "LastNameText").Name == "Factura_0002-B.pdf"));
        Assert.Contains("\"batchNextNumber\": 3", app.Settings());
        Assert.Contains("\"batchPrefix\": \"Factura_\"", app.Settings());
        Assert.Null(app.TryById(app.Main, "Page1"));   // en serie no se acumulan hojas
        app.Capture(app.Main, "serie");
    }

    [Fact]
    public void En_serie_un_prefijo_con_caracteres_prohibidos_se_avisa_y_no_se_escanea()
    {
        using var app = ScanApp.Launch();
        ScanApp.Select(app.ById(app.Main, "BatchTile"));
        Assert.True(Wait(() => !app.ById(app.Main, "BatchPrefixBox").IsOffscreen));
        app.ById(app.Main, "BatchPrefixBox").Patterns.Value.Pattern.SetValue("a:b");
        var scan = app.Button(app.Main, "ScanButton");
        Assert.True(Wait(() => scan.IsEnabled));
        ScanApp.PressNoWait(scan);
        var alert = app.WaitModal();
        app.Capture(alert, "aviso");
        ScanApp.Press(app.Button(alert, "OkButton"));
        app.WaitNoModal();
        Assert.Equal("", app.ById(app.Main, "BatchPrefixBox").Patterns.Value.Pattern.Value.Value);   // vuelve a lo guardado
        Assert.False(Directory.Exists(app.ScansFolder) && Directory.EnumerateFiles(app.ScansFolder).Any());
    }

    [Fact]
    public void Calidad_a_medida_queda_como_personalizada()
    {
        using var app = ScanApp.Launch();
        Assert.True(Wait(() => app.Button(app.Main, "ScanButton").IsEnabled));
        ScanApp.Press(app.Button(app.Main, "QualityButton"));
        var dialog = app.WaitModal();
        ScanApp.Select(app.ById(dialog, "ColorBlackWhiteRadio"));
        var dpi = app.ById(dialog, "DpiCombo").AsComboBox();
        dpi.Expand();
        dpi.Items.First(i => i.Name == "300 ppp").Select();
        dpi.Collapse();
        app.Capture(dialog, "calidad");
        ScanApp.Press(app.Button(dialog, "OkButton"));
        app.WaitNoModal();
        Assert.True(Wait(() => app.ById(app.Main, "QualitySummaryText").Name == "300 ppp · Blanco y negro · A4"));
        Assert.Contains("\"preset\": \"custom\"", app.Settings());
        Assert.Contains("\"color\": \"blackWhite\"", app.Settings());

        // Escanear con esa calidad da una hoja de 1 bit.
        ScanApp.Press(app.Button(app.Main, "ScanButton"));
        Assert.True(Wait(() => app.ById(app.Main, "DetailText").Name.Contains("Blanco y negro")));
        Assert.Contains("300 ppp", app.ById(app.Main, "DetailText").Name);
    }

    [Fact]
    public void Idioma_cambia_al_momento_sin_perder_las_hojas()
    {
        using var app = ScanApp.Launch();
        var scan = app.Button(app.Main, "ScanButton");
        Assert.True(Wait(() => scan.IsEnabled));
        Assert.Equal("Escanear", scan.Name);
        ScanApp.Press(scan);
        Assert.True(Wait(() => app.TryById(app.Main, "Page1") is not null && scan.IsEnabled));
        ScanApp.Press(app.Button(app.Main, "LanguageButton"));
        Assert.True(Wait(() => app.CurrentMain().FindFirstDescendant(app.Cf.ByAutomationId("ScanButton"))?.Name == "Scan"));
        Assert.Contains("\"language\": \"en\"", app.Settings());
        Assert.True(Wait(() => app.TryById(app.CurrentMain(), "Page1") is not null), "se pierden las hojas al cambiar de idioma");
        app.Capture(app.CurrentMain(), "ingles");
    }

    [Fact]
    public void Acerca_de_novedades_y_guia()
    {
        using var app = ScanApp.Launch();
        ScanApp.Press(app.Button(app.Main, "AboutButton"));
        var about = app.WaitModal();
        app.Capture(about, "acerca-de");
        ScanApp.Press(app.Button(about, "CloseButton"));
        app.WaitNoModal();

        ScanApp.Press(app.Button(app.Main, "NewsButton"));
        var news = app.WaitModal();
        app.Capture(news, "novedades");
        ScanApp.Press(app.Button(news, "CloseButton"));
        app.WaitNoModal();

        ScanApp.Press(app.Button(app.Main, "GuideButton"));
        var guide = app.WaitModal();
        Assert.Equal("Hecho", app.ById(guide, "GuideStateText").Name);   // hay un dispositivo
        for (var i = 0; i < 3; i++)
            ScanApp.Press(app.Button(guide, "GuideNextButton"));
        Assert.Equal("Hecho", app.ById(guide, "GuideStateText").Name);   // la carpeta de la serie vale
        app.Capture(guide, "guia");
        ScanApp.Press(app.Button(guide, "GuideNextButton"));   // terminar
        app.WaitNoModal();
    }

    [Fact]
    public void La_primera_vez_sale_la_guia()
    {
        using var app = ScanApp.Launch("""{ "language": "es" }""");
        var guide = app.WaitModal();
        Assert.NotNull(app.TryById(guide, "GuideNextButton"));
        ScanApp.Press(app.Button(guide, "CloseButton"));
        app.WaitNoModal();
        Assert.True(Wait(() => app.Settings().Contains("\"guideShown\": true")));
    }
}
