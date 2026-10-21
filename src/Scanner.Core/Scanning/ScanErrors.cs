using System.Runtime.InteropServices;

namespace SocScanner.Scanning;

/// <summary>
/// Traduce los fallos de los escáneres y las cámaras a una explicación para el usuario (clave de
/// <c>Loc</c>, General §6.9). Los códigos son los de WIA, que es lo que hay debajo de la API de
/// escáneres de Windows.
/// </summary>
public static class ScanErrors
{
    public const int WiaGeneral = unchecked((int)0x80210001);
    public const int WiaPaperJam = unchecked((int)0x80210002);
    public const int WiaPaperEmpty = unchecked((int)0x80210003);
    public const int WiaPaperProblem = unchecked((int)0x80210004);
    public const int WiaOffline = unchecked((int)0x80210005);
    public const int WiaBusy = unchecked((int)0x80210006);
    public const int WiaWarmingUp = unchecked((int)0x80210007);
    public const int WiaUserIntervention = unchecked((int)0x80210008);
    public const int WiaItemDeleted = unchecked((int)0x80210009);
    public const int WiaDeviceCommunication = unchecked((int)0x8021000A);
    public const int WiaInvalidCommand = unchecked((int)0x8021000B);
    public const int WiaIncorrectSetting = unchecked((int)0x8021000C);
    public const int WiaDeviceLocked = unchecked((int)0x8021000D);
    public const int WiaCoverOpen = unchecked((int)0x80210016);
    public const int WiaLampOff = unchecked((int)0x80210017);
    public const int DeviceNotConnected = unchecked((int)0x8007048F);
    public const int DeviceNotFound = unchecked((int)0x80070490);
    public const int AccessDenied = unchecked((int)0x80070005);
    public const int CameraInUse = unchecked((int)0xC00D3704);
    public const int CameraNoFrames = unchecked((int)0xC00DABE4);

    /// <summary>La clave del texto que explica el fallo y qué hacer.</summary>
    public static string KeyFor(Exception ex)
    {
        if (ex is ScanException se)
            return se.MessageKey;
        if (ex is UnauthorizedAccessException)
            return "ErrCameraDenied";
        if (ex is OperationCanceledException)
            return "ErrCancelled";
        return KeyFor(ex.HResult) ?? (ex.InnerException is { } inner ? KeyFor(inner) : "ErrScanUnknown");
    }

    public static string? KeyFor(int hresult) => hresult switch
    {
        WiaPaperJam => "ErrPaperJam",
        WiaPaperEmpty => "ErrPaperEmpty",
        WiaPaperProblem => "ErrPaperProblem",
        WiaOffline or DeviceNotConnected or WiaDeviceCommunication or WiaItemDeleted or DeviceNotFound => "ErrDeviceOffline",
        WiaBusy or WiaDeviceLocked => "ErrDeviceBusy",
        WiaWarmingUp or WiaLampOff => "ErrWarmingUp",
        WiaCoverOpen => "ErrCoverOpen",
        WiaUserIntervention => "ErrUserIntervention",
        WiaIncorrectSetting or WiaInvalidCommand => "ErrBadSetting",
        AccessDenied => "ErrCameraDenied",
        CameraInUse => "ErrCameraInUse",
        CameraNoFrames => "ErrCameraNoFrames",
        _ => null,
    };

    /// <summary>¿Es un fallo de los que se pasan reintentando en unos segundos (calentando, ocupado)?</summary>
    public static bool IsTransient(Exception ex) => ex is not ScanException && (ex.HResult is WiaWarmingUp or WiaBusy || ex is COMException { HResult: WiaWarmingUp or WiaBusy });
}
