using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;

namespace SocScanner.App.Services;

/// <summary>Lo que se le pide al diálogo de guardar de Windows.</summary>
public sealed record SaveFileRequest(string Title, string Filter, int FilterIndex, string FileName, string? InitialDirectory);

/// <summary>
/// La puerta por la que la aplicación sale de sí misma: ventanas modales, los diálogos de Windows
/// (guardar, elegir carpeta), abrir ficheros y carpetas y el portapapeles. En la aplicación hacen lo
/// de siempre; el banco de pruebas las cambia por dobles que contestan sin enseñar nada ni abrir
/// nada (como en sOC RC Manager).
/// </summary>
public static class Dialogs
{
    public static Func<Window, bool?> ShowModal { get; set; } = w => w.ShowDialog();

    /// <summary>Devuelve la ruta elegida y el filtro (1 = el primero), o null si se cancela.</summary>
    public static Func<Window, SaveFileRequest, (string Path, int FilterIndex)?> PickSaveFile { get; set; } = (owner, r) =>
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = r.Title,
            Filter = r.Filter,
            FilterIndex = r.FilterIndex,
            FileName = r.FileName,
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (r.InitialDirectory is { } dir)
            dialog.InitialDirectory = dir;
        return dialog.ShowDialog(owner) == true ? (dialog.FileName, dialog.FilterIndex) : null;
    };

    /// <summary>Devuelve la carpeta elegida, o null si se cancela.</summary>
    public static Func<Window, string, string?, string?> PickFolder { get; set; } = (owner, title, initial) =>
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (initial is { } dir && Directory.Exists(dir))
            dialog.InitialDirectory = dir;
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    };

    /// <summary>Abre un fichero, una carpeta, una dirección o una pantalla de Configuración con Windows.</summary>
    public static Action<ProcessStartInfo> Start { get; set; } = psi => Process.Start(psi)?.Dispose();

    public static Action<StringCollection> CopyFiles { get; set; } = Clipboard.SetFileDropList;
}
