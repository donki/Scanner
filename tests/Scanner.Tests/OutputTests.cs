using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media;
using SocScanner.Output;
using SocScanner.Scanning;

namespace SocScanner.Tests;

/// <summary>Nombres correlativos, formatos, el PDF propio y el guardado en cada formato.</summary>
[Collection("Estado global")]
public sealed class OutputTests
{
    [Fact]
    public void SeriesName_pone_prefijo_ceros_y_sufijo()
    {
        Assert.Equal("Factura_0007.pdf", FileNaming.SeriesName("Factura_", 7, 4, null, OutputFormat.Pdf));
        Assert.Equal("0012-2026.jpg", FileNaming.SeriesName(null, 12, 4, "-2026", OutputFormat.Jpeg));
        Assert.Equal("123456.png", FileNaming.SeriesName("", 123456, 3, "", OutputFormat.Png));   // no se corta un número más largo
        Assert.Equal("a_1_b.tif", FileNaming.SeriesName("a_", 1, 0, "_b", OutputFormat.Tiff));   // cifras de 1 como mínimo
        Assert.Equal("x0.bmp", FileNaming.SeriesName("x", -5, 1, null, OutputFormat.Bmp));
        Assert.Equal("a_b0001.pdf", FileNaming.SeriesName("a/b", 1, 4, null, OutputFormat.Pdf));   // lo que Windows no admite, fuera
    }

