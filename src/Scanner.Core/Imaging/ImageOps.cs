using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.Scanning;

namespace SocScanner.Imaging;

/// <summary>
/// Lo que se le hace a la imagen después de escanearla: gris, blanco y negro (umbral de Otsu, sin
/// tramado, que es lo que mejor deja la letra), brillo y contraste cuando el dispositivo no los
/// tiene, y giros. Todo devuelve imágenes congeladas, que se pueden usar desde cualquier hilo.
/// </summary>
public static class ImageOps
{
    /// <summary>Deja la imagen con el color final y los ajustes que no hizo el dispositivo.</summary>
    public static BitmapSource Finish(BitmapSource image, ScanRequest request)
    {
        if (request.SoftwareBrightness != 0 || request.SoftwareContrast != 0)
            image = AdjustBrightnessContrast(image, request.SoftwareBrightness, request.SoftwareContrast);
        return request.FinalColor switch
        {
            ColorMode.Grayscale when !IsGray(image) => ToGray(image),
            ColorMode.BlackWhite when image.Format != PixelFormats.BlackWhite => ToBlackWhite(image),
            _ => Freeze(image),
        };
    }

    public static bool IsGray(BitmapSource image) =>
        image.Format == PixelFormats.Gray8 || image.Format == PixelFormats.Gray16 || image.Format == PixelFormats.BlackWhite
        || image.Format == PixelFormats.Gray2 || image.Format == PixelFormats.Gray4;

    /// <summary>Grises de 8 bits.</summary>
    public static BitmapSource ToGray(BitmapSource image)
    {
        if (image.Format == PixelFormats.Gray8)
            return Freeze(image);
        var gray = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
        return Materialize(gray);
    }

    /// <summary>Blanco y negro de 1 bit con el umbral de Otsu (el que mejor separa la letra del papel).</summary>
    public static BitmapSource ToBlackWhite(BitmapSource image)
    {
        if (image.Format == PixelFormats.BlackWhite)
            return Freeze(image);
        var gray = ToGray(image);
        int w = gray.PixelWidth, h = gray.PixelHeight;
        var src = new byte[w * h];
        gray.CopyPixels(src, w, 0);
        var threshold = OtsuThreshold(src);
        var stride = (w + 7) / 8;
        var dst = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var outRow = y * stride;
            for (var x = 0; x < w; x++)
                if (src[row + x] > threshold)   // claro: blanco (1); oscuro: negro (0)
                    dst[outRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
        }
        var bw = BitmapSource.Create(w, h, image.DpiX, image.DpiY, PixelFormats.BlackWhite, null, dst, stride);
        bw.Freeze();
        return bw;
    }

