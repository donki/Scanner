using System.Text;
using System.Text.RegularExpressions;
using SocScanner.Localization;
using SocScanner.Output;
using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Tests;

/// <summary>Ajustes, registro, novedades y textos.</summary>
[Collection("Estado global")]
public sealed class AppTests
{
    [Fact]
    public void Ajustes_se_guardan_y_se_leen_igual()
    {
        using var tmp = new TempFolder();
        var s = new ScannerSettings
        {
            Language = "en",
            DeviceId = "dev",
            Source = ScanSource.Feeder,
            Preset = QualityPreset.Custom,
            CustomQuality = new ScanQuality { Dpi = 400, Color = ColorMode.BlackWhite, Brightness = 10 },
            Mode = ScanMode.Batch,
            BatchPrefix = "Recibo-",
            BatchSuffix = "-ñ",
            BatchNextNumber = 41,
            BatchDigits = 5,
            BatchFormat = OutputFormat.Tiff,
            CameraWidth = 1280,
            CameraHeight = 720,
        };
        Assert.True(s.Save());
        var json = File.ReadAllText(ScannerSettings.FilePath);
        Assert.Contains("\"mode\": \"batch\"", json);
        Assert.Contains("-ñ", json);   // legible a mano
        var back = ScannerSettings.Load();
        Assert.Equal(("en", "dev", ScanSource.Feeder, ScanMode.Batch), (back.Language, back.DeviceId, back.Source, back.Mode));
        Assert.Equal((400, ColorMode.BlackWhite, 10), (back.EffectiveQuality().Dpi, back.EffectiveQuality().Color, back.EffectiveQuality().Brightness));
        Assert.Equal(("Recibo-", "-ñ", 41, 5, OutputFormat.Tiff), (back.BatchPrefix, back.BatchSuffix, back.BatchNextNumber, back.BatchDigits, back.BatchFormat));
        Assert.Equal((1280, 720), back.CameraResolution);
    }

    [Fact]
    public void Ajustes_ilegibles_o_raros_no_impiden_arrancar()
    {
        using var tmp = new TempFolder();
        Assert.Equal(ScanMode.Single, ScannerSettings.Load().Mode);   // sin fichero
        File.WriteAllText(ScannerSettings.FilePath, "{ esto no es json");
        var s = ScannerSettings.Load();
        Assert.Equal(QualityPreset.Document, s.Preset);
        Assert.Contains("ajustes ilegibles", File.ReadAllText(AppLog.FilePath));

        File.WriteAllText(ScannerSettings.FilePath, """
            { "language": "fr", "deviceId": " ", "source": 7, "preset": 99, "mode": 5, "saveFolder": "", "batchFolder": " ",
              "singleFormat": 50, "batchFormat": 60, "batchPrefix": "a:b", "batchSuffix": null, "batchNextNumber": -4,
              "batchDigits": 40, "cameraWidth": 1280, "cameraHeight": 0, "windowWidth": -3, "customQuality": null }
            """);
        var odd = ScannerSettings.Load();
        Assert.Null(odd.Language);
        Assert.Null(odd.DeviceId);
        Assert.Equal((ScanSource.Flatbed, QualityPreset.Document, ScanMode.Single), (odd.Source, odd.Preset, odd.Mode));
        Assert.Equal(ScannerSettings.DefaultFolder, odd.SaveFolder);
        Assert.Equal(ScannerSettings.DefaultFolder, odd.BatchFolder);
        Assert.Equal((OutputFormat.Pdf, OutputFormat.Pdf), (odd.SingleFormat, odd.BatchFormat));
        Assert.Equal(("a_b", ""), (odd.BatchPrefix, odd.BatchSuffix));
        Assert.Equal((0, 9), (odd.BatchNextNumber, odd.BatchDigits));
        Assert.Null(odd.CameraResolution);
        Assert.Equal(0, odd.WindowWidth);
        Assert.NotNull(odd.CustomQuality);

        var longOnes = new ScannerSettings { BatchPrefix = new string('p', 100), BatchSuffix = new string('s', 100) }.Normalize();
        Assert.Equal((80, 80), (longOnes.BatchPrefix.Length, longOnes.BatchSuffix.Length));
    }

