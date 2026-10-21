using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SocScanner.App.Services;
using SocScanner.Engine;
using SocScanner.Imaging;
using SocScanner.Localization;
using SocScanner.Output;
using SocScanner.Scanning;
using SocScanner.Session;
using SocScanner.Settings;

namespace SocScanner.App;

/// <summary>
/// La ventana: elegir dispositivo, calidad y modo; escanear con un botón (o Intro); ver la hoja, y en
/// «una a una» ordenar y guardar las hojas, en «en serie» ver lo que se acaba de guardar. Solo
/// coordina: escanear y guardar están en <see cref="ScanWorkflow"/> y el hardware en Scanner.Engine.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Anchos de las miniaturas y de la vista previa (la hoja entera no hace falta para verla).</summary>
    private const int ThumbWidth = 90;
    private const int PreviewWidth = 1600;

    private static readonly ConditionalWeakTable<BitmapSource, BitmapSource> Thumbs = [];
    private static readonly ConditionalWeakTable<BitmapSource, BitmapSource> Previews = [];

    private readonly ScanWorkflow _workflow = App.Workflow;
    private IReadOnlyList<ScanDeviceInfo> _devices = [];
    private IScanDevice? _device;
    private DeviceCapabilities? _caps;
    private CancellationTokenSource? _cts;
    private WriteableBitmap? _live;
    private bool _framePending;
    private bool _loading = true;
    private bool _busy;
    private int _deviceRequest;
    private IReadOnlyList<string> _lastSaved = [];

    public MainWindow()
    {
        InitializeComponent();
        if (Sandbox.IsOn)
            Title += " [SOC_SANDBOX]";
        LogoImage.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        PreviewKeyDown += OnWindowKey;
        _workflow.Document.Changed += OnDocumentChanged;
        PreviewImage.MouseLeftButtonDown += (_, _) => { if (_device is ILivePreview) PageList.SelectedIndex = -1; };

        var s = App.Settings;
        if (s.WindowWidth >= MinWidth && s.WindowHeight >= MinHeight)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }

        foreach (var preset in Enum.GetValues<QualityPreset>())
            PresetCombo.Items.Add(new ComboBoxItem { Content = Loc.Get("Preset" + preset), Tag = preset });
        foreach (var f in OutputFormats.All)
        {
            SingleFormatCombo.Items.Add(new ComboBoxItem { Content = FormatLabel(f), Tag = f });
            BatchFormatCombo.Items.Add(new ComboBoxItem { Content = FormatLabel(f), Tag = f });
        }
        for (var d = 1; d <= 6; d++)
            BatchDigitsCombo.Items.Add(new ComboBoxItem { Content = Loc.Format("DigitsItem", d, new string('0', d - 1) + "7"), Tag = d });

        SelectByTag(PresetCombo, s.Preset);
        SelectByTag(SingleFormatCombo, s.SingleFormat);
        SelectByTag(BatchFormatCombo, s.BatchFormat);
        SelectByTag(BatchDigitsCombo, Math.Clamp(s.BatchDigits, 1, 6));
        BatchFolderBox.Text = s.BatchFolder;
        BatchPrefixBox.Text = s.BatchPrefix;
        BatchSuffixBox.Text = s.BatchSuffix;
        BatchNumberBox.Text = s.BatchNextNumber.ToString(CultureInfo.CurrentCulture);
        (s.Mode == ScanMode.Batch ? BatchTile : SingleTile).IsChecked = true;
        (s.Source == ScanSource.Feeder ? FeederRadio : FlatbedRadio).IsChecked = true;
        if (App.LastSaved is { Count: > 0 } last)
            ShowLastSaved(last);
        _loading = false;

        ApplyMode();
        UpdateQualitySummary();
        UpdateBatchNext();
        RefreshPages();
        SetIdleStatus();
    }

    private static string FormatLabel(OutputFormat f) => Loc.Format("FormatItem", OutputFormats.Label(f), Loc.Get("Format" + f));

    private static void SelectByTag<T>(ComboBox combo, T value)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (Equals(item.Tag, value))
            {
                combo.SelectedItem = item;
                return;
            }
        if (combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static T? TagOf<T>(ComboBox combo) => combo.SelectedItem is ComboBoxItem { Tag: T value } ? value : default;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ThemeManager.ApplyToWindow(this);
        if (PresentationSource.FromVisual(this) is HwndSource src)
            src.AddHook(WndProc);
    }

    /// <summary>Otra instancia pide que se enseñe esta (instancia única, General §8.3).</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == SingleInstance.ShowMessage)
        {
            App.Instance.ShowMain();
            SingleInstance.NotifyShown();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"arranque de la ventana: {ex}");
        }
    }

    // ------------------------------------------------------------------ dispositivos

    private async Task RefreshDevicesAsync()
    {
        RefreshButton.IsEnabled = false;
        DeviceInfoText.Text = Loc.Get("DevicesSearching");
        try
        {
            // La ventana anterior (al cambiar de idioma) suelta antes la cámara: no se puede abrir dos veces.
            await App.DeviceReleased;
            _devices = await DeviceCatalog.ListAsync(Sandbox.IsOn, Loc.Get("SimulatedScanner"));
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
        _loading = true;
        DeviceCombo.Items.Clear();
        foreach (var d in _devices)
        {
            var item = new ComboBoxItem
            {
                Content = d.Kind == DeviceKind.Camera ? Loc.Format("DeviceCameraItem", d.Name) : d.Name,
                Tag = d,
            };
            AutomationProperties.SetAutomationId(item, "Device" + (DeviceCombo.Items.Count + 1));
            DeviceCombo.Items.Add(item);
        }
        _loading = false;
        if (_devices.Count == 0)
        {
            await SwitchDeviceAsync(null);
            return;
        }
        var wanted = _devices.FirstOrDefault(d => d.Id == App.Settings.DeviceId) ?? _devices[0];
        DeviceCombo.SelectedItem = DeviceCombo.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, wanted));
        if (_device?.Info.Id != wanted.Id)
            await SwitchDeviceAsync(wanted);
    }

    private async void OnRefreshDevices(object sender, RoutedEventArgs e)
    {
        try { await RefreshDevicesAsync(); }
        catch (Exception ex) { AppLog.Write($"refrescar dispositivos: {ex}"); }
    }

    private async void OnDeviceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DeviceCombo.SelectedItem is not ComboBoxItem { Tag: ScanDeviceInfo info } || info.Id == _device?.Info.Id)
            return;
        App.Settings.DeviceId = info.Id;
        App.Settings.Save();
        try { await SwitchDeviceAsync(info); }
        catch (Exception ex) { AppLog.Write($"cambiar de dispositivo: {ex}"); }
    }

    /// <summary>Suelta el dispositivo anterior (parando su cámara) y abre el nuevo, con lo que sabe hacer.</summary>
    private async Task SwitchDeviceAsync(ScanDeviceInfo? info)
    {
        var request = ++_deviceRequest;
        var old = _device;
        _device = null;
        _caps = null;
        if (old is ILivePreview oldLive)
        {
            oldLive.FrameArrived -= OnCameraFrame;
            try { await oldLive.StopPreviewAsync(); }
            catch (Exception ex) { AppLog.Write($"parar la cámara: {ex.Message}"); }
        }
        if (old is not null && old != App.Simulated)
            old.Dispose();
        _live = null;
        LiveBadge.Visibility = Visibility.Collapsed;
        SourcePanel.Visibility = Visibility.Collapsed;
        CameraPanel.Visibility = Visibility.Collapsed;
        if (info is null)
        {
            DeviceInfoText.Text = Loc.Get("NoDevices");
            UpdateButtons();
            UpdatePreview();
            return;
        }

        var device = DeviceCatalog.Open(info, App.Simulated);
        _device = device;
        DeviceInfoText.Text = Loc.Get("DeviceChecking");
        UpdateButtons();
        try
        {
            var caps = await device.GetCapabilitiesAsync();
            if (request != _deviceRequest)
                return;
            _caps = caps;
            DeviceInfoText.Text = DescribeDevice(info, caps);
            if (caps.Sources.Contains(ScanSource.Feeder) && caps.Sources.Contains(ScanSource.Flatbed))
                SourcePanel.Visibility = Visibility.Visible;
            if (device is ILivePreview live)
            {
                FillCameraResolutions(caps);
                CameraPanel.Visibility = Visibility.Visible;
                live.FrameArrived += OnCameraFrame;
                await StartCameraAsync(live, caps);
            }
        }
        catch (Exception ex)
        {
            if (request != _deviceRequest)
                return;
            AppLog.Write($"dispositivo {info.Name}: {ex}");
            DeviceInfoText.Text = Loc.Get(ScanErrors.KeyFor(ex));
        }
        UpdateQualitySummary();
        UpdateButtons();
        UpdatePreview();
    }

    private async Task StartCameraAsync(ILivePreview live, DeviceCapabilities caps)
    {
        try
        {
            await live.StartPreviewAsync(caps, App.Settings.CameraResolution);
            LiveBadge.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            AppLog.Write($"vista de la cámara: {ex}");
            DeviceInfoText.Text = Loc.Get(ScanErrors.KeyFor(ex));
        }
    }

    private static string DescribeDevice(ScanDeviceInfo info, DeviceCapabilities caps)
    {
        if (info.Kind == DeviceKind.Camera)
            return caps.CameraResolutions.Count > 0
                ? Loc.Format("DeviceCameraInfo", caps.CameraResolutions[0].Width, caps.CameraResolutions[0].Height)
                : Loc.Get("DeviceCameraNoInfo");
        var sources = string.Join(" · ", caps.Sources.Select(s => Loc.Get(s == ScanSource.Feeder ? "SourceFeeder" : "SourceFlatbed")));
        return Loc.Format("DeviceScannerInfo", sources, caps.MinDpi, caps.MaxDpi);
    }

    private void FillCameraResolutions(DeviceCapabilities caps)
    {
        _loading = true;
        CameraResCombo.Items.Clear();
        foreach (var (w, h) in caps.CameraResolutions)
            CameraResCombo.Items.Add(new ComboBoxItem { Content = Loc.Format("CameraResItem", w, h, Math.Round(w * h / 1e6, 1)), Tag = (w, h) });
        var wanted = App.Settings.CameraResolution;
        var match = CameraResCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => wanted is { } r && Equals(i.Tag, r));
        CameraResCombo.SelectedItem = match ?? (CameraResCombo.Items.Count > 0 ? CameraResCombo.Items[0] : null);
        _loading = false;
    }

    private async void OnCameraResChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || CameraResCombo.SelectedItem is not ComboBoxItem { Tag: ValueTuple<int, int> res })
            return;
        App.Settings.CameraWidth = res.Item1;
        App.Settings.CameraHeight = res.Item2;
        App.Settings.Save();
        if (_device is ILivePreview live && _caps is { } caps)
        {
            try { await StartCameraAsync(live, caps); }
            catch (Exception ex) { AppLog.Write($"resolución de la cámara: {ex}"); }
        }
    }

    /// <summary>
    /// Llega cada imagen de la cámara (en su hilo). Se pinta en la ventana como mucho una a la vez:
    /// si la anterior aún no se ha pintado, esta se salta (la interfaz nunca se atasca).
    /// </summary>
    private void OnCameraFrame(byte[] data, int width, int height, int stride)
    {
        if (_framePending)
            return;
        _framePending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _framePending = false;
            if (_device is not ILivePreview)
                return;
            if (_live is null || _live.PixelWidth != width || _live.PixelHeight != height)
                _live = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
            _live.WritePixels(new Int32Rect(0, 0, width, height), data, stride, 0);
            if (PageList.SelectedIndex < 0)
                UpdatePreview();
        });
    }

    private void OnSourceChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Settings.Source = FeederRadio.IsChecked == true ? ScanSource.Feeder : ScanSource.Flatbed;
        App.Settings.Save();
    }

    // ------------------------------------------------------------------ calidad

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PresetCombo.SelectedItem is not ComboBoxItem { Tag: QualityPreset preset })
            return;
        App.Settings.Preset = preset;
        App.Settings.Save();
        UpdateQualitySummary();
    }

    private void OnQuality(object sender, RoutedEventArgs e)
    {
        var dialog = new QualityWindow(App.Settings.EffectiveQuality(), _caps, _device?.Info.Kind ?? DeviceKind.Scanner) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        App.Settings.Preset = QualityPreset.Custom;
        App.Settings.CustomQuality = dialog.Result;
        App.Settings.Save();
        _loading = true;
        SelectByTag(PresetCombo, QualityPreset.Custom);
        _loading = false;
        UpdateQualitySummary();
    }

    /// <summary>Lo que se va a pedir de verdad, ya ajustado a lo que admite el dispositivo elegido.</summary>
    private void UpdateQualitySummary()
    {
        var q = App.Settings.EffectiveQuality();
        var color = Loc.Get("Color" + q.Color);
        if (_device?.Info.Kind == DeviceKind.Camera)
        {
            QualitySummaryText.Text = Loc.Format("QualitySummaryCamera", color);
            return;
        }
        var dpi = _caps is { } caps ? ScanPlanner.Plan(q, caps, App.Settings.Source, DeviceKind.Scanner).Dpi : q.Dpi;
        var area = Loc.Get("Area" + q.Area);
        var text = Loc.Format("QualitySummary", dpi, color, area);
        if (dpi != q.Dpi)
            text += " " + Loc.Format("QualityClamped", q.Dpi);
        QualitySummaryText.Text = text;
    }

    // ------------------------------------------------------------------ modo

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        App.Settings.Mode = BatchTile.IsChecked == true ? ScanMode.Batch : ScanMode.Single;
        App.Settings.Save();
        ApplyMode();
    }

    private void ApplyMode()
    {
        var batch = App.Settings.Mode == ScanMode.Batch;
        SinglePanel.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        BatchPanel.Visibility = batch ? Visibility.Visible : Visibility.Collapsed;
        DocumentButtons.Visibility = batch ? Visibility.Collapsed : Visibility.Visible;
        RefreshPages();
        UpdateButtons();
        UpdatePreview();
        if (!_busy)
            SetIdleStatus();
    }

    private void OnSingleFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SingleFormatCombo.SelectedItem is null)
            return;
        App.Settings.SingleFormat = TagOf<OutputFormat>(SingleFormatCombo);
        App.Settings.Save();
    }

    private void OnBatchFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BatchFormatCombo.SelectedItem is null)
            return;
        App.Settings.BatchFormat = TagOf<OutputFormat>(BatchFormatCombo);
        App.Settings.Save();
        UpdateBatchNext();
    }

    private void OnBatchDigitsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BatchDigitsCombo.SelectedItem is null)
            return;
        App.Settings.BatchDigits = TagOf<int>(BatchDigitsCombo);
        App.Settings.Save();
        UpdateBatchNext();
    }

    private void OnBrowseBatchFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.Get("BrowseFolder") };
        if (Directory.Exists(App.Settings.BatchFolder))
            dialog.InitialDirectory = App.Settings.BatchFolder;
        if (dialog.ShowDialog(this) != true)
            return;
        if (ScannerSettings.CheckFolder(dialog.FolderName) is { } problem)
        {
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get(problem));
            return;
        }
        App.Settings.BatchFolder = dialog.FolderName;
        App.Settings.Save();
        BatchFolderBox.Text = dialog.FolderName;
        UpdateBatchNext();
    }

    private void OnOpenBatchFolder(object sender, RoutedEventArgs e)
    {
        if (ScannerSettings.CheckFolder(App.Settings.BatchFolder) is { } problem)
        {
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get(problem));
            return;
        }
        Shell.Open(this, App.Settings.BatchFolder);
    }

    private void OnBatchFieldTyping(object sender, TextChangedEventArgs e)
    {
        if (!_loading)
            UpdateBatchNext(preview: true);
    }

    private void OnBatchFieldKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitBatchFields();
            e.Handled = true;
        }
    }

    private void OnBatchFieldCommit(object sender, RoutedEventArgs e) => CommitBatchFields();

    /// <summary>
    /// Prefijo, sufijo y número se aplican al salir de la casilla o con Intro (General §6.8); si no
    /// valen, se avisa y la casilla vuelve a lo guardado.
    /// </summary>
    private bool CommitBatchFields()
    {
        if (_loading)
            return true;
        var s = App.Settings;
        string? problem = FileNaming.CheckAffix(BatchPrefixBox.Text) ?? FileNaming.CheckAffix(BatchSuffixBox.Text);
        var numberOk = int.TryParse(BatchNumberBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) && number >= 0 && number <= 999_999_999;
        if (problem is null && !numberOk)
            problem = "ErrBatchNumber";
        if (problem is not null)
        {
            _loading = true;
            BatchPrefixBox.Text = s.BatchPrefix;
            BatchSuffixBox.Text = s.BatchSuffix;
            BatchNumberBox.Text = s.BatchNextNumber.ToString(CultureInfo.CurrentCulture);
            _loading = false;
            UpdateBatchNext();
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get(problem));
            return false;
        }
        if (s.BatchPrefix != BatchPrefixBox.Text || s.BatchSuffix != BatchSuffixBox.Text || s.BatchNextNumber != number)
        {
            s.BatchPrefix = BatchPrefixBox.Text;
            s.BatchSuffix = BatchSuffixBox.Text;
            s.BatchNextNumber = number;
            s.Save();
        }
        UpdateBatchNext();
        return true;
    }

    /// <summary>El nombre de la próxima hoja; mientras se escribe, con lo escrito (aún sin guardar).</summary>
    private void UpdateBatchNext(bool preview = false)
    {
        var s = App.Settings;
        if (preview)
        {
            var draft = new ScannerSettings
            {
                BatchFolder = s.BatchFolder,
                BatchFormat = s.BatchFormat,
                BatchDigits = s.BatchDigits,
                BatchPrefix = BatchPrefixBox.Text,
                BatchSuffix = BatchSuffixBox.Text,
                BatchNextNumber = int.TryParse(BatchNumberBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var n) && n >= 0 ? n : s.BatchNextNumber,
            };
            BatchNextText.Text = FileNaming.CheckAffix(draft.BatchPrefix) is null && FileNaming.CheckAffix(draft.BatchSuffix) is null
                ? ScanWorkflow.NextSeriesName(draft) : Loc.Get("ErrAffixChars");
            return;
        }
        BatchNextText.Text = ScanWorkflow.NextSeriesName(s);
    }

    // ------------------------------------------------------------------ escanear

    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _busy)
        {
            _cts?.Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ComboBox && ScanButton.IsEnabled)
        {
            e.Handled = true;
            OnScan(ScanButton, new RoutedEventArgs());
        }
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        if (_busy || _device is null)
            return;
        if (App.Settings.Mode == ScanMode.Batch && !CommitBatchFields())
            return;
        _busy = true;
        _cts = new CancellationTokenSource();
        UpdateButtons();
        StatusText.Text = Loc.Get(_device.Info.Kind == DeviceKind.Camera ? "StatusCapturing" : "StatusScanning");
        DetailText.Text = Loc.Get("StatusCancelHint");
        ScanProgress.Value = 0;
        ScanProgress.IsIndeterminate = true;
        ScanProgress.Visibility = Visibility.Visible;
        var progress = new Progress<int>(p =>
        {
            ScanProgress.IsIndeterminate = p <= 0;
            ScanProgress.Value = p;
        });
        try
        {
            var outcome = await _workflow.ScanAsync(_device, progress, _cts.Token);
            if (outcome.Saved.Count > 0)
            {
                App.LastSaved = outcome.Saved;
                ShowLastSaved(outcome.Saved);
                ShowPage(outcome.Pages[^1]);
                StatusText.Text = Loc.Format(outcome.Saved.Count == 1 ? "StatusSavedOne" : "StatusSavedMany", Path.GetFileName(outcome.Saved[^1]), outcome.Saved.Count);
                _loading = true;
                BatchNumberBox.Text = App.Settings.BatchNextNumber.ToString(CultureInfo.CurrentCulture);
                _loading = false;
                UpdateBatchNext();
                DetailText.Text = Loc.Format("StatusNextSheet", BatchNextText.Text);
            }
            else
            {
                PageList.SelectedIndex = _device is ILivePreview ? -1 : _workflow.Document.Count - 1;
                StatusText.Text = Loc.Format(outcome.Pages.Count == 1 ? "StatusScannedOne" : "StatusScannedMany", _workflow.Document.Count, outcome.Pages.Count);
                var page = outcome.Pages[^1];
                DetailText.Text = Loc.Format("StatusPageInfo", page.PixelWidth, page.PixelHeight, Math.Round(page.Dpi), Loc.Get("Color" + page.Color));
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.Get("StatusCancelled");
            DetailText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            AppLog.Write($"escanear con {_device?.Info.Name}: {ex}");
            StatusText.Text = Loc.Get("StatusFailed");
            DetailText.Text = string.Empty;
            var key = ex is SaveException se ? se.MessageKey : ScanErrors.KeyFor(ex);
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get(key));
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            ScanProgress.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ------------------------------------------------------------------ documento (una a una)

    private void OnDocumentChanged()
    {
        RefreshPages();
        UpdateButtons();
    }

    private void RefreshPages()
    {
        var pages = _workflow.Document.Pages.ToList();
        var single = App.Settings.Mode == ScanMode.Single;
        PagesCard.Visibility = single && pages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var selected = PageList.SelectedIndex;
        _loading = true;
        PageList.Items.Clear();
        for (var i = 0; i < pages.Count; i++)
        {
            var image = new Image { Width = ThumbWidth, Height = ThumbWidth * 1.3, Stretch = Stretch.Uniform };
            var label = new TextBlock { Text = (i + 1).ToString(CultureInfo.CurrentCulture), HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
            var item = new ListBoxItem { Content = new StackPanel { Children = { image, label } }, Tag = pages[i] };
            AutomationProperties.SetAutomationId(item, "Page" + (i + 1));
            AutomationProperties.SetName(item, Loc.Format("PageItem", i + 1));
            PageList.Items.Add(item);
            _ = LoadThumbAsync(image, pages[i].Image);
        }
        PageList.SelectedIndex = Math.Min(selected, pages.Count - 1);
        _loading = false;
        if (single && pages.Count > 0 && PageList.SelectedIndex < 0 && _device is not ILivePreview)
            PageList.SelectedIndex = pages.Count - 1;
        UpdatePreview();
    }

    private static async Task LoadThumbAsync(Image target, BitmapSource source)
    {
        try
        {
            if (!Thumbs.TryGetValue(source, out var thumb))
            {
                thumb = await Task.Run(() => ImageOps.Thumbnail(source, ThumbWidth * 2));
                Thumbs.AddOrUpdate(source, thumb);
            }
            target.Source = thumb;
        }
        catch (Exception ex)
        {
            AppLog.Write($"miniatura: {ex.Message}");
        }
    }

    private void OnPageSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        UpdatePreview();
        UpdateButtons();
    }

    private int SelectedPage => PageList.SelectedIndex;

    private void OnRotateLeft(object sender, RoutedEventArgs e) => RotateSelected(-90);

    private void OnRotateRight(object sender, RoutedEventArgs e) => RotateSelected(90);

    private void RotateSelected(int degrees)
    {
        var index = SelectedPage;
        if (index < 0)
            return;
        _workflow.Document.Rotate(index, degrees);
        PageList.SelectedIndex = index;
    }

    private void OnMoveLeft(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveRight(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var index = SelectedPage;
        if (index < 0)
            return;
        var target = _workflow.Document.Move(index, delta);
        PageList.SelectedIndex = target;
    }

    private void OnDeletePage(object sender, RoutedEventArgs e)
    {
        var index = SelectedPage;
        if (index < 0)
            return;
        _workflow.Document.RemoveAt(index);
        PageList.SelectedIndex = Math.Min(index, _workflow.Document.Count - 1);
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_workflow.Document.IsEmpty)
            return;
        if (!PromptWindow.Confirm(this, Loc.Get("ConfirmTitle"), Loc.Format("ClearConfirm", _workflow.Document.Count), danger: true))
            return;
        _workflow.Document.Clear();
        SetIdleStatus();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_workflow.Document.IsEmpty || _busy)
            return;
        var s = App.Settings;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.Get("ActionSave"),
            Filter = OutputFormats.DialogFilter(),
            FilterIndex = OutputFormats.All.ToList().IndexOf(s.SingleFormat) + 1,
            FileName = FileNaming.TimestampName(Loc.Get("FilePrefix"), DateTime.Now),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (ScannerSettings.CheckFolder(s.SaveFolder) is null)
            dialog.InitialDirectory = s.SaveFolder;
        if (dialog.ShowDialog(this) != true)
            return;
        var format = OutputFormats.FromExtension(Path.GetExtension(dialog.FileName)) ?? OutputFormats.All[Math.Clamp(dialog.FilterIndex - 1, 0, OutputFormats.All.Count - 1)];
        var path = Path.ChangeExtension(dialog.FileName, OutputFormats.Extension(format));
        await SaveDocumentAsync(path, format);
    }

    /// <summary>Guarda el documento (fuera del hilo de la interfaz) y enseña dónde ha quedado.</summary>
    internal async Task SaveDocumentAsync(string path, OutputFormat format)
    {
        var s = App.Settings;
        _busy = true;
        UpdateButtons();
        StatusText.Text = Loc.Get("StatusSaving");
        DetailText.Text = string.Empty;
        ScanProgress.IsIndeterminate = true;
        ScanProgress.Visibility = Visibility.Visible;
        try
        {
            var pages = _workflow.Document.Count;
            var saved = await _workflow.SaveDocumentAsync(path, format, s.EffectiveQuality().JpegQuality);
            s.SaveFolder = Path.GetDirectoryName(path) ?? s.SaveFolder;
            s.SingleFormat = format;
            s.Save();
            _loading = true;
            SelectByTag(SingleFormatCombo, format);
            _loading = false;
            App.LastSaved = saved;
            ShowLastSaved(saved);
            StatusText.Text = Loc.Format(saved.Count == 1 ? "StatusDocSavedOne" : "StatusDocSavedMany", Path.GetFileName(saved[0]), pages, saved.Count);
        }
        catch (SaveException ex)
        {
            StatusText.Text = Loc.Get("StatusFailed");
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get(ex.MessageKey));
        }
        finally
        {
            _busy = false;
            ScanProgress.Visibility = Visibility.Collapsed;
            UpdateButtons();
        }
    }

    // ------------------------------------------------------------------ vista previa

    private void ShowPage(ScannedPage page) => _ = ShowImageAsync(page.Image);

    private void UpdatePreview()
    {
        if (App.Settings.Mode == ScanMode.Single && PageList.SelectedItem is ListBoxItem { Tag: ScannedPage page })
        {
            LiveBadge.Visibility = Visibility.Collapsed;
            _ = ShowImageAsync(page.Image);
            return;
        }
        if (_device is ILivePreview && _live is not null)
        {
            LiveBadge.Visibility = Visibility.Visible;
            PreviewImage.Source = _live;
            EmptyHint.Visibility = Visibility.Collapsed;
            return;
        }
        LiveBadge.Visibility = Visibility.Collapsed;
        if (App.Settings.Mode == ScanMode.Batch && PreviewImage.Source is not null && PreviewImage.Source != _live)
            return;   // en serie se queda a la vista la última hoja guardada
        PreviewImage.Source = null;
        EmptyHint.Visibility = Visibility.Visible;
        EmptyHintText.Text = _device is null ? Loc.Get("EmptyNoDevice")
            : _device.Info.Kind == DeviceKind.Camera ? Loc.Get("EmptyCamera")
            : Loc.Get(App.Settings.Mode == ScanMode.Batch ? "EmptyBatch" : "EmptySingle");
    }

    private async Task ShowImageAsync(BitmapSource source)
    {
        try
        {
            if (!Previews.TryGetValue(source, out var preview))
            {
                preview = source.PixelWidth > PreviewWidth ? await Task.Run(() => ImageOps.Thumbnail(source, PreviewWidth)) : source;
                Previews.AddOrUpdate(source, preview);
            }
            PreviewImage.Source = preview;
            EmptyHint.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AppLog.Write($"vista previa: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ último guardado

    private void ShowLastSaved(IReadOnlyList<string> saved)
    {
        _lastSaved = saved;
        if (saved.Count == 0)
        {
            LastCard.Visibility = Visibility.Collapsed;
            return;
        }
        LastCard.Visibility = Visibility.Visible;
        LastNameText.Text = Path.GetFileName(saved[^1]);
        long bytes = 0;
        foreach (var f in saved)
            try { bytes += new FileInfo(f).Length; } catch (IOException) { }
        LastInfoText.Text = saved.Count == 1
            ? Loc.Format("LastInfoOne", FormatSize(bytes), Path.GetDirectoryName(saved[0]))
            : Loc.Format("LastInfoMany", saved.Count, FormatSize(bytes), Path.GetDirectoryName(saved[0]));
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? Loc.Format("SizeMb", Math.Round(bytes / (1024.0 * 1024), 1))
        : Loc.Format("SizeKb", Math.Max(1, (int)Math.Round(bytes / 1024.0)));

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        if (_lastSaved.Count > 0)
            Shell.Open(this, _lastSaved[^1]);
    }

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (_lastSaved.Count > 0)
            Shell.ShowInFolder(this, _lastSaved[^1]);
    }

    private void OnCopyFile(object sender, RoutedEventArgs e)
    {
        var files = _lastSaved.Where(File.Exists).ToArray();
        if (files.Length == 0)
        {
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get("ErrFileGone"));
            return;
        }
        try
        {
            var list = new StringCollection();
            list.AddRange(files);
            Clipboard.SetFileDropList(list);
            DetailText.Text = Loc.Get("CopiedFile");
        }
        catch (Exception ex)
        {
            AppLog.Write($"copiar fichero: {ex.Message}");
            PromptWindow.Alert(this, Loc.Get("ErrorTitle"), Loc.Get("ErrClipboard"));
        }
    }

    // ------------------------------------------------------------------ estado

    private void UpdateButtons()
    {
        var hasDevice = _device is not null && _caps is not null;
        ScanButton.IsEnabled = hasDevice && !_busy;
        CancelButton.Visibility = _busy && _cts is not null ? Visibility.Visible : Visibility.Collapsed;
        var pages = _workflow.Document.Count;
        SaveButton.IsEnabled = !_busy && pages > 0;
        ClearButton.IsEnabled = !_busy && pages > 0;
        var sel = SelectedPage;
        RotateLeftButton.IsEnabled = RotateRightButton.IsEnabled = DeletePageButton.IsEnabled = !_busy && sel >= 0;
        MoveLeftButton.IsEnabled = !_busy && sel > 0;
        MoveRightButton.IsEnabled = !_busy && sel >= 0 && sel < pages - 1;
        DeviceCombo.IsEnabled = RefreshButton.IsEnabled = !_busy;
        PresetCombo.IsEnabled = QualityButton.IsEnabled = !_busy;
        SingleTile.IsEnabled = BatchTile.IsEnabled = !_busy;
        SinglePanel.IsEnabled = BatchPanel.IsEnabled = !_busy;
    }

    private void SetIdleStatus()
    {
        var pages = _workflow.Document.Count;
        if (App.Settings.Mode == ScanMode.Single && pages > 0)
        {
            StatusText.Text = Loc.Format("StatusPagesPending", pages);
            DetailText.Text = Loc.Get("StatusPagesPendingHint");
        }
        else
        {
            StatusText.Text = Loc.Get(App.Settings.Mode == ScanMode.Batch ? "StatusReadyBatch" : "StatusReady");
            DetailText.Text = App.Settings.Mode == ScanMode.Batch ? Loc.Format("StatusNextSheet", BatchNextText.Text) : string.Empty;
        }
    }

    // ------------------------------------------------------------------ cabecera

    private void OnGuide(object sender, RoutedEventArgs e) => new GuideWindow(_devices.Count) { Owner = this }.ShowDialog();

    private void OnNews(object sender, RoutedEventArgs e) => new WhatsNewWindow { Owner = this }.ShowDialog();

    private void OnLanguage(object sender, RoutedEventArgs e) => Loc.Toggle();

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    // ------------------------------------------------------------------ cerrar

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.Rebuilding)
            return;
        if (_busy && _cts is not null)
            _cts.Cancel();
        if (_workflow.Document.HasUnsaved
            && !PromptWindow.Confirm(this, Loc.Get("ConfirmTitle"), Loc.Format("CloseUnsavedConfirm", _workflow.Document.Count), danger: true))
        {
            e.Cancel = true;
            return;
        }
        if (WindowState == WindowState.Normal)
        {
            App.Settings.WindowWidth = Width;
            App.Settings.WindowHeight = Height;
            App.Settings.Save();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _workflow.Document.Changed -= OnDocumentChanged;
        App.DeviceReleased = ReleaseDeviceAsync();
        if (!App.Rebuilding)
            App.Instance.Shutdown();
    }

    private async Task ReleaseDeviceAsync()
    {
        try { await SwitchDeviceAsync(null); }
        catch (Exception ex) { AppLog.Write($"soltar el dispositivo: {ex.Message}"); }
    }
}

/// <summary>Abrir un fichero o una carpeta con el programa de Windows, o enseñarlo en el Explorador.</summary>
public static class Shell
{
    public static void Open(Window owner, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"abrir {path}: {ex.Message}");
            PromptWindow.Alert(owner, Loc.Get("ErrorTitle"), Loc.Format("ErrOpen", path));
        }
    }

    public static void ShowInFolder(Window owner, string path)
    {
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                Open(owner, Path.GetDirectoryName(path) ?? path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"enseñar {path}: {ex.Message}");
            PromptWindow.Alert(owner, Loc.Get("ErrorTitle"), Loc.Format("ErrOpen", path));
        }
    }
}
