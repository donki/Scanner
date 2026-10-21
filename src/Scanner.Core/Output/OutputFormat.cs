namespace SocScanner.Output;

/// <summary>Formatos en que se guarda lo escaneado.</summary>
public enum OutputFormat
{
    /// <summary>PDF: todas las hojas en un fichero.</summary>
    Pdf,
    Jpeg,
    Png,

    /// <summary>TIFF: también admite varias hojas en un fichero.</summary>
    Tiff,
    Bmp,
}

public static class OutputFormats
{
    public static IReadOnlyList<OutputFormat> All { get; } = Enum.GetValues<OutputFormat>();

    public static string Extension(OutputFormat f) => f switch
    {
        OutputFormat.Pdf => ".pdf",
        OutputFormat.Jpeg => ".jpg",
        OutputFormat.Png => ".png",
        OutputFormat.Tiff => ".tif",
        OutputFormat.Bmp => ".bmp",
        _ => ".pdf",
    };

    /// <summary>Nombre corto para las listas («PDF», «JPG»…), igual en los dos idiomas.</summary>
    public static string Label(OutputFormat f) => f switch
    {
        OutputFormat.Jpeg => "JPG",
        OutputFormat.Tiff => "TIFF",
        _ => f.ToString().ToUpperInvariant(),
    };

    /// <summary>¿Caben varias hojas en un solo fichero?</summary>
    public static bool IsMultiPage(OutputFormat f) => f is OutputFormat.Pdf or OutputFormat.Tiff;

    /// <summary>El formato de una extensión (con o sin punto; «.jpeg» y «.tiff» también), o null.</summary>
    public static OutputFormat? FromExtension(string? extension) =>
        extension?.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "pdf" => OutputFormat.Pdf,
            "jpg" or "jpeg" => OutputFormat.Jpeg,
            "png" => OutputFormat.Png,
            "tif" or "tiff" => OutputFormat.Tiff,
            "bmp" => OutputFormat.Bmp,
            _ => null,
        };

    /// <summary>El filtro del diálogo de guardar: «PDF (*.pdf)|*.pdf|JPG (*.jpg)|*.jpg…», en el orden de <see cref="All"/>.</summary>
    public static string DialogFilter() =>
        string.Join("|", All.Select(f => $"{Label(f)} (*{Extension(f)})|*{Extension(f)}"));
}
