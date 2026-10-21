using System.Globalization;

namespace SocScanner.Output;

/// <summary>
/// Nombres de los ficheros. En serie: prefijo + número correlativo con ceros + sufijo
/// («Factura_0007.pdf»); nunca se pisa un fichero: si el número ya está cogido se salta al siguiente
/// libre. Una a una: «Escaneo 2026-10-21 10.30.05» y, si son varias hojas en un formato de una sola,
/// « (1)», « (2)»…
/// </summary>
public static class FileNaming
{
    public const int MaxDigits = 9;

    /// <summary>El nombre (sin carpeta) de un número de la serie.</summary>
    public static string SeriesName(string? prefix, int number, int digits, string? suffix, OutputFormat format)
    {
        digits = Math.Clamp(digits, 1, MaxDigits);
        var core = Math.Max(0, number).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        var name = Sanitize(prefix ?? string.Empty, allowEmpty: true) + core + Sanitize(suffix ?? string.Empty, allowEmpty: true);
        return name + OutputFormats.Extension(format);
    }

    /// <summary>
    /// El primer número desde <paramref name="start"/> cuyo fichero no existe aún (también con las
    /// variantes « (1)», « (2)» de varias hojas, para no mezclar series).
    /// </summary>
    public static int NextFree(string folder, string? prefix, int start, int digits, string? suffix, OutputFormat format, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var n = Math.Max(0, start);
        for (var guard = 0; guard < 1_000_000; guard++, n++)
        {
            var name = SeriesName(prefix, n, digits, suffix, format);
            var path = Path.Combine(folder, name);
            var stem = Path.Combine(folder, Path.GetFileNameWithoutExtension(name));
            if (!exists(path) && !exists($"{stem} (1){OutputFormats.Extension(format)}"))
                return n;
        }
        return n;
    }

    /// <summary>«Escaneo 2026-10-21 10.30.05» con la palabra en el idioma de la aplicación.</summary>
    public static string TimestampName(string word, DateTime when) =>
        Sanitize(word, allowEmpty: false) + " " + when.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Las rutas de los ficheros de un guardado: una si el formato admite varias hojas o si solo hay
    /// una; si no, una por hoja con « (1)», « (2)»…
    /// </summary>
    public static IReadOnlyList<string> PagePaths(string path, int pages, OutputFormat format)
    {
        if (pages <= 1 || OutputFormats.IsMultiPage(format))
            return [path];
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return [.. Enumerable.Range(1, pages).Select(i => Path.Combine(folder, $"{stem} ({i}){ext}"))];
    }

    /// <summary>Si el fichero ya existe, « (2)», « (3)»… hasta uno libre.</summary>
    public static string Unique(string path, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(path))
            return path;
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({n}){ext}");
            if (!exists(candidate))
                return candidate;
        }
    }

    /// <summary>Quita lo que Windows no admite en un nombre de fichero (prefijo y sufijo pueden quedar vacíos).</summary>
    public static string Sanitize(string text, bool allowEmpty)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(text.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        clean = allowEmpty ? clean : clean.Trim().TrimEnd('.');
        return clean.Length == 0 && !allowEmpty ? "Scan" : clean;
    }

    /// <summary>¿Valen el prefijo y el sufijo tal cual? Devuelve la clave de <c>Loc</c> del problema, o null.</summary>
    public static string? CheckAffix(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "ErrAffixChars";
        if (text.Length > 80)
            return "ErrAffixLong";
        return null;
    }
}