    /// <summary>Umbral de Otsu sobre un histograma de grises: el que más separa las dos clases.</summary>
    public static int OtsuThreshold(ReadOnlySpan<byte> gray)
    {
        if (gray.Length == 0)
            return 127;
        Span<long> hist = stackalloc long[256];
        foreach (var g in gray)
            hist[g]++;
        long total = gray.Length;
        double sum = 0;
        for (var i = 0; i < 256; i++)
            sum += i * (double)hist[i];
        double sumB = 0, best = -1;
        long wB = 0;
        var threshold = 127;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0)
                continue;
            var wF = total - wB;
            if (wF == 0)
                break;
            sumB += t * (double)hist[t];
            var mB = sumB / wB;
            var mF = (sum - sumB) / wF;
            var between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > best)
            {
                best = between;
                threshold = t;
            }
        }
        // Una hoja de un solo tono (en blanco) no tiene dos clases: que no salga toda negra.
        return best <= 0 ? 127 : threshold;
    }

    /// <summary>Brillo y contraste de -100 a 100 (0 = sin cambio), sobre gris o color de 8 bits por canal.</summary>
    public static BitmapSource AdjustBrightnessContrast(BitmapSource image, int brightness, int contrast)
    {
        brightness = Math.Clamp(brightness, -100, 100);
        contrast = Math.Clamp(contrast, -100, 100);
        if (brightness == 0 && contrast == 0)
            return Freeze(image);
        var lut = BuildLut(brightness, contrast);
        var gray = IsGray(image);
        var format = gray ? PixelFormats.Gray8 : PixelFormats.Bgr32;
        BitmapSource src = image.Format == format ? image : new FormatConvertedBitmap(image, format, null, 0);
        int w = src.PixelWidth, h = src.PixelHeight;
        var bpp = gray ? 1 : 4;
        var stride = w * bpp;
        var px = new byte[stride * h];
        src.CopyPixels(px, stride, 0);
        for (var i = 0; i < px.Length; i++)
        {
            if (!gray && (i & 3) == 3)
                continue;   // el cuarto byte de Bgr32 no se usa
            px[i] = lut[px[i]];
        }
        var result = BitmapSource.Create(w, h, image.DpiX, image.DpiY, format, null, px, stride);
        result.Freeze();
        return result;
    }

    /// <summary>La tabla de un ajuste: brillo desplaza (±128 a tope) y contraste estira alrededor del gris medio.</summary>
    internal static byte[] BuildLut(int brightness, int contrast)
    {
        var lut = new byte[256];
        // Contraste: factor de 0 (todo gris) a ~ (en 100, casi umbral); 0 = 1.
        var c = contrast * 2.55;
        var factor = 259.0 * (c + 255) / (255.0 * (259 - c));
        var shift = brightness * 1.28;
        for (var i = 0; i < 256; i++)
        {
            var v = factor * (i - 128) + 128 + shift;
            lut[i] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
        }
        return lut;
    }

    /// <summary>Gira de 90 en 90 grados (positivo: a la derecha).</summary>
    public static BitmapSource Rotate(BitmapSource image, int degrees)
    {
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0)
            return Freeze(image);
        var rotated = new TransformedBitmap(image, new RotateTransform(degrees));
        return Materialize(rotated);
    }

    /// <summary>Una miniatura del ancho dado (para la tira de hojas), sin cambiar el original.</summary>
    public static BitmapSource Thumbnail(BitmapSource image, int width)
    {
        var scale = Math.Min(1.0, width / (double)Math.Max(1, image.PixelWidth));
        BitmapSource src = image.Format == PixelFormats.BlackWhite ? new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0) : image;
        var thumb = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        return Materialize(thumb);
    }

    /// <summary>Copia los píxeles a un mapa propio: corta la cadena de transformaciones y deja la imagen congelada.</summary>
    /// <summary>
    /// Lista para usarse en cualquier hilo: congelada y sin decodificador detras. Un BitmapFrame leido de
    /// un fichero, aunque este congelado, consulta a su decodificador (atado a su hilo) al codificarlo.
    /// </summary>
    public static BitmapSource Detached(BitmapSource image)
        => image is BitmapFrame || !image.IsFrozen ? (image.CheckAccess() ? Materialize(image) : image.Dispatcher.Invoke(() => Materialize(image))) : image;

    public static BitmapSource Materialize(BitmapSource source)
    {
        var format = source.Format;
        int w = source.PixelWidth, h = source.PixelHeight;
        var stride = (w * format.BitsPerPixel + 7) / 8;
        var px = new byte[stride * h];
        source.CopyPixels(new Int32Rect(0, 0, w, h), px, stride, 0);
        var copy = BitmapSource.Create(w, h, source.DpiX, source.DpiY, format, source.Palette, px, stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>Una imagen BGRA de 32 bits (lo que da la cámara) como mapa congelado de 24 bits sin alfa.</summary>
    public static BitmapSource FromBgra(byte[] bgra, int width, int height, int stride, double dpi)
    {
        var src = BitmapSource.Create(width, height, dpi, dpi, PixelFormats.Bgr32, null, bgra, stride);
        return Materialize(new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0));
    }

    /// <summary>Pone los ppp de la imagen (los escáneres no siempre los escriben en el fichero).</summary>
    public static BitmapSource WithDpi(BitmapSource image, double dpi)
    {
        if (Math.Abs(image.DpiX - dpi) < 0.5 && Math.Abs(image.DpiY - dpi) < 0.5)
            return Freeze(image);
        var format = image.Format;
        int w = image.PixelWidth, h = image.PixelHeight;
        var stride = (w * format.BitsPerPixel + 7) / 8;
        var px = new byte[stride * h];
        image.CopyPixels(px, stride, 0);
        var copy = BitmapSource.Create(w, h, dpi, dpi, format, image.Palette, px, stride);
        copy.Freeze();
        return copy;
    }

    private static BitmapSource Freeze(BitmapSource image)
    {
        if (image.IsFrozen)
            return image;
        if (image.CanFreeze)
        {
            image.Freeze();
            return image;
        }
        return Materialize(image);
    }
}