    [Fact]
    public void Calidad_efectiva_y_carpetas()
    {
        using var tmp = new TempFolder();
        var s = new ScannerSettings { Preset = QualityPreset.Text, CustomQuality = new ScanQuality { Dpi = 75 } };
        Assert.Equal(300, s.EffectiveQuality().Dpi);
        s.Preset = QualityPreset.Custom;
        Assert.Equal(75, s.EffectiveQuality().Dpi);

        Assert.Null(ScannerSettings.CheckFolder(tmp.File("nueva")));
        Assert.True(Directory.Exists(tmp.File("nueva")));
        Assert.Empty(Directory.GetFiles(tmp.File("nueva")));   // la prueba de escritura no deja nada
        Assert.Equal("ErrFolderEmpty", ScannerSettings.CheckFolder(" "));
        Assert.Equal("ErrFolderNotAbsolute", ScannerSettings.CheckFolder("relativa"));
        File.WriteAllText(tmp.File("fichero"), "");
        Assert.Equal("ErrFolderNotWritable", ScannerSettings.CheckFolder(Path.Combine(tmp.File("fichero"), "dentro")));
    }

    [Fact]
    public void Guardar_ajustes_en_un_sitio_imposible_no_revienta()
    {
        using var tmp = new TempFolder();
        File.WriteAllText(tmp.File("bloque"), "");
        ScannerSettings.FilePath = Path.Combine(tmp.File("bloque"), "settings.json");
        Assert.False(new ScannerSettings().Save());
    }

    [Fact]
    public void El_registro_rota_al_pasar_de_un_mega()
    {
        using var tmp = new TempFolder();
        AppLog.Write("primero");
        Assert.Contains("primero", File.ReadAllText(AppLog.FilePath));
        File.WriteAllText(AppLog.FilePath, new string('x', 1024 * 1024 + 10));
        AppLog.Write("tras rotar");
        Assert.True(File.Exists(Path.Combine(tmp.Path, "errors.old.log")));
        Assert.DoesNotContain("xxxx", File.ReadAllText(AppLog.FilePath));
        AppLog.Folder = Path.Combine(tmp.File("no-es-carpeta"));
        File.WriteAllText(AppLog.Folder, "");
        AppLog.Write("no revienta");
    }

    [Fact]
    public void Novedades_empiezan_por_esta_version_y_en_los_dos_idiomas()
    {
        var es = WhatsNew.Load("es");
        var en = WhatsNew.Load("en");
        Assert.NotEmpty(es);
        Assert.True(WhatsNew.SameVersion(es[0].Version, WhatsNew.CurrentVersion()), $"whatsnew.json empieza por {es[0].Version} y la versión es {WhatsNew.CurrentVersion()}");
        Assert.Equal(es.Count, en.Count);
        Assert.All(es.Zip(en), p => Assert.Equal(p.First.Items.Count, p.Second.Items.Count));
        Assert.NotNull(es[0].Date);
        Assert.True(es.Count <= WhatsNew.MaxVersions);
    }

