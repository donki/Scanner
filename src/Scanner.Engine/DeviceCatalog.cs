using SocScanner.Scanning;
using SocScanner.Settings;

namespace SocScanner.Engine;

/// <summary>
/// La lista de dispositivos: primero los escáneres y luego las cámaras. En el modo aislado (pruebas)
/// solo el escáner simulado, para no tocar nada real (General §8.4).
/// </summary>
public static class DeviceCatalog
{
    public static async Task<IReadOnlyList<ScanDeviceInfo>> ListAsync(bool sandbox, string simulatedName)
    {
        if (sandbox)
            return [new ScanDeviceInfo(SimulatedScanner.DeviceId, simulatedName, DeviceKind.Simulated)];
        var list = new List<ScanDeviceInfo>();
        try { list.AddRange(await WinRtScanner.ListAsync()); }
        catch (Exception ex) { AppLog.Write($"no se pudieron listar los escáneres: {ex}"); }
        try { list.AddRange(await CameraDevice.ListAsync()); }
        catch (Exception ex) { AppLog.Write($"no se pudieron listar las cámaras: {ex}"); }
        return list;
    }

    public static IScanDevice Open(ScanDeviceInfo info, SimulatedScanner? simulated = null) => info.Kind switch
    {
        DeviceKind.Camera => new CameraDevice(info),
        DeviceKind.Simulated => simulated ?? new SimulatedScanner(info.Name),
        _ => new WinRtScanner(info),
    };
}
