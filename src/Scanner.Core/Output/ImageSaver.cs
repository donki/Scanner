using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Output;

/// <summary>
/// Guarda hojas en el formato elegido. PDF y TIFF meten todas en un fichero; JPG, PNG y BMP, una por
/// fichero (« (1)», « (2)»…). Se escribe a un temporal y se renombra al final: un fallo a medias no
/// deja un fichero roto con el nombre bueno. Los fallos de disco salen como <see cref="SaveException"/>
/// con su explicación.
/// </summary>
public static class ImageSaver
{
    public static IReadOnlyList<string> Save(IReadOnlyList<ScannedPage> pages, OutputFormat format, string path, int jpegQuality)
    {
        if (pages.Count == 0)
            throw new ArgumentException("No pages", nameof(pages));
        var paths = FileNaming.PagePaths(path, pages.Count, format);
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            if (paths.Count == 1)
                WriteAtomically(paths[0], s => Write(s, pages, format, jpegQuality));
            else
                for (var i = 0; i < pages.Count; i++)
                {
                    var one = new[] { pages[i] };
                    WriteAtomically(paths[i], s => Write(s, one, format, jpegQuality));
                }
            return paths;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            AppLog.Write($"no se pudo guardar {path}: {ex}");
            var key = ex switch
            {
                // Windows da «acceso denegado» también cuando otro programa tiene abierto el fichero.
                UnauthorizedAccessException when paths.Any(IsInUse) => "ErrSaveInUse",
                IOException when paths.Any(IsInUse) => "ErrSaveInUse",
                UnauthorizedAccessException or System.Security.SecurityException => "ErrSaveDenied",
                IOException io when IsDiskFull(io) => "ErrSaveDiskFull",
                _ => "ErrSaveFailed",
            };
            throw new SaveException(key, ex);
        }
    }

    public static void Write(Stream stream, IReadOnlyList<ScannedPage> pages, OutputFormat format, int jpegQuality)
    {
        if (format == OutputFormat.Pdf)
        {
            PdfWriter.Write(stream, pages, jpegQuality);
            return;
        }
        BitmapEncoder encoder = format switch
        {
            OutputFormat.Jpeg => new JpegBitmapEncoder { QualityLevel = Math.Clamp(jpegQuality, 1, 100) },
            OutputFormat.Png => new PngBitmapEncoder(),
            // Todo en blanco y negro: CCITT G4, la compresión de los faxes (la compresión es de todo el fichero).
            OutputFormat.Tiff => new TiffBitmapEncoder
            {
                Compression = pages.All(p => p.Image.Format == PixelFormats.BlackWhite) ? TiffCompressOption.Ccitt4 : TiffCompressOption.Lzw,
            },
            _ => new BmpBitmapEncoder(),
        };
        foreach (var page in pages)
        {
            var image = Imaging.ImageOps.WithDpi(Imaging.ImageOps.Detached(page.Image), page.Dpi);
            // JPEG no admite 1 bit: el blanco y negro va en gris (el PNG y el TIFF lo guardan en 1 bit).
            if (format == OutputFormat.Jpeg && image.Format == PixelFormats.BlackWhite)
                image = Imaging.ImageOps.ToGray(image);
            encoder.Frames.Add(BitmapFrame.Create(image));
        }
        encoder.Save(stream);
    }

    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var tmp = path + ".part";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                write(fs);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }

    /// <summary>¿Existe, no es de solo lectura y otro programa lo tiene abierto?</summary>
    private static bool IsInUse(string path)
    {
        try
        {
            if (!File.Exists(path) || File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                return false;
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 0x70 or 0x27;   // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL
}

/// <summary>No se pudo guardar; <see cref="MessageKey"/> es la clave de <c>Loc</c> de la explicación.</summary>
public sealed class SaveException(string messageKey, Exception inner) : Exception(inner.Message, inner)
{
    public string MessageKey { get; } = messageKey;
}
