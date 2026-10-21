using SocScanner.Output;
using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Session;

/// <summary>Lo que dejó un escaneo: las hojas y, en serie, los ficheros guardados.</summary>
public sealed record ScanOutcome(IReadOnlyList<ScannedPage> Pages, IReadOnlyList<string> Saved, ScanRequest Request);

/// <summary>
/// El trabajo de la ventana sin la ventana: pedir las hojas al dispositivo con la calidad ajustada a
/// lo que admite, reintentar una vez si estaba calentando u ocupado, y, según el modo, añadirlas al
/// documento («una a una») o guardarlas al momento con su número («en serie»). Guardar y codificar
/// van fuera del hilo de la interfaz.
/// </summary>
public sealed class ScanWorkflow(Func<ScannerSettings> settings)
{
    /// <summary>Lo que se espera antes de reintentar un escáner que está calentando u ocupado.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(4);

    public ScanDocument Document { get; } = new();

    public bool IsBusy { get; private set; }

    public async Task<ScanOutcome> ScanAsync(IScanDevice device, IProgress<int>? progress, CancellationToken ct)
    {
        if (IsBusy)
            throw new InvalidOperationException("Already scanning");
        IsBusy = true;
        try
        {
            var s = settings();
            if (s.Mode == ScanMode.Batch && ScannerSettings.CheckFolder(s.BatchFolder) is { } folderProblem)
                throw new ScanException(folderProblem, $"carpeta de la serie: {s.BatchFolder}");
            var caps = await device.GetCapabilitiesAsync(ct);
            var request = ScanPlanner.Plan(s.EffectiveQuality(), caps, s.Source, device.Info.Kind, s.CameraResolution);
            IReadOnlyList<ScannedPage> pages;
            try
            {
                pages = await device.ScanAsync(request, progress, ct);
            }
            catch (Exception ex) when (ScanErrors.IsTransient(ex) && !ct.IsCancellationRequested)
            {
                AppLog.Write($"escáner ocupado o calentando, se reintenta: {ex.Message}");
                await Task.Delay(RetryDelay, ct);
                pages = await device.ScanAsync(request, progress, ct);
            }
            if (pages.Count == 0)
                throw new ScanException("ErrNoPages", "el dispositivo no devolvió ninguna hoja");

            if (s.Mode == ScanMode.Single)
            {
                Document.Add(pages);
                return new ScanOutcome(pages, [], request);
            }
            var saved = await Task.Run(() => SaveSeries(s, pages, s.EffectiveQuality().JpegQuality), ct);
            return new ScanOutcome(pages, saved, request);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Guarda cada hoja con el siguiente número libre de la serie y avanza el contador (que queda
    /// guardado en los ajustes, para seguir mañana donde se dejó).
    /// </summary>
    public static IReadOnlyList<string> SaveSeries(ScannerSettings s, IReadOnlyList<ScannedPage> pages, int jpegQuality)
    {
        var saved = new List<string>();
        foreach (var page in pages)
        {
            var number = FileNaming.NextFree(s.BatchFolder, s.BatchPrefix, s.BatchNextNumber, s.BatchDigits, s.BatchSuffix, s.BatchFormat);
            var path = Path.Combine(s.BatchFolder, FileNaming.SeriesName(s.BatchPrefix, number, s.BatchDigits, s.BatchSuffix, s.BatchFormat));
            saved.AddRange(ImageSaver.Save([page], s.BatchFormat, path, jpegQuality));
            s.BatchNextNumber = number + 1;
            s.Save();
        }
        return saved;
    }

    /// <summary>El nombre que llevará la próxima hoja de la serie (para enseñarlo antes de escanear).</summary>
    public static string NextSeriesName(ScannerSettings s)
    {
        var number = Directory.Exists(s.BatchFolder)
            ? FileNaming.NextFree(s.BatchFolder, s.BatchPrefix, s.BatchNextNumber, s.BatchDigits, s.BatchSuffix, s.BatchFormat)
            : s.BatchNextNumber;
        return FileNaming.SeriesName(s.BatchPrefix, number, s.BatchDigits, s.BatchSuffix, s.BatchFormat);
    }

    /// <summary>Guarda las hojas del documento y, si sale bien, lo vacía.</summary>
    public async Task<IReadOnlyList<string>> SaveDocumentAsync(string path, OutputFormat format, int jpegQuality)
    {
        if (Document.IsEmpty)
            return [];
        var pages = Document.Pages.ToList();
        var saved = await Task.Run(() => ImageSaver.Save(pages, format, path, jpegQuality));
        Document.Clear();
        return saved;
    }
}
