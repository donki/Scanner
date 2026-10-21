using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Engine;

/// <summary>
/// La lista de dispositivos: primero los escáneres y luego las cámaras. En el modo aislado (pruebas)
/// solo el escáner y la cámara simulados, para no tocar nada real (General §8.4).
/// </summary>
public static class DeviceCatalog
{
    public static async Task<IReadOnlyList<ScanDeviceInfo>> ListAsync(IReadOnlyCollection<IScanDevice>? simulated)
    {
        if (simulated is not null)
            return [.. simulated.Select(d => d.Info)];
        var list = new List<ScanDeviceInfo>();
        try { list.AddRange(await WinRtScanner.ListAsync()); }
        catch (Exception ex) { AppLog.Write($"no se pudieron listar los escáneres: {ex}"); }
        try { list.AddRange(await CameraDevice.ListAsync()); }
        catch (Exception ex) { AppLog.Write($"no se pudieron listar las cámaras: {ex}"); }
        return list;
    }

    /// <summary>Abre el dispositivo de la lista; los simulados son siempre los mismos (no se crean otros).</summary>
    public static IScanDevice Open(ScanDeviceInfo info, IReadOnlyCollection<IScanDevice>? simulated = null) =>
        simulated?.FirstOrDefault(d => d.Info.Id == info.Id) ?? info.Kind switch
        {
            DeviceKind.Camera => new CameraDevice(info),
            DeviceKind.Simulated => new SimulatedScanner(info.Name),
            _ => new WinRtScanner(info),
        };

    /// <summary>¿Es uno de los simulados (que no se sueltan al cambiar de dispositivo)?</summary>
    public static bool IsSimulated(IScanDevice device, IReadOnlyCollection<IScanDevice>? simulated) =>
        simulated?.Contains(device) == true;
}
