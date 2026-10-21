using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SocScanner.App;
using SocScanner.App.Services;
using SocScanner.Localization;
using SocScanner.Output;
using SocScanner.Scanning;
using SocScanner.Settings;
using static SocScanner.Tests.Ui;

namespace SocScanner.Tests;

/// <summary>
/// Las ventanas de la aplicación, dentro del banco (General §8.6): la principal con el escáner y la
/// cámara simulados, la calidad, la guía, las novedades, «Acerca de» y los avisos. Fuera de la
/// pantalla, sin activarse y con los diálogos de Windows contestados por la prueba.
/// </summary>
[Collection("Estado global")]
public sealed class WindowTests
{
    private static MainWindow Open(ScannerSettings? settings = null)
    {
        Reset(settings);
        var w = Run(() => Show(new MainWindow()));
        WaitUntil(() => w.ScanButton.IsEnabled, () => "el botón de escanear se activa (" + Run(() => w.DeviceInfoText.Text + " / " + w.IsLoaded + " / " + w.DeviceCombo.Items.Count) + ")");
        return w;
    }

    private static void Scan(MainWindow w)
    {
        Run(() => Click(w.ScanButton));
        WaitUntil(() => w.ScanButton.IsEnabled && !App.App.Workflow.IsBusy, "termina de escanear");
        Settle();
    }

    private static void Close(Window w) => Run(() => { if (w.IsVisible) w.Close(); });

    [Fact]
    public void Arranca_con_el_escaner_y_la_camara_simulados()
    {
        var w = Open();
        Run(() =>
        {
            Assert.Contains("[SOC_SANDBOX]", w.Title);
            Assert.Equal(2, w.DeviceCombo.Items.Count);
            Assert.Contains("75 a 300 ppp", w.DeviceInfoText.Text);
            Assert.Equal(Visibility.Visible, w.SourcePanel.Visibility);
            Assert.Equal("200 ppp · Gris · A4", w.QualitySummaryText.Text);
            Assert.Equal(Visibility.Visible, w.EmptyHint.Visibility);
            Assert.False(w.SaveButton.IsEnabled);
            Assert.Equal("Listo para escanear.", w.StatusText.Text);
        });
        Close(w);
    }

    [Fact]
    public void Una_a_una_escanear_ordenar_girar_quitar_y_guardar()
    {
        var w = Open();
        Scan(w);
        Scan(w);
        Run(() =>
        {
            Assert.Equal(2, w.PageList.Items.Count);
            Assert.Equal(Visibility.Visible, w.PagesCard.Visibility);
            Assert.Equal(1, w.PageList.SelectedIndex);
            Assert.Contains("2 en el documento", w.StatusText.Text);
            Assert.Contains("200 ppp · Gris", w.DetailText.Text);
            Assert.True(w.SaveButton.IsEnabled);
            Assert.False(w.MoveRightButton.IsEnabled);
            var second = App.App.Workflow.Document.Pages[1];
            Click(w.MoveLeftButton);
            Assert.Same(second, App.App.Workflow.Document.Pages[0]);
            Assert.Equal(0, w.PageList.SelectedIndex);
            var (pw, ph) = (second.PixelWidth, second.PixelHeight);
            Click(w.RotateRightButton);
            Assert.Equal((ph, pw), (App.App.Workflow.Document.Pages[0].PixelWidth, App.App.Workflow.Document.Pages[0].PixelHeight));
            Click(w.RotateLeftButton);
            Assert.Equal((pw, ph), (App.App.Workflow.Document.Pages[0].PixelWidth, App.App.Workflow.Document.Pages[0].PixelHeight));
            Click(w.MoveRightButton);
            Assert.Equal(1, w.PageList.SelectedIndex);
            Click(w.DeletePageButton);
            Assert.Equal(1, App.App.Workflow.Document.Count);
        });

        // Cancelar el diálogo de guardar no hace nada.
        SaveAs = null;
        Run(() => Click(w.SaveButton));
        Assert.Equal(1, Run(() => App.App.Workflow.Document.Count));
        Assert.StartsWith("Escaneo 20", SaveAsked!.FileName);
        Assert.Equal(1, SaveAsked.FilterIndex);   // PDF

        var path = Path.Combine(SandboxFolder, "salida", "doc.pdf");
        SaveAs = (path, 1);
        Run(() => Click(w.SaveButton));
        WaitUntil(() => File.Exists(path) && w.LastCard.Visibility == Visibility.Visible, "se guarda el PDF");
        Run(() =>
        {
            Assert.Equal("doc.pdf", w.LastNameText.Text);
            Assert.Contains("KB", w.LastInfoText.Text);
            Assert.Equal(0, App.App.Workflow.Document.Count);
            Assert.False(w.SaveButton.IsEnabled);
            Assert.Equal(Path.GetDirectoryName(path), App.App.Settings.SaveFolder);
            Click(w.OpenFileButton);
            Click(w.ShowInFolderButton);
            Click(w.CopyFileButton);
        });
        Assert.Equal(path, Started[0].FileName);
        Assert.Contains("/select", Started[1].Arguments);
        Assert.Equal(path, Copied![0]);
        Assert.Equal("Fichero copiado: pégalo donde quieras.", Run(() => w.DetailText.Text));

        // El portapapeles ocupado se avisa.
        ClipboardBusy = true;
        AnswerOk();
        Run(() => Click(w.CopyFileButton));
        Assert.Contains("portapapeles", AllText(Modals[^1]));

        // Si el fichero ya no está: aviso al copiar y se abre la carpeta al enseñarlo.
        File.Delete(path);
        AnswerOk();
        Run(() => Click(w.CopyFileButton));
        Assert.Contains("ya no está", AllText(Modals[^1]));
        Run(() => Click(w.ShowInFolderButton));
        Assert.Equal(Path.GetDirectoryName(path), Started[^1].FileName);
        Close(w);
    }

