using System.Windows;
using System.Windows.Threading;
using SocScanner.App.Services;
using SocScanner.Localization;
using SocScanner.Scanning;
using SocScanner.Session;
using SocScanner.Settings;

namespace SocScanner.App;

/// <summary>
/// Arranque: gestor de excepciones → modo aislado → ajustes → instancia única → ventana, con la guía
/// la primera vez y las novedades al cambiar de versión.
/// </summary>
public partial class App : Application
{
    private bool _showingError;

    public static App? Instance { get; private set; }
    public static ScannerSettings Settings { get; internal set; } = new();
    public static MainWindow? MainView { get; internal set; }

    /// <summary>Las hojas y el trabajo de escanear: viven aquí para no perderse al rehacer la ventana (idioma).</summary>
    public static ScanWorkflow Workflow { get; } = new(() => Settings);

    /// <summary>El escáner simulado del modo aislado (uno solo, para que cuente las hojas entre ventanas).</summary>
    public static SimulatedScanner? Simulated { get; internal set; }

    /// <summary>La cámara simulada del modo aislado.</summary>
    public static SimulatedCamera? SimulatedCam { get; internal set; }

    /// <summary>En el modo aislado, los dispositivos simulados (los únicos de la lista); fuera de él, null.</summary>
    public static IReadOnlyCollection<IScanDevice>? SimulatedDevices =>
        Simulated is null ? null : SimulatedCam is null ? [Simulated] : [Simulated, SimulatedCam];

    /// <summary>Lo último guardado (se enseña también en la ventana rehecha).</summary>
    public static IReadOnlyList<string> LastSaved { get; set; } = [];

    /// <summary>Termina cuando la ventana anterior ha soltado su dispositivo (la cámara no se abre dos veces).</summary>
    public static Task DeviceReleased { get; set; } = Task.CompletedTask;

    /// <summary>
    /// El banco de pruebas crea la App para tener sus estilos, y WPF llama a OnStartup en cuanto
    /// arranca el Dispatcher aunque nadie llame a Run: con esto no arranca nada más (como en RC Manager).
    /// </summary>
    internal static bool SkipStartup { get; set; }

    /// <summary>Se está rehaciendo la ventana principal (cambio de idioma): cerrarla no es salir.</summary>
    public static bool Rebuilding { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (SkipStartup)
            return;
        Instance = this;
        GuardAgainstCrashes();
        Sandbox.Apply();
        Settings = ScannerSettings.Load();
        if (Settings.Language is { } lang)
            Loc.Use(lang);
        ThemeManager.Apply();
        if (Sandbox.IsOn)
            UseSimulatedDevices();
        else if (!SingleInstance.Claim())
        {
            Shutdown();
            return;
        }
        Loc.LanguageChanged += OnLanguageChanged;

        ShowMain();
        var args = e.Args.ToList();
        var size = args.FindIndex(a => a == "--size");
        if (size >= 0 && size + 1 < args.Count && args[size + 1].Split('x') is [var w, var h] && double.TryParse(w, out var pw) && double.TryParse(h, out var ph))
        {
            MainView!.Width = pw;
            MainView.Height = ph;
        }
        MainView!.Dispatcher.BeginInvoke(ShowFirstRunWindows, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>El escáner y la cámara de mentira del modo aislado (y del banco de pruebas).</summary>
    internal static void UseSimulatedDevices()
    {
        Simulated = new SimulatedScanner(Loc.Get("SimulatedScanner"));
        SimulatedCam = new SimulatedCamera(Loc.Get("SimulatedCamera"));
    }

    /// <summary>La guía sale sola la primera vez; las novedades al cambiar de versión (§6.7, §6.10).</summary>
    internal void ShowFirstRunWindows()
    {
        var current = WhatsNew.CurrentVersion();
        var (guide, news) = WhatsNew.OnStartup(Settings, current);
        Settings.GuideShown = true;
        Settings.LastSeenVersion = current;
        Settings.Save();
        if (MainView is null)
            return;
        if (guide)
            Dialogs.ShowModal(new GuideWindow(null) { Owner = MainView });
        else if (news)
            Dialogs.ShowModal(new WhatsNewWindow { Owner = MainView });
    }

    public void ShowMain()
    {
        if (MainView is null)
        {
            MainView = new MainWindow();
            MainView.Closed += (_, _) => { if (!Rebuilding) MainView = null; };
        }
        MainView.Show();
        if (MainView.WindowState == WindowState.Minimized)
            MainView.WindowState = WindowState.Normal;
        if (!Sandbox.IsOn)
            MainView.Activate();
    }

    /// <summary>Los textos del XAML se fijan al construir: la ventana se cierra y se abre ya traducida, en el mismo sitio.</summary>
    internal void OnLanguageChanged()
    {
        Settings.Language = Loc.Language;
        Settings.Save();
        if (MainView is not { IsVisible: true } old)
            return;
        var bounds = new Rect(old.Left, old.Top, old.Width, old.Height);
        var state = old.WindowState;
        Rebuilding = true;
        try
        {
            // Primero se cierra la vieja: suelta la cámara antes de que la nueva la pida.
            old.Close();
            MainView = null;
            ShowMain();
            MainView!.Left = bounds.Left;
            MainView.Top = bounds.Top;
            MainView.Width = bounds.Width;
            MainView.Height = bounds.Height;
            MainView.WindowState = state;
        }
        finally
        {
            Rebuilding = false;
        }
    }

    // ------------------------------------------------------------------ errores

    public void Alert(string title, string text)
    {
        if (_showingError)
            return;
        _showingError = true;
        try
        {
            PromptWindow.Alert(MainView is { IsVisible: true } w ? w : null, title, text);
        }
        catch (Exception ex)
        {
            AppLog.Write($"no se pudo enseñar el aviso: {ex}");
        }
        finally
        {
            _showingError = false;
        }
    }

    /// <summary>
    /// Gestor global de excepciones (General §6.12): un error inesperado se apunta con su traza y se
    /// avisa en el idioma del usuario, y la aplicación sigue abierta.
    /// </summary>
    private void GuardAgainstCrashes()
    {
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Write($"error no controlado: {ex.Exception}");
            ex.Handled = true;
            Alert(Loc.Get("UnexpectedErrorTitle"), Loc.Format("UnexpectedErrorText", AppLog.FilePath));
        };
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            AppLog.Write($"error no controlado en tarea: {ex.Exception}");
            ex.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Write($"error fatal: {ex.ExceptionObject}");
    }
}