    [Fact]
    public void Novedades_de_un_fichero_raro()
    {
        using var tmp = new TempFolder();
        var json = """[{ "version": "1.0", "date": "no", "fr": ["x"] }, { "version": "2.0", "en": ["a", ""] }]""";
        var list = WhatsNew.Load("es", new MemoryStream(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(2, list.Count);
        Assert.Empty(list[0].Items);
        Assert.Null(list[0].Date);
        Assert.Equal(["a"], list[1].Items);
        Assert.Empty(WhatsNew.Load("es", new MemoryStream(Encoding.UTF8.GetBytes("{roto"))));
        Assert.True(WhatsNew.SameVersion("2026.10.21.0", "2026.10.21.00"));
        Assert.False(WhatsNew.SameVersion("2026.10.21.0", null));
        Assert.True(WhatsNew.SameVersion("x", "x"));
        Assert.Equal((true, false), WhatsNew.OnStartup(new ScannerSettings(), "1.0.0.0"));
        Assert.Equal((false, true), WhatsNew.OnStartup(new ScannerSettings { GuideShown = true, LastSeenVersion = "0.9.0.0" }, "1.0.0.0"));
        Assert.Equal((false, false), WhatsNew.OnStartup(new ScannerSettings { GuideShown = true, LastSeenVersion = "1.0.0.0" }, "1.0.0.0"));
    }

    [Fact]
    public void Los_dos_idiomas_tienen_las_mismas_claves_y_los_mismos_huecos()
    {
        Assert.Equal(Loc.EnglishTable.Keys.Order(), Loc.SpanishTable.Keys.Order());
        Assert.All(Loc.EnglishTable.Values.Concat(Loc.SpanishTable.Values), v => Assert.False(string.IsNullOrWhiteSpace(v)));
        foreach (var (key, en) in Loc.EnglishTable)
        {
            var holes = Regex.Matches(en, @"\{\d+\}").Select(m => m.Value).Distinct().Order();
            var esHoles = Regex.Matches(Loc.SpanishTable[key], @"\{\d+\}").Select(m => m.Value).Distinct().Order();
            Assert.True(holes.SequenceEqual(esHoles), $"{key}: los huecos no coinciden");
        }
    }

    [Fact]
    public void Todas_las_claves_que_usa_el_codigo_existen()
    {
        var root = FindRoot();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"Loc\.(?:Get|Format)\(""([A-Za-z0-9]+)""(?!\s*\+)|\{loc:T ([A-Za-z0-9]+)\}|""((?:Err|Status)[A-Z][A-Za-z0-9]+)"""))
            {
                var key = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
                if (!key.EndsWith("Text"))   // StatusText, DetailText…: nombres de controles, no textos
                    used.Add(key);
            }
        }
        // Las que se arman con el nombre de un enumerado.
        foreach (var p in Enum.GetValues<QualityPreset>()) used.Add("Preset" + p);
        foreach (var f in Enum.GetValues<OutputFormat>()) used.Add("Format" + f);
        foreach (var c in Enum.GetValues<ColorMode>()) used.Add("Color" + c);
        foreach (var a in Enum.GetValues<PageArea>()) used.Add("Area" + a);
        var missing = used.Where(k => !Loc.EnglishTable.ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "faltan: " + string.Join(", ", missing));
        Assert.True(used.Count > 150);
    }

    [Fact]
    public void Cambio_de_idioma_y_reserva_en_ingles()
    {
        var before = Loc.Language;
        var changes = 0;
        void OnChange() => changes++;
        Loc.LanguageChanged += OnChange;
        try
        {
            Loc.Use("es");
            Loc.Use("es");
            Assert.Equal("Escanear", Loc.Get("ActionScan"));
            Loc.Toggle();
            Assert.Equal("Scan", Loc.Get("ActionScan"));
            Loc.Use("fr");   // lo que no es español, inglés
            Assert.Equal("en", Loc.Language);
            Assert.Equal(string.Empty, Loc.Get("NoExiste"));   // nunca la clave
            Assert.Equal("Page 3", Loc.Format("PageItem", 3));
            Assert.Equal("{0} dpi", Loc.Format("DpiItem"));   // sin argumentos: el texto tal cual
            Assert.True(changes >= 1);
        }
        finally
        {
            Loc.LanguageChanged -= OnChange;
            Loc.Use(before);
        }
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Scanner.slnx")))
                return dir.FullName;
        throw new DirectoryNotFoundException("Scanner.slnx");
    }
}