    [Fact]
    public void Guardar_varias_hojas_en_JPG_hace_un_fichero_por_hoja_y_un_fallo_se_avisa()
    {
        var w = Open();
        Scan(w);
        Scan(w);
        var path = Path.Combine(SandboxFolder, "jpg", "hojas.jpg");
        SaveAs = (path, 1);   // el filtro dice PDF, pero manda la extensión
        Run(() => Click(w.SaveButton));
        WaitUntil(() => w.LastCard.Visibility == Visibility.Visible, "se guardan las dos");
        Assert.True(File.Exists(Path.Combine(SandboxFolder, "jpg", "hojas (2).jpg")));
        Assert.Contains("2 ficheros", Run(() => w.StatusText.Text + w.LastInfoText.Text));
        Assert.Equal(OutputFormat.Jpeg, App.App.Settings.SingleFormat);

        // Sin extensión conocida, manda el filtro elegido.
        Scan(w);
        var noExt = Path.Combine(SandboxFolder, "jpg", "sin-extension");
        SaveAs = (noExt, 3);
        Run(() => Click(w.SaveButton));
        WaitUntil(() => File.Exists(noExt + ".png"), "se guarda como PNG");

        // Un fichero abierto en otro programa: se avisa y las hojas siguen ahí.
        Scan(w);
        var locked = Path.Combine(SandboxFolder, "jpg", "abierto.pdf");
        File.WriteAllText(locked, "x");
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SaveAs = (locked, 1);
            AnswerOk();
            Run(() => Click(w.SaveButton));
            WaitUntil(() => Modals.Count > 0 && !Modals[^1].IsVisible, "avisa del fallo");
        }
        Assert.Contains("abierto en otro programa", AllText(Modals[^1]));
        Assert.Equal(1, Run(() => App.App.Workflow.Document.Count));
        Assert.Equal("No se ha podido hacer.", Run(() => w.StatusText.Text));
        Run(() => App.App.Workflow.Document.Clear());
        Close(w);
    }

    [Fact]
    public void En_serie_guarda_cada_hoja_con_su_numero()
    {
        var w = Open();
        Run(() =>
        {
            w.BatchTile.IsChecked = true;
            Assert.Equal(Visibility.Visible, w.BatchPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, w.DocumentButtons.Visibility);
            Assert.Equal(ScanMode.Batch, App.App.Settings.Mode);
            w.BatchPrefixBox.Text = "F_";
            Assert.Equal("F_0001.pdf", w.BatchNextText.Text);   // mientras se escribe ya se ve
            w.BatchPrefixBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            w.BatchNumberBox.Text = "7";
            w.BatchNumberBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        });
        Settle();
        Run(() =>
        {
            Assert.Equal(("F_", 7), (App.App.Settings.BatchPrefix, App.App.Settings.BatchNextNumber));
            w.BatchDigitsCombo.SelectedIndex = 2;   // 3 cifras
            Assert.Equal("F_007.pdf", w.BatchNextText.Text);
        });
        Scan(w);
        WaitUntil(() => w.PreviewImage.Source is not null, "se ve la hoja guardada");
        var first = Path.Combine(App.App.Settings.BatchFolder, "F_007.pdf");
        Assert.True(File.Exists(first));
        Run(() =>
        {
            Assert.Equal("F_007.pdf", w.LastNameText.Text);
            Assert.Equal("Guardada: F_007.pdf", w.StatusText.Text);
            Assert.Equal("8", w.BatchNumberBox.Text);
            Assert.Equal("Siguiente: F_008.pdf", w.DetailText.Text);
            w.BatchFormatCombo.SelectedIndex = 2;   // PNG
            Assert.Equal("F_008.png", w.BatchNextText.Text);
            w.BatchSuffixBox.Text = "a?b";
            Assert.Contains("no puede llevar", w.BatchNextText.Text);
        });

        // Un sufijo con caracteres prohibidos: aviso, vuelve a lo guardado y no se escanea.
        AnswerOk();
        Run(() => Click(w.ScanButton));
        Settle();
        Assert.Contains("no puede llevar", AllText(Modals[^1]));
        Assert.Equal("", Run(() => w.BatchSuffixBox.Text));
        Assert.False(File.Exists(Path.Combine(App.App.Settings.BatchFolder, "F_008.png")));

        // Un número que no vale, con Intro.
        AnswerOk();
        Run(() =>
        {
            w.BatchNumberBox.Text = "-3";
            var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent };
            w.BatchNumberBox.RaiseEvent(enter);
        });
        Assert.Contains("número entero", AllText(Modals[^1]));
        Assert.Equal("8", Run(() => w.BatchNumberBox.Text));

        // Otra carpeta: la elegida vale; una que no, se avisa.
        PickedFolder = Path.Combine(SandboxFolder, "otra-serie");
        Run(() => Click(w.BrowseBatchFolderButton));
        Assert.Equal(PickedFolder, Run(() => w.BatchFolderBox.Text));
        PickedFolder = null;
        Run(() => Click(w.BrowseBatchFolderButton));
        PickedFolder = "relativa";
        AnswerOk();
        Run(() => Click(w.BrowseBatchFolderButton));
        Assert.Contains("ruta completa", AllText(Modals[^1]));
        Run(() => Click(w.OpenBatchFolderButton));
        Assert.Equal(Path.Combine(SandboxFolder, "otra-serie"), Started[^1].FileName);
        App.App.Settings.BatchFolder = "relativa";
        AnswerOk();
        Run(() => Click(w.OpenBatchFolderButton));
        Assert.Contains("ruta completa", AllText(Modals[^1]));

        Run(() => w.SingleTile.IsChecked = true);
        Assert.Equal(ScanMode.Single, App.App.Settings.Mode);
        Close(w);
    }

    [Fact]
    public void Calidad_a_medida_y_atajos()
    {
        var w = Open();
        Answer<QualityWindow>(q =>
        {
            Click(Find<RadioButton>(q, "ColorBlackWhiteRadio"));
            Find<RadioButton>(q, "ColorBlackWhiteRadio").IsChecked = true;
            Find<ComboBox>(q, "DpiCombo").SelectedIndex = 3;   // 75, 100, 150, 200, 300 → 200
            Find<ComboBox>(q, "DpiCombo").SelectedIndex = 4;   // 300
            Find<ComboBox>(q, "AreaCombo").SelectedIndex = 2;   // Carta
            Find<System.Windows.Controls.Slider>(q, "BrightnessSlider").Value = 20;
            Find<System.Windows.Controls.Slider>(q, "ContrastSlider").Value = -10;
            Find<System.Windows.Controls.Slider>(q, "JpegSlider").Value = 95;
            Assert.Contains("megapíxeles", Find<TextBlock>(q, "EstimateText").Text);
            Click(Find<Button>(q, "OkButton"));
        });
        Run(() => Click(w.QualityButton));
        Run(() =>
        {
            Assert.Equal("300 ppp · Blanco y negro · Carta", w.QualitySummaryText.Text);
            Assert.Equal(QualityPreset.Custom, App.App.Settings.Preset);
            Assert.Equal((20, -10, 95), (App.App.Settings.CustomQuality.Brightness, App.App.Settings.CustomQuality.Contrast, App.App.Settings.CustomQuality.JpegQuality));
            Assert.Equal(QualityPreset.Custom, ((ComboBoxItem)w.PresetCombo.SelectedItem).Tag);
        });

        // Cancelar el diálogo no cambia nada.
        Run(() => Click(w.QualityButton));
        Assert.Equal("300 ppp · Blanco y negro · Carta", Run(() => w.QualitySummaryText.Text));

        // Foto: este dispositivo no llega a 600 y se dice.
        Run(() => w.PresetCombo.SelectedIndex = (int)QualityPreset.Photo);
        Assert.Equal("300 ppp · Color · Todo el cristal (pediste 600; este dispositivo no llega)", Run(() => w.QualitySummaryText.Text));

        // Con un escaneo enorme se avisa en el diálogo.
        Answer<QualityWindow>(q =>
        {
            Find<RadioButton>(q, "ColorColorRadio").IsChecked = true;
            Find<ComboBox>(q, "AreaCombo").SelectedIndex = 0;
            Assert.Contains("1 bit", "1 bit");
            q.Close();
        });
        Run(() => Click(w.QualityButton));
        Run(() =>
        {
            var big = new QualityWindow(new ScanQuality { Dpi = 1200, Color = ColorMode.Color, Area = PageArea.A4 }, new DeviceCapabilities { MinDpi = 100, MaxDpi = 1200 }, DeviceKind.Scanner);
            Show(big);
            Assert.Contains("tardará un rato", Find<TextBlock>(big, "EstimateText").Text);
            Assert.Contains("de 100 a 1200 ppp", AllText(big));
            big.Close();
            var noCaps = Show(new QualityWindow(new ScanQuality { Dpi = 190 }, null, DeviceKind.Scanner));
            Assert.Equal("200 ppp", ((ComboBoxItem)Find<ComboBox>(noCaps, "DpiCombo").SelectedItem).Content);
            noCaps.Close();
        });
        Close(w);
    }

    [Fact]
    public void Con_la_camara_se_ve_en_directo_y_se_captura()
    {
        var w = Open();
        Run(() => w.DeviceCombo.SelectedIndex = 1);
        WaitUntil(() => w.CameraPanel.Visibility == Visibility.Visible && w.PreviewImage.Source is WriteableBitmap && w.ScanButton.IsEnabled, "sale la imagen en directo");
        Run(() =>
        {
            Assert.Equal(Visibility.Visible, w.LiveBadge.Visibility);
            Assert.Equal(Visibility.Collapsed, w.SourcePanel.Visibility);
            Assert.Equal(2, w.CameraResCombo.Items.Count);
            Assert.Contains("640×480", w.DeviceInfoText.Text);
            Assert.Equal("Cámara · Gris", w.QualitySummaryText.Text);
            Assert.Equal(App.App.SimulatedCam!.Info.Id, App.App.Settings.DeviceId);
            w.CameraResCombo.SelectedIndex = 1;
        });
        WaitUntil(() => ((WriteableBitmap?)w.PreviewImage.Source)?.PixelWidth == 320, "cambia la resolución");
        Assert.Equal((320, 240), App.App.Settings.CameraResolution);
        Scan(w);
        Run(() =>
        {
            Assert.Equal(1, App.App.Workflow.Document.Count);
            Assert.Equal(-1, w.PageList.SelectedIndex);   // sigue en directo
            Assert.Contains("Hoja escaneada", w.StatusText.Text);
            w.PageList.SelectedIndex = 0;
        });
        WaitUntil(() => w.LiveBadge.Visibility == Visibility.Collapsed && w.PreviewImage.Source is not WriteableBitmap, "se ve la hoja capturada");
        Run(() =>
        {
            // Con la cámara, la calidad no tiene resolución ni zona.
            var q = Show(new QualityWindow(App.App.Settings.EffectiveQuality(), null, DeviceKind.Camera));
            Assert.Null(TryFind<ComboBox>(q, "DpiCombo"));
            Assert.Contains("resolución elegida", Find<TextBlock>(q, "EstimateText").Text);
            q.Close();
            App.App.Workflow.Document.Clear();
            w.DeviceCombo.SelectedIndex = 0;
        });
        WaitUntil(() => w.CameraPanel.Visibility == Visibility.Collapsed && w.ScanButton.IsEnabled, "vuelve al escáner");
        WaitUntil(() => !App.App.SimulatedCam!.IsRunning, "la cámara se apaga");
        Close(w);
    }

    [Fact]
    public void Los_fallos_del_escaner_se_explican_y_se_puede_cancelar()
    {
        var w = Open();
        App.App.Simulated!.FailWith = ScanErrors.WiaPaperJam;
        AnswerOk();
        Run(() => Click(w.ScanButton));
        WaitUntil(() => w.ScanButton.IsEnabled && w.StatusText.Text == "No se ha podido hacer.", "avisa del atasco");
        Assert.Contains("atascado", AllText(Modals[^1]));

        App.App.Workflow.RetryDelay = TimeSpan.Zero;
        App.App.Simulated.FailWith = ScanErrors.WiaWarmingUp;
        Scan(w);
        Assert.Equal(1, Run(() => App.App.Workflow.Document.Count));   // se reintentó solo

        App.App.Simulated.Delay = TimeSpan.FromSeconds(4);
        Run(() => Click(w.ScanButton));
        WaitUntil(() => w.CancelButton.Visibility == Visibility.Visible, "sale cancelar");
        Assert.False(Run(() => w.DeviceCombo.IsEnabled));
        Run(() => Click(w.CancelButton));
        WaitUntil(() => w.StatusText.Text == "Cancelado.", "se cancela");

        Run(() => Click(w.ScanButton));
        WaitUntil(() => w.CancelButton.Visibility == Visibility.Visible, "sale cancelar otra vez");
        Run(() => w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent }));
        WaitUntil(() => w.StatusText.Text == "Cancelado.", "Esc cancela");

        // Intro escanea.
        App.App.Simulated.Delay = TimeSpan.Zero;
        Run(() => w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent }));
        WaitUntil(() => App.App.Workflow.Document.Count == 2 && w.ScanButton.IsEnabled, "Intro escanea");
        Run(() => App.App.Workflow.Document.Clear());
        Close(w);
    }

    [Fact]
    public void Descartar_y_cerrar_con_hojas_sin_guardar_piden_confirmacion()
    {
        var w = Open();
        Scan(w);
        AnswerCancel();
        Run(() => Click(w.ClearButton));
        Assert.Equal(1, Run(() => App.App.Workflow.Document.Count));
        AnswerOk();
        Run(() => Click(w.ClearButton));
        Assert.Equal(0, Run(() => App.App.Workflow.Document.Count));
        Assert.Equal("Listo para escanear.", Run(() => w.StatusText.Text));

        Scan(w);
        AnswerCancel();
        Run(() => w.Close());
        Assert.True(Run(() => w.IsVisible));
        Assert.Contains("sin guardar", AllText(Modals[^1]));
        AnswerOk();
        Run(() => { w.Width = 1100; w.Close(); });
        Assert.False(Run(() => w.IsVisible));
        Assert.Equal(1100, App.App.Settings.WindowWidth);
        Run(() => App.App.Workflow.Document.Clear());
    }

    [Fact]
    public void Cabecera_guia_novedades_idioma_y_acerca_de()
    {
        var w = Open();
        var language = Loc.Language;
        Answer<GuideWindow>(g =>
        {
            Assert.Equal("Hecho", Find<TextBlock>(g, "GuideStateText").Text);   // hay dispositivos
            Click(Find<Button>(g, "GuideActionButton"));
            for (var i = 0; i < 3; i++)
                Click(Find<Button>(g, "GuideNextButton"));
            Click(Find<Button>(g, "GuideBackButton"));
            Click(Find<Button>(g, "GuideNextButton"));
            Click(Find<Button>(g, "GuideNextButton"));   // terminar
        });
        Run(() => Click(w.GuideButton));
        Assert.Equal("ms-settings:printers", Started[^1].FileName);

        Answer<WhatsNewWindow>(n => Assert.Contains("Primera versión", AllText(n)));
        Run(() => Click(w.NewsButton));

        Answer<AboutWindow>(a =>
        {
            Assert.Contains("v" + WhatsNew.CurrentVersion(), AllText(a));
            Click(Find<Button>(a, "ContactButton"));
            a.Close();
        });
        Run(() => Click(w.AboutButton));
        Assert.Contains("github.com/donki/Scanner", Started[^1].FileName);

        Run(() => Click(w.LanguageButton));
        Assert.NotEqual(language, Loc.Language);
        Loc.Use(language);
        Close(w);
    }

    [Fact]
    public void Guia_paso_a_paso()
    {
        Reset();
        PickedFolder = Path.Combine(SandboxFolder, "serie-guia");
        Answer<QualityWindow>(q =>
        {
            Find<RadioButton>(q, "ColorColorRadio").IsChecked = true;
            Click(Find<Button>(q, "OkButton"));
        });
        Run(() =>
        {
            var g = Show(new GuideWindow(0));
            Assert.Equal("Pendiente", Find<TextBlock>(g, "GuideStateText").Text);   // sin dispositivos
            Click(Find<Button>(g, "GuideNextButton"));
            Click(Find<Button>(g, "GuideActionButton"));
            Assert.Equal("ms-settings:privacy-webcam", Started[^1].FileName);
            Assert.Contains(Find<TextBlock>(g, "GuideStateText").Text, new[] { "Opcional", "Pendiente" });
            Click(Find<Button>(g, "GuideNextButton"));
            Click(Find<Button>(g, "GuideActionButton"));
            Click(Find<Button>(g, "GuideNextButton"));
            Click(Find<Button>(g, "GuideActionButton"));
            Assert.Equal("Hecho", Find<TextBlock>(g, "GuideStateText").Text);
            Click(Find<Button>(g, "CloseButton"));
            Assert.False(g.IsVisible);
            var none = Show(new GuideWindow(null));
            Assert.Equal("Opcional", Find<TextBlock>(none, "GuideStateText").Text);
            none.Close();
        });
        Assert.Equal(QualityPreset.Custom, App.App.Settings.Preset);
        Assert.Equal(ColorMode.Color, App.App.Settings.CustomQuality.Color);
        Assert.Equal(Path.Combine(SandboxFolder, "serie-guia"), App.App.Settings.BatchFolder);
        Assert.IsType<bool>(GuideWindow.CameraAllowed());
    }

    [Fact]
    public void Acerca_de_cambia_de_idioma_con_sus_banderas()
    {
        Reset();
        var main = Run(() => Show(new MainWindow()));
        App.App.MainView = main;
        Answer<AboutWindow>(a =>
        {
            Answer<WhatsNewWindow>(_ => { });
            Click(Find<Button>(a, "NewsButton"));
            Click(Find<Button>(a, "SpanishButton"));   // ya es el activo: no hace nada
            Assert.True(a.IsVisible);
            Click(Find<Button>(a, "EnglishButton"));   // se cierra y se vuelve a abrir en inglés
        });
        Answer<AboutWindow>(a => Assert.Contains("Language", AllText(a)));
        Run(() => Click(main.AboutButton));
        WaitUntil(() => Loc.Language == "en" && Modals.Count >= 3 && !Modals[^1].IsVisible, "se reabre en inglés");
        Loc.Use("es");
        App.App.MainView = null;
        Close(main);
    }

    [Fact]
    public void Avisos_y_confirmaciones()
    {
        Reset();
        AnswerOk();
        Run(() => PromptWindow.Alert(null, "Título", "Sin dueña: arriba del todo"));
        Assert.True(Run(() => Modals[^1].Topmost));
        AnswerOk();
        Assert.True(Run(() => PromptWindow.Confirm(null, "T", "¿Seguro?", danger: true)));
        AnswerCancel();
        Assert.False(Run(() => PromptWindow.Confirm(null, "T", "¿Seguro?", danger: false)));
        var closedByEsc = false;
        Answer(m =>
        {
            m.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(m), 0, Key.Escape) { RoutedEvent = UIElement.KeyDownEvent });
            closedByEsc = !m.IsVisible;
            return closedByEsc;
        });
        Assert.False(Run(() => PromptWindow.Confirm(null, "T", "Esc", danger: false)));
        Assert.Null(LastError);
        Assert.True(closedByEsc, "Esc no cierra el aviso");
    }

    [Fact]
    public void La_aplicacion_rehace_la_ventana_al_cambiar_de_idioma_y_ensena_la_guia_la_primera_vez()
    {
        Reset(new ScannerSettings { Language = "es" });
        var app = Run(() => (App.App)Application.Current);
        var main = Run(() => Show(new MainWindow()));
        App.App.MainView = main;
        WaitUntil(() => main.ScanButton.IsEnabled, "arranca");
        Scan(main);
        Answer<GuideWindow>(_ => { });
        Run(app.ShowFirstRunWindows);
        Assert.IsType<GuideWindow>(Modals[^1]);
        Assert.True(App.App.Settings.GuideShown);

        App.App.Settings.LastSeenVersion = "1.0.0.0";
        Answer<WhatsNewWindow>(_ => { });
        Run(app.ShowFirstRunWindows);
        Assert.IsType<WhatsNewWindow>(Modals[^1]);
        Assert.Equal(WhatsNew.CurrentVersion(), App.App.Settings.LastSeenVersion);

        Run(() =>
        {
            Loc.Use("en");
            app.OnLanguageChanged();
        });
        var rebuilt = App.App.MainView!;
        Assert.NotSame(main, rebuilt);
        Assert.False(Run(() => main.IsVisible));
        Assert.Equal("en", App.App.Settings.Language);
        WaitUntil(() => rebuilt.ScanButton.IsEnabled && rebuilt.PageList.Items.Count == 1, "la nueva tiene la hoja");
        Assert.Equal("Scan", Run(() => System.Windows.Automation.AutomationProperties.GetName(rebuilt.ScanButton)));
        Run(() => app.ShowMain());   // ya abierta: solo se enseña
        Assert.Same(rebuilt, App.App.MainView);
        Run(() =>
        {
            rebuilt.WindowState = WindowState.Minimized;
            app.ShowMain();
            Assert.Equal(WindowState.Normal, rebuilt.WindowState);
            Hide(rebuilt);
            App.App.Workflow.Document.Clear();
            rebuilt.Close();
        });
        Assert.Null(App.App.MainView);
        Run(() => app.ShowMain());   // sin ventana: la crea
        Assert.NotNull(App.App.MainView);
        Run(() => { Hide(App.App.MainView!); App.App.MainView!.Close(); });
        Run(() => app.Alert("T", "un aviso"));
        Loc.Use("es");
    }

    [Fact]
    public void Servicios_de_plataforma()
    {
        Reset();
        Run(() =>
        {
            Assert.Equal("Escanear", new TExtension("ActionScan").ProvideValue(null!));
            Assert.Equal(string.Empty, new TExtension().ProvideValue(null!));
            Assert.True(Sandbox.IsOn);
            Assert.Equal(SandboxFolder, Sandbox.Folder);
            var dark = ThemeManager.IsDark;
            ThemeManager.Apply();
            Assert.Equal(dark, ThemeManager.IsDark);
            Assert.NotNull(Ui.Brush("CardBackground"));
            var w = Show(new WhatsNewWindow());
            Assert.NotNull(w.Icon);
            ThemeManager.ApplyToWindow(w);
            w.Close();
            ThemeManager.ApplyToWindow(new Window());   // sin ventana nativa: no hace nada
        });
        Assert.NotEqual(0u, SingleInstance.ShowMessage);
        SingleInstance.NotifyShown();
    }
}
