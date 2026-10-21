using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.Scanning;

namespace SocScanner.Output;

/// <summary>
/// PDF propio, sin bibliotecas: una página por hoja, del tamaño real de la hoja (píxeles ÷ ppp), con la
/// imagen a página completa. El color y el gris van en JPEG (DCTDecode); el blanco y negro, en 1 bit
/// comprimido sin pérdida (FlateDecode), que deja una hoja de texto en unas decenas de KB. PDF 1.4,
/// el que abre cualquier lector.
/// </summary>
public static class PdfWriter
{
    public static void Write(Stream output, IReadOnlyList<ScannedPage> pages, int jpegQuality, DateTime? created = null)
    {
        if (pages.Count == 0)
            throw new ArgumentException("No pages", nameof(pages));
        var w = new Writer(output);
        w.Raw("%PDF-1.4\n%âãÏÓ\n");

        // 1 catálogo, 2 páginas, 3 información; luego por hoja: imagen, contenido y página.
        const int catalog = 1, pagesId = 2, info = 3;
        var pageIds = new List<int>();
        var next = 4;
        foreach (var page in pages)
        {
            int imageId = next++, contentId = next++, pageId = next++;
            pageIds.Add(pageId);
            var (data, filter, colorSpace, bits) = EncodeImage(page, jpegQuality);
            w.BeginObject(imageId);
            w.Raw($"<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} /ColorSpace /{colorSpace} /BitsPerComponent {bits} /Filter /{filter} /Length {data.Length} >>\nstream\n");
            w.Bytes(data);
            w.Raw("\nendstream\n");
            w.EndObject();

            var (pw, ph) = PageSizePoints(page);
            var content = Encoding.ASCII.GetBytes($"q {F(pw)} 0 0 {F(ph)} 0 0 cm /Im0 Do Q\n");
            w.BeginObject(contentId);
            w.Raw($"<< /Length {content.Length} >>\nstream\n");
            w.Bytes(content);
            w.Raw("\nendstream\n");
            w.EndObject();

            w.BeginObject(pageId);
            w.Raw($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {F(pw)} {F(ph)}] /Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>\n");
            w.EndObject();
        }

        w.BeginObject(catalog);
        w.Raw($"<< /Type /Catalog /Pages {pagesId} 0 R >>\n");
        w.EndObject();
        w.BeginObject(pagesId);
        w.Raw($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>\n");
        w.EndObject();
        var when = (created ?? DateTime.Now).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        w.BeginObject(info);
        w.Raw($"<< /Producer (sOC Scanner) /Creator (sOC Scanner) /CreationDate (D:{when}) >>\n");
        w.EndObject();

        w.Finish(next, catalog, info);
    }

    /// <summary>Tamaño de la página en puntos (1/72 de pulgada).</summary>
    public static (double Width, double Height) PageSizePoints(ScannedPage page)
    {
        var dpi = page.Dpi > 1 ? page.Dpi : 96;
        return (page.PixelWidth * 72.0 / dpi, page.PixelHeight * 72.0 / dpi);
    }

    internal static (byte[] Data, string Filter, string ColorSpace, int Bits) EncodeImage(ScannedPage page, int jpegQuality)
    {
        var image = page.Image;
        if (image.Format == PixelFormats.BlackWhite)
        {
            int w = image.PixelWidth, h = image.PixelHeight;
            var stride = (w + 7) / 8;
            var px = new byte[stride * h];
            image.CopyPixels(px, stride, 0);
            return (Deflate(px), "FlateDecode", "DeviceGray", 1);
        }
        var gray = Imaging.ImageOps.IsGray(image);
        BitmapSource src = gray
            ? (image.Format == PixelFormats.Gray8 ? image : new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0))
            : (image.Format == PixelFormats.Bgr24 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0));
        var encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) };
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), "DCTDecode", gray ? "DeviceGray" : "DeviceRGB", 8);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return ms.ToArray();
    }

    private static string F(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Escribe objetos apuntando dónde empieza cada uno, para la tabla de referencias del final.</summary>
    private sealed class Writer(Stream output)
    {
        private readonly Dictionary<int, long> _offsets = [];
        private long _written;

        public void Raw(string text) => Bytes(Encoding.Latin1.GetBytes(text));

        public void Bytes(byte[] data)
        {
            output.Write(data);
            _written += data.Length;
        }

        public void BeginObject(int id)
        {
            _offsets[id] = _written;
            Raw($"{id} 0 obj\n");
        }

        public void EndObject() => Raw("endobj\n");

        public void Finish(int count, int root, int info)
        {
            var xref = _written;
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"xref\n0 {count}\n0000000000 65535 f \n");
            for (var id = 1; id < count; id++)
                sb.Append(CultureInfo.InvariantCulture, $"{_offsets[id]:D10} 00000 n \n");
            sb.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {count} /Root {root} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Raw(sb.ToString());
        }
    }
}
