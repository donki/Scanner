using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SocScanner.Output;
using SocScanner.Scanning;

namespace SocScanner.Settings;

/// <summary>Cómo se trabaja: hoja a hoja con vista previa, o en serie guardando cada hoja al momento.</summary>
public enum ScanMode
{
    /// <summary>Se escanea, se mira y se guarda (con una o varias hojas en el mismo fichero).</summary>
    Single,

    /// <summary>Cada hoja se guarda sola al escanearla, con nombre correlativo, en la carpeta y el formato fijados.</summary>
    Batch,
}

/// <summary>
/// Los ajustes, en <c>%AppData%\sOCScanner\settings.json</c>. Se guardan al cambiar cualquier cosa.
/// Un fichero ilegible no impide arrancar: se apunta en el registro y se usan los de por defecto; un
/// valor fuera de rango se corrige al cargar.
/// </summary>
public sealed class ScannerSettings
{
    public const int CurrentSchema = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [JsonIgnore]
    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "sOCScanner", "settings.json");

    /// <summary>Documentos del usuario + «Escaneos» («Scans» con Windows en otro idioma); se fija al crear los ajustes.</summary>
    [JsonIgnore]
    public static string DefaultFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "Escaneos" : "Scans");

    public int Schema { get; set; } = CurrentSchema;
    public string? Language { get; set; }
    public string? LastSeenVersion { get; set; }
    public bool GuideShown { get; set; }

    /// <summary>El dispositivo elegido (identificador de Windows); null = el primero que haya.</summary>
    public string? DeviceId { get; set; }
    public ScanSource Source { get; set; } = ScanSource.Flatbed;
    public QualityPreset Preset { get; set; } = QualityPreset.Document;
    public ScanQuality CustomQuality { get; set; } = ScanQuality.ForPreset(QualityPreset.Document);

    /// <summary>Resolución elegida para la cámara (0 = la mayor).</summary>
    public int CameraWidth { get; set; }
    public int CameraHeight { get; set; }

    public ScanMode Mode { get; set; } = ScanMode.Single;

    /// <summary>Una a una: carpeta y formato que propone el diálogo de guardar.</summary>
    public string SaveFolder { get; set; } = DefaultFolder;
    public OutputFormat SingleFormat { get; set; } = OutputFormat.Pdf;

    /// <summary>En serie: dónde, en qué formato y con qué nombre se guarda cada hoja.</summary>
    public string BatchFolder { get; set; } = DefaultFolder;
    public OutputFormat BatchFormat { get; set; } = OutputFormat.Pdf;
    public string BatchPrefix { get; set; } = string.Empty;
    public string BatchSuffix { get; set; } = string.Empty;
    public int BatchNextNumber { get; set; } = 1;
    public int BatchDigits { get; set; } = 4;

    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }

    public static ScannerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<ScannerSettings>(File.ReadAllText(FilePath), Json);
                if (s is not null)
                    return s.Normalize();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"ajustes ilegibles, se usan los de por defecto: {ex.Message}");
        }
        return new ScannerSettings().Normalize();
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudieron guardar los ajustes: {ex.Message}");
            return false;
        }
    }

    /// <summary>La calidad que se usará: la del atajo o la personalizada.</summary>
    public ScanQuality EffectiveQuality() =>
        Preset == QualityPreset.Custom ? CustomQuality.Clone().Normalize() : ScanQuality.ForPreset(Preset);

    [JsonIgnore]
    public (int Width, int Height)? CameraResolution => CameraWidth > 0 && CameraHeight > 0 ? (CameraWidth, CameraHeight) : null;

    /// <summary>Corrige lo que no valga.</summary>
    public ScannerSettings Normalize()
    {
        Schema = CurrentSchema;
        if (Language is not ("es" or "en"))
            Language = null;
        if (string.IsNullOrWhiteSpace(DeviceId)) DeviceId = null;
        if (!Enum.IsDefined(Source)) Source = ScanSource.Flatbed;
        if (!Enum.IsDefined(Preset)) Preset = QualityPreset.Document;
        CustomQuality = (CustomQuality ?? new ScanQuality()).Normalize();
        if (!Enum.IsDefined(Mode)) Mode = ScanMode.Single;
        if (string.IsNullOrWhiteSpace(SaveFolder)) SaveFolder = DefaultFolder;
        if (string.IsNullOrWhiteSpace(BatchFolder)) BatchFolder = DefaultFolder;
        if (!Enum.IsDefined(SingleFormat)) SingleFormat = OutputFormat.Pdf;
        if (!Enum.IsDefined(BatchFormat)) BatchFormat = OutputFormat.Pdf;
        BatchPrefix ??= string.Empty;
        BatchSuffix ??= string.Empty;
        if (FileNaming.CheckAffix(BatchPrefix) is not null) BatchPrefix = FileNaming.Sanitize(BatchPrefix, allowEmpty: true);
        if (FileNaming.CheckAffix(BatchSuffix) is not null) BatchSuffix = FileNaming.Sanitize(BatchSuffix, allowEmpty: true);
        if (BatchPrefix.Length > 80) BatchPrefix = BatchPrefix[..80];
        if (BatchSuffix.Length > 80) BatchSuffix = BatchSuffix[..80];
        BatchNextNumber = Math.Clamp(BatchNextNumber, 0, 999_999_999);
        BatchDigits = Math.Clamp(BatchDigits, 1, FileNaming.MaxDigits);
        if (CameraWidth < 0 || CameraHeight < 0 || CameraWidth == 0 != (CameraHeight == 0)) { CameraWidth = 0; CameraHeight = 0; }
        if (double.IsNaN(WindowWidth) || WindowWidth < 0) WindowWidth = 0;
        if (double.IsNaN(WindowHeight) || WindowHeight < 0) WindowHeight = 0;
        return this;
    }

    /// <summary>
    /// ¿Se puede guardar en esta carpeta? Se crea si no existe y se prueba a escribir. Devuelve null
    /// si vale, o la clave de <c>Loc</c> del motivo.
    /// </summary>
    public static string? CheckFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return "ErrFolderEmpty";
        try
        {
            if (!Path.IsPathFullyQualified(folder))
                return "ErrFolderNotAbsolute";
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".socscan-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write($"carpeta no válida ({folder}): {ex.Message}");
            return "ErrFolderNotWritable";
        }
    }
}