    [Fact]
    public void NextFree_salta_los_numeros_cogidos_tambien_los_de_varias_hojas()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("C:\\d", "F0001.pdf"),
            Path.Combine("C:\\d", "F0002 (1).pdf"),
            Path.Combine("C:\\d", "F0004.pdf"),
        };
        Assert.Equal(3, FileNaming.NextFree("C:\\d", "F", 1, 4, null, OutputFormat.Pdf, taken.Contains));
        Assert.Equal(5, FileNaming.NextFree("C:\\d", "F", 4, 4, null, OutputFormat.Pdf, taken.Contains));
        Assert.Equal(1, FileNaming.NextFree("C:\\d", "F", 1, 4, null, OutputFormat.Png, taken.Contains));   // otro formato, otra serie
        Assert.Equal(0, FileNaming.NextFree("C:\\d", "F", -3, 4, null, OutputFormat.Pdf, taken.Contains));
    }

    [Fact]
    public void Nombres_por_fecha_por_hoja_y_sin_pisar()
    {
        Assert.Equal("Escaneo 2026-10-21 09.05.03", FileNaming.TimestampName("Escaneo", new DateTime(2026, 10, 21, 9, 5, 3)));
        Assert.StartsWith("Scan ", FileNaming.TimestampName("  ", DateTime.Now));
        Assert.Equal([@"C:\d\a.pdf"], FileNaming.PagePaths(@"C:\d\a.pdf", 3, OutputFormat.Pdf));
        Assert.Equal([@"C:\d\a.tif"], FileNaming.PagePaths(@"C:\d\a.tif", 3, OutputFormat.Tiff));
        Assert.Equal([@"C:\d\a.jpg"], FileNaming.PagePaths(@"C:\d\a.jpg", 1, OutputFormat.Jpeg));
        Assert.Equal([@"C:\d\a (1).jpg", @"C:\d\a (2).jpg"], FileNaming.PagePaths(@"C:\d\a.jpg", 2, OutputFormat.Jpeg));
        var exists = new HashSet<string> { @"C:\d\a.pdf", @"C:\d\a (2).pdf" };
        Assert.Equal(@"C:\d\a (3).pdf", FileNaming.Unique(@"C:\d\a.pdf", exists.Contains));
        Assert.Equal(@"C:\d\b.pdf", FileNaming.Unique(@"C:\d\b.pdf", exists.Contains));
    }

    [Fact]
    public void CheckAffix_y_Sanitize()
    {
        Assert.Null(FileNaming.CheckAffix(null));
        Assert.Null(FileNaming.CheckAffix("Factura_"));
        Assert.Equal("ErrAffixChars", FileNaming.CheckAffix("a:b"));
        Assert.Equal("ErrAffixLong", FileNaming.CheckAffix(new string('a', 81)));
        Assert.Equal("Scan", FileNaming.Sanitize(" . ", allowEmpty: false));
        Assert.Equal("", FileNaming.Sanitize("", allowEmpty: true));
        Assert.Equal("a_b", FileNaming.Sanitize("a?b", allowEmpty: true));
    }

    [Fact]
    public void Formatos()
    {
        Assert.Equal(5, OutputFormats.All.Count);
        Assert.Equal([".pdf", ".jpg", ".png", ".tif", ".bmp"], OutputFormats.All.Select(OutputFormats.Extension));
        Assert.Equal(".pdf", OutputFormats.Extension((OutputFormat)99));
        Assert.Equal(["PDF", "JPG", "PNG", "TIFF", "BMP"], OutputFormats.All.Select(OutputFormats.Label));
        Assert.True(OutputFormats.IsMultiPage(OutputFormat.Pdf));
        Assert.True(OutputFormats.IsMultiPage(OutputFormat.Tiff));
        Assert.False(OutputFormats.IsMultiPage(OutputFormat.Jpeg));
        Assert.Equal(OutputFormat.Jpeg, OutputFormats.FromExtension(".JPEG"));
        Assert.Equal(OutputFormat.Tiff, OutputFormats.FromExtension("tiff"));
        Assert.Equal(OutputFormat.Bmp, OutputFormats.FromExtension("bmp"));
        Assert.Equal(OutputFormat.Png, OutputFormats.FromExtension(".png"));
        Assert.Equal(OutputFormat.Pdf, OutputFormats.FromExtension("pdf"));
        Assert.Null(OutputFormats.FromExtension(".gif"));
        Assert.Null(OutputFormats.FromExtension(null));
        var filter = OutputFormats.DialogFilter();
        Assert.Equal(10, filter.Split('|').Length);
        Assert.StartsWith("PDF (*.pdf)|*.pdf|JPG (*.jpg)|*.jpg", filter);
    }

    [Fact]
    public void Pdf_con_paginas_del_tamano_real_y_tabla_de_referencias_correcta()
    {
        var color = new ScannedPage(SimulatedScanner.Draw(827, 1169, 1, ColorMode.Color), 100, ColorMode.Color);   // A4 a 100 ppp
        var bw = new ScannedPage(SimulatedScanner.Draw(850, 1100, 2, ColorMode.BlackWhite), 100, ColorMode.BlackWhite);   // Carta
        var gray = new ScannedPage(SimulatedScanner.Draw(100, 100, 3, ColorMode.Grayscale), 0, ColorMode.Grayscale);   // sin ppp: 96
        using var ms = new MemoryStream();
        PdfWriter.Write(ms, [color, bw, gray], 80, new DateTime(2026, 10, 21));
        var bytes = ms.ToArray();
        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 3", text);
        Assert.Equal(3, Regex.Matches(text, @"/Type /Page\b(?!s)").Count);
        Assert.Contains("/MediaBox [0 0 595.44 841.68]", text);   // A4 en puntos
        Assert.Contains("/MediaBox [0 0 612 792]", text);   // Carta en puntos
        Assert.Contains("/MediaBox [0 0 75 75]", text);
        Assert.Contains("/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", text);
        Assert.Contains("/ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /FlateDecode", text);
        Assert.Contains("/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /DCTDecode", text);
        Assert.Contains("/CreationDate (D:20261021000000)", text);

        // Cada entrada de la tabla apunta al principio de su objeto.
        var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[startxref..]);
        var entries = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n");
        Assert.Equal(12, entries.Count);   // catálogo, páginas, info y 3 por hoja
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value);
            Assert.StartsWith($"{i + 1} 0 obj", text[offset..]);
        }
    }

    [Fact]
    public void Pdf_en_blanco_y_negro_guarda_los_bits_tal_cual()
    {
        var page = new ScannedPage(SimulatedScanner.Draw(64, 32, 1, ColorMode.BlackWhite), 300, ColorMode.BlackWhite);
        var (data, filter, space, bits) = PdfWriter.EncodeImage(page, 80);
        Assert.Equal(("FlateDecode", "DeviceGray", 1), (filter, space, bits));
        using var z = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(Images.Pixels(page.Image), raw.ToArray());
    }

    [Fact]
    public void Pdf_convierte_formatos_raros_y_rechaza_vacio()
    {
        var bgr32 = new System.Windows.Media.Imaging.FormatConvertedBitmap(Images.TwoTone(), PixelFormats.Bgr32, null, 0);
        bgr32.Freeze();
        var (_, filter, space, _) = PdfWriter.EncodeImage(new ScannedPage(bgr32, 100, ColorMode.Color), 80);
        Assert.Equal(("DCTDecode", "DeviceRGB"), (filter, space));
        var gray16 = new System.Windows.Media.Imaging.FormatConvertedBitmap(Images.TwoTone(), PixelFormats.Gray16, null, 0);
        gray16.Freeze();
        Assert.Equal("DeviceGray", PdfWriter.EncodeImage(new ScannedPage(gray16, 100, ColorMode.Grayscale), 80).ColorSpace);
        Assert.Throws<ArgumentException>(() => PdfWriter.Write(new MemoryStream(), [], 80));
    }

    [Theory]
    [InlineData(OutputFormat.Jpeg, ColorMode.Color)]
    [InlineData(OutputFormat.Jpeg, ColorMode.BlackWhite)]
    [InlineData(OutputFormat.Png, ColorMode.BlackWhite)]
    [InlineData(OutputFormat.Png, ColorMode.Grayscale)]
    [InlineData(OutputFormat.Tiff, ColorMode.BlackWhite)]
    [InlineData(OutputFormat.Tiff, ColorMode.Color)]
    [InlineData(OutputFormat.Bmp, ColorMode.Color)]
    public void Guardar_una_hoja_en_cada_formato_se_puede_volver_a_abrir(OutputFormat format, ColorMode color)
    {
        using var tmp = new TempFolder();
        var page = Images.Page(color, 120, 160, 150);
        var path = tmp.File("hoja" + OutputFormats.Extension(format));
        var saved = ImageSaver.Save([page], format, path, 85);
        Assert.Equal([path], saved);
        var back = Images.Decode(path);
        Assert.Equal((120, 160), (back.PixelWidth, back.PixelHeight));
        Assert.Equal(150, back.DpiX, 0);
        if (format is OutputFormat.Png or OutputFormat.Tiff && color == ColorMode.BlackWhite)
            Assert.Equal(1, back.Format.BitsPerPixel);
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public void Guardar_varias_hojas_en_PDF_y_TIFF_va_en_un_fichero_y_en_JPG_en_varios()
    {
        using var tmp = new TempFolder();
        ScannedPage[] pages = [Images.Page(ColorMode.Color), Images.Page(ColorMode.Grayscale), Images.Page(ColorMode.BlackWhite)];
        var pdf = ImageSaver.Save(pages, OutputFormat.Pdf, tmp.File(Path.Combine("sub", "doc.pdf")), 80);
        Assert.Single(pdf);
        Assert.Contains("/Count 3", File.ReadAllText(pdf[0], Encoding.Latin1));
        var tif = ImageSaver.Save(pages, OutputFormat.Tiff, tmp.File("doc.tif"), 80);
        Assert.Equal(3, Images.FrameCount(tif[0]));
        var jpgs = ImageSaver.Save(pages, OutputFormat.Jpeg, tmp.File("doc.jpg"), 80);
        Assert.Equal(3, jpgs.Count);
        Assert.All(jpgs, f => Assert.True(File.Exists(f)));
        Assert.EndsWith("doc (3).jpg", jpgs[2]);
        Assert.Throws<ArgumentException>(() => ImageSaver.Save([], OutputFormat.Pdf, tmp.File("x.pdf"), 80));
    }

    [Fact]
    public void Un_fallo_al_guardar_sale_con_su_explicacion_y_no_deja_restos()
    {
        using var tmp = new TempFolder();
        var path = tmp.File("bloqueado.pdf");
        File.WriteAllText(path, "abierto en otro programa");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Assert.Throws<SaveException>(() => ImageSaver.Save([Images.Page()], OutputFormat.Pdf, path, 80));
            Assert.Equal("ErrSaveInUse", ex.MessageKey);
        }
        Assert.False(File.Exists(path + ".part"));
        Assert.Equal("abierto en otro programa", File.ReadAllText(path));   // el bueno sigue intacto

        var readOnly = tmp.File("solo-lectura.pdf");
        File.WriteAllText(readOnly, "x");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            var denied = Assert.Throws<SaveException>(() => ImageSaver.Save([Images.Page()], OutputFormat.Pdf, readOnly, 80));
            Assert.Equal("ErrSaveDenied", denied.MessageKey);
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }
}
