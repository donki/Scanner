using System.Windows.Media;
using SocScanner.Imaging;
using SocScanner.Scanning;

namespace SocScanner.Tests;

/// <summary>Gris, blanco y negro, brillo y contraste, giros, miniaturas y la imagen de la cámara.</summary>
public sealed class ImageTests
{
    [Fact]
    public void ToGray_y_ToBlackWhite_separan_la_letra_del_papel()
    {
        var src = Images.TwoTone(dark: 40, light: 210);
        var gray = ImageOps.ToGray(src);
        Assert.Equal(PixelFormats.Gray8, gray.Format);
        Assert.True(gray.IsFrozen);
        Assert.Same(gray, ImageOps.ToGray(gray));

        var bw = ImageOps.ToBlackWhite(src);
        Assert.Equal(PixelFormats.BlackWhite, bw.Format);
        Assert.Equal(200, bw.DpiX, 0);
        var px = Images.Pixels(bw);
        var stride = (bw.PixelWidth + 7) / 8;
        Assert.Equal(0, px[0] & 0x80);   // izquierda oscura: negro
        Assert.NotEqual(0, px[stride - 1] & 0x01 | px[stride - 1]);   // derecha clara: blanco
        Assert.Same(bw, ImageOps.ToBlackWhite(bw));
        Assert.True(ImageOps.IsGray(bw));
        Assert.False(ImageOps.IsGray(src));
    }

    [Fact]
    public void Otsu_encuentra_el_corte_entre_dos_grupos()
    {
        var data = Enumerable.Repeat((byte)30, 500).Concat(Enumerable.Repeat((byte)200, 500)).ToArray();
        var t = ImageOps.OtsuThreshold(data);
        Assert.InRange(t, 30, 199);
        Assert.Equal(127, ImageOps.OtsuThreshold(Enumerable.Repeat((byte)255, 100).ToArray()));   // hoja en blanco: no sale negra
        Assert.Equal(127, ImageOps.OtsuThreshold(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Una_hoja_en_blanco_sigue_blanca_en_blanco_y_negro()
    {
        var white = SimulatedScanner.Draw(16, 16, 0, ColorMode.Color);   // con número 0 no hay barras
        var px = Images.Pixels(ImageOps.ToBlackWhite(white));
        Assert.True(px.Count(b => b == 0xFF) > px.Length / 2);
    }

    [Fact]
    public void Brillo_y_contraste()
    {
        var lut = ImageOps.BuildLut(0, 0);
        Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i), lut);
        var brighter = ImageOps.BuildLut(50, 0);
        Assert.True(brighter[100] > 100);
        Assert.Equal(255, brighter[255]);
        var flat = ImageOps.BuildLut(0, -100);
        Assert.Equal(flat[0], flat[255]);   // contraste -100: todo gris
        var hard = ImageOps.BuildLut(0, 100);
        Assert.Equal(0, hard[100]);
        Assert.Equal(255, hard[160]);

        var src = Images.TwoTone(dark: 100, light: 150);
        Assert.Same(src, ImageOps.AdjustBrightnessContrast(src, 0, 0));
        var color = ImageOps.AdjustBrightnessContrast(src, 40, 0);
        Assert.Equal(PixelFormats.Bgr32, color.Format);
        Assert.True(Images.Pixels(color)[0] > 100);
        var gray = ImageOps.AdjustBrightnessContrast(ImageOps.ToGray(src), -40, 0);
        Assert.Equal(PixelFormats.Gray8, gray.Format);
        Assert.True(Images.Pixels(gray)[0] < 100);
    }

    [Fact]
    public void Finish_aplica_ajustes_y_color_final()
    {
        var src = Images.TwoTone();
        var r = new ScanRequest { FinalColor = ColorMode.BlackWhite, SoftwareBrightness = 10 };
        Assert.Equal(PixelFormats.BlackWhite, ImageOps.Finish(src, r).Format);
        Assert.Equal(PixelFormats.Gray8, ImageOps.Finish(src, new ScanRequest { FinalColor = ColorMode.Grayscale }).Format);
        Assert.Same(src, ImageOps.Finish(src, new ScanRequest { FinalColor = ColorMode.Color }));
        var gray = ImageOps.ToGray(src);
        Assert.Same(gray, ImageOps.Finish(gray, new ScanRequest { FinalColor = ColorMode.Grayscale }));
    }

    [Theory]
    [InlineData(90, 20, 40)]
    [InlineData(-90, 20, 40)]
    [InlineData(180, 40, 20)]
    [InlineData(360, 40, 20)]
    [InlineData(450, 20, 40)]
    public void Rotate_gira_de_90_en_90(int degrees, int w, int h)
    {
        var r = ImageOps.Rotate(Images.TwoTone(40, 20), degrees);
        Assert.Equal((w, h), (r.PixelWidth, r.PixelHeight));
        Assert.True(r.IsFrozen);
    }

    [Fact]
    public void Rotate_a_la_derecha_pone_la_izquierda_arriba()
    {
        var r = ImageOps.Rotate(Images.TwoTone(40, 20, dark: 0, light: 255), 90);
        var px = Images.Pixels(r);
        var stride = r.PixelWidth * 3;
        Assert.Equal(0, px[0]);   // arriba: lo que era la izquierda (oscuro)
        Assert.Equal(255, px[(r.PixelHeight - 1) * stride]);
    }

    [Fact]
    public void Thumbnail_reduce_sin_agrandar_y_sabe_con_1_bit()
    {
        var big = SimulatedScanner.Draw(400, 600, 1, ColorMode.BlackWhite);
        var t = ImageOps.Thumbnail(big, 100);
        Assert.Equal(100, t.PixelWidth);
        Assert.Equal(150, t.PixelHeight);
        var small = ImageOps.Thumbnail(Images.TwoTone(40, 20), 100);
        Assert.Equal(40, small.PixelWidth);
    }

    [Fact]
    public void FromBgra_y_WithDpi()
    {
        var bgra = new byte[4 * 4 * 2];
        for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 10; bgra[i + 1] = 20; bgra[i + 2] = 30; bgra[i + 3] = 255; }
        var img = ImageOps.FromBgra(bgra, 4, 2, 16, 150);
        Assert.Equal(PixelFormats.Bgr24, img.Format);
        Assert.Equal(150, img.DpiX, 0);
        Assert.Equal([10, 20, 30], Images.Pixels(img)[..3]);

        var src = Images.TwoTone(dpi: 96);
        Assert.Same(src, ImageOps.WithDpi(src, 96.2));
        var moved = ImageOps.WithDpi(src, 300);
        Assert.Equal(300, moved.DpiX, 0);
        Assert.Equal(Images.Pixels(src), Images.Pixels(moved));
    }

