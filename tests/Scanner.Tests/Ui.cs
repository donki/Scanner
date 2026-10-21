using System.Collections.Specialized;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SocScanner.App;
using SocScanner.App.Services;
using SocScanner.Settings;

namespace SocScanner.Tests;

/// <summary>
/// El hilo de interfaz de las pruebas (como en sOC RC Manager): un hilo STA con la <see cref="App"/>
/// de la aplicación (sus estilos y colores) en modo aislado. Las ventanas se crean y se manejan aquí
/// con <see cref="Run"/>; las modales se abren de verdad, fuera de la pantalla y sin activarse, y las
/// contesta la prueba (<see cref="Answer"/>). Los diálogos de Windows, abrir ficheros y el
/// portapapeles van contra dobles que apuntan lo que se pidió.
/// </summary>
public static class Ui
{
    private static readonly Lazy<Dispatcher> _dispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Queue<Func<Window, bool>> _answers = new();

    public const double Offscreen = -32000;

    public static Dispatcher Dispatcher => _dispatcher.Value;

    /// <summary>Carpeta del modo aislado de toda la tanda.</summary>
    public static string SandboxFolder { get; } = Path.Combine(Path.GetTempPath(), "sOCScanner-tests-ui", Guid.NewGuid().ToString("N"));

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                Environment.SetEnvironmentVariable(Sandbox.Variable, SandboxFolder);
                App.App.SkipStartup = true;
                var app = new App.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                ThemeManager.Apply();
                Sandbox.Apply();
                dispatcher = Dispatcher.CurrentDispatcher;
                dispatcher.UnhandledException += (_, e) => { LastError = e.Exception; e.Handled = true; };
            }
            catch (Exception ex)
            {
                error = ex;
            }
            ready.Set();
            if (error is null)
                Dispatcher.Run();
        })
        { IsBackground = true, Name = "Ui de las pruebas" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (error is not null)
            throw new InvalidOperationException("No se pudo arrancar el hilo de interfaz de las pruebas", error);
        InstallHooks();
        return dispatcher!;
    }

    private static void InstallHooks()
    {
        Dialogs.ShowModal = w =>
        {
            Hide(w);
            Modals.Add(w);
            w.Dispatcher.BeginInvoke(() => Respond(w), DispatcherPriority.ApplicationIdle);
            return w.ShowDialog();
        };
        Dialogs.PickSaveFile = (_, r) => { SaveAsked = r; return SaveAs; };
        Dialogs.PickFolder = (_, _, _) => PickedFolder;
        Dialogs.Start = psi => Started.Add(psi);
        Dialogs.CopyFiles = list =>
        {
            if (ClipboardBusy)
                throw new System.Runtime.InteropServices.COMException("ocupado", unchecked((int)0x800401D0));
            Copied = list;
        };
    }

    /// <summary>Pone todo como al principio para la prueba siguiente (ajustes nuevos, sin hojas, dobles vacíos).</summary>
    public static void Reset(ScannerSettings? settings = null)
    {
        Run(() =>
        {
            foreach (Window w in Application.Current.Windows.Cast<Window>().ToList())
                w.Close();
            ScannerSettings.FilePath = Path.Combine(SandboxFolder, "settings.json");
            ScannerSettings.DefaultFolder = Path.Combine(SandboxFolder, "scans");
            AppLog.Folder = SandboxFolder;
            App.App.Workflow.Document.Clear();
            App.App.LastSaved = [];
            App.App.UseSimulatedDevices();
            App.App.Simulated!.Delay = TimeSpan.Zero;
            App.App.Settings = settings ?? new ScannerSettings { Language = "es", GuideShown = true };
            Localization.Loc.Use(App.App.Settings.Language ?? "es");
        });
        _answers.Clear();
        Modals.Clear();
        Started.Clear();
        Copied = null;
        ClipboardBusy = false;
        SaveAs = null;
        SaveAsked = null;
        PickedFolder = null;
        LastError = null;
    }

    public static Exception? LastError { get; set; }
    public static List<Window> Modals { get; } = [];
    public static List<ProcessStartInfo> Started { get; } = [];
    public static StringCollection? Copied { get; set; }
    public static bool ClipboardBusy { get; set; }
    public static (string Path, int FilterIndex)? SaveAs { get; set; }
    public static SaveFileRequest? SaveAsked { get; set; }
    public static string? PickedFolder { get; set; }

    public static void Hide(Window w)
    {
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = Offscreen;
        w.Top = Offscreen;
        w.ShowInTaskbar = false;
        w.ShowActivated = false;
    }

    /// <summary>Encola la respuesta a la próxima modal: devuelve true si ya la ha cerrado (si no, se cancela).</summary>
    public static void Answer(Func<Window, bool> answer) => _answers.Enqueue(answer);

    public static void Answer<T>(Action<T> answer) where T : Window =>
        Answer(w =>
        {
            Assert.IsType<T>(w);
            answer((T)w);
            return !w.IsVisible;
        });

    /// <summary>Contesta con el botón de aceptar (o el de quitar) de un aviso o una confirmación.</summary>
    public static void AnswerOk() => Answer(w => { Click(Find<Button>(w, "OkButton")); return true; });

    public static void AnswerCancel() => Answer(w => { Click(Find<Button>(w, "CancelButton")); return true; });

    public static int PendingAnswers => _answers.Count;

    private static void Respond(Window w)
    {
        if (!w.IsVisible)
            return;
        var closed = false;
        if (_answers.Count > 0)
        {
            try { closed = _answers.Dequeue()(w); }
            catch (Exception ex) { LastError = ex; }
        }
        if (!closed && w.IsVisible)
            w.Close();
    }

    public static void Run(Action action) => Dispatcher.Invoke(action);

    public static T Run<T>(Func<T> func) => Dispatcher.Invoke(func);

    /// <summary>Espera (sin bloquear el hilo de interfaz) a que se cumpla algo que se mira en él.</summary>
    public static void WaitUntil(Func<bool> condition, string what, int seconds = 10) => WaitUntil(condition, () => what, seconds);

    public static void WaitUntil(Func<bool> condition, Func<string> what, int seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (Dispatcher.Invoke(condition, DispatcherPriority.Background))
                return;
            Thread.Sleep(15);
        }
        throw new TimeoutException($"No llega: {what()}" + (LastError is { } e ? $" (error en la interfaz: {e})" : ""));
    }

    /// <summary>Deja que el hilo de interfaz termine lo que tenga pendiente.</summary>
    public static void Settle() => Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    public static T Show<T>(T w) where T : Window
    {
        Hide(w);
        w.Show();
        return w;
    }

    private static readonly System.Reflection.MethodInfo OnClick =
        typeof(ButtonBase).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    /// <summary>
    /// Pulsa como lo haría el ratón (OnClick): lanza Click y, en un botón IsCancel, cierra el diálogo
    /// (solo con el evento Click no se cerraría). Un botón desactivado no se puede pulsar.
    /// </summary>
    public static void Click(ButtonBase button)
    {
        if (!button.IsEnabled)
            throw new InvalidOperationException($"{button.Name} está desactivado");
        OnClick.Invoke(button, null);
    }

    /// <summary>Un elemento por su AutomationId o su x:Name, en el árbol lógico.</summary>
    public static T Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        TryFind<T>(root, id) ?? throw new InvalidOperationException($"No está {id}");

    public static T? TryFind<T>(DependencyObject root, string id) where T : DependencyObject
    {
        if (root is T match && (AutomationProperties.GetAutomationId(root) == id || (root as FrameworkElement)?.Name == id))
            return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (TryFind<T>(child, id) is { } found)
                return found;
        return null;
    }

    /// <summary>Todo el texto de los TextBlock de una ventana (para comprobar lo que dice).</summary>
    public static string AllText(DependencyObject root) => Run(() => AllTextCore(root));

    private static string AllTextCore(DependencyObject root)
    {
        var parts = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock t && !string.IsNullOrEmpty(t.Text))
                parts.Add(t.Text);
            foreach (var c in LogicalTreeHelper.GetChildren(d).OfType<DependencyObject>())
                Walk(c);
        }
        Walk(root);
        return string.Join(" | ", parts);
    }

    public static Brush? Brush(string key) => Application.Current.TryFindResource(key) as Brush;
}