    [Fact]
    public void Materialize_congela_y_copia()
    {
        var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(Images.TwoTone(), PixelFormats.Gray8, null, 0);
        var m = ImageOps.Materialize(conv);
        Assert.True(m.IsFrozen);
        Assert.Equal(PixelFormats.Gray8, m.Format);
        // Una imagen sin congelar que se puede congelar: Finish la congela en su sitio.
        var loose = new System.Windows.Media.Imaging.FormatConvertedBitmap(Images.TwoTone(), PixelFormats.Bgr24, null, 0);
        var done = ImageOps.Finish(loose, new ScanRequest { FinalColor = ColorMode.Color });
        Assert.True(done.IsFrozen);
    }

    [Fact]
    public void El_escaner_simulado_dibuja_la_hoja_con_su_color()
    {
        Assert.Equal(PixelFormats.Bgr24, SimulatedScanner.Draw(50, 70, 3, ColorMode.Color).Format);
        Assert.Equal(PixelFormats.Gray8, SimulatedScanner.Draw(50, 70, 3, ColorMode.Grayscale).Format);
        Assert.Equal(PixelFormats.BlackWhite, SimulatedScanner.Draw(50, 70, 3, ColorMode.BlackWhite).Format);
        var tiny = SimulatedScanner.Draw(1, 1, 30, ColorMode.Color);
        Assert.Equal((16, 16), (tiny.PixelWidth, tiny.PixelHeight));
    }
}
