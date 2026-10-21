using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SocScanner.App.Services;
using SocScanner.Localization;
using SocScanner.Scanning;

namespace SocScanner.App;

/// <summary>
/// Calidad a medida: resolución (solo las que admite el dispositivo elegido), color, zona, brillo,
/// contraste y calidad JPEG, con los megas aproximados de una hoja a la vista. Guardar deja la calidad
/// en «Personalizada». En una cámara no hay resolución ni zona: se elige su resolución en la ventana.
/// </summary>
public sealed class QualityWindow : Window
{
    private readonly ComboBox _dpi = new();
    private readonly RadioButton _color = new();
    private readonly RadioButton _gray = new();
    private readonly RadioButton _bw = new();
    private readonly ComboBox _area = new();
    private readonly Slider _brightness = new();
    private readonly Slider _contrast = new();
    private readonly Slider _jpeg = new();
    private readonly TextBlock _brightnessValue = new();
    private readonly TextBlock _contrastValue = new();
    private readonly TextBlock _jpegValue = new();
    private readonly TextBlock _estimate = new();
    private readonly bool _camera;
    private bool _ready;

    public ScanQuality Result { get; private set; }

    public QualityWindow(ScanQuality current, DeviceCapabilities? caps, DeviceKind kind)
    {
        Result = current.Clone().Normalize();
        _camera = kind == DeviceKind.Camera;
        Title = Loc.Get("QualityWindowTitle");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("PageBackground");
        SourceInitialized += (_, _) => ThemeManager.ApplyToWindow(this);

        var stack = new StackPanel();
        if (!_camera)
        {
            stack.Children.Add(Label("QualityDpi"));
            Setup(_dpi, "DpiCombo", "QualityDpi");
            var choices = caps is null ? ScanQuality.StandardDpis : ScanPlanner.DpiChoices(caps);
            foreach (var d in choices)
                _dpi.Items.Add(new ComboBoxItem { Content = Loc.Format("DpiItem", d), Tag = d });
            var nearest = choices.OrderBy(d => Math.Abs(d - Result.Dpi)).First();
            _dpi.SelectedItem = _dpi.Items.Cast<ComboBoxItem>().First(i => (int)i.Tag == nearest);
            _dpi.SelectionChanged += (_, _) => UpdateEstimate();
            stack.Children.Add(_dpi);
            if (caps is not null)
                stack.Children.Add(Hint(Loc.Format("QualityDpiRange", caps.MinDpi, caps.MaxDpi)));
        }

        stack.Children.Add(Label("QualityColor"));
        var colors = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (radio, mode, id) in new[] { (_color, ColorMode.Color, "ColorColorRadio"), (_gray, ColorMode.Grayscale, "ColorGrayRadio"), (_bw, ColorMode.BlackWhite, "ColorBlackWhiteRadio") })
        {
            radio.Style = (Style)FindResource("Radio");
            radio.Content = Loc.Get("Color" + mode);
            radio.GroupName = "Color";
            radio.Margin = new Thickness(0, 4, 16, 0);
            radio.IsChecked = Result.Color == mode;
            radio.Checked += (_, _) => UpdateEstimate();
            AutomationProperties.SetAutomationId(radio, id);
            colors.Children.Add(radio);
        }
        stack.Children.Add(colors);
        stack.Children.Add(Hint(Loc.Get("QualityColorHint")));

        if (!_camera)
        {
            stack.Children.Add(Label("QualityArea"));
            Setup(_area, "AreaCombo", "QualityArea");
            foreach (var a in Enum.GetValues<PageArea>())
                _area.Items.Add(new ComboBoxItem { Content = Loc.Get("Area" + a), Tag = a });
            _area.SelectedItem = _area.Items.Cast<ComboBoxItem>().First(i => (PageArea)i.Tag == Result.Area);
            _area.SelectionChanged += (_, _) => UpdateEstimate();
            stack.Children.Add(_area);
        }

        stack.Children.Add(SliderRow("QualityBrightness", _brightness, _brightnessValue, "BrightnessSlider", -100, 100, Result.Brightness));
        stack.Children.Add(SliderRow("QualityContrast", _contrast, _contrastValue, "ContrastSlider", -100, 100, Result.Contrast));
        stack.Children.Add(SliderRow("QualityJpeg", _jpeg, _jpegValue, "JpegSlider", 40, 100, Result.JpegQuality));
        stack.Children.Add(Hint(Loc.Get("QualityJpegHint")));

        _estimate.Style = (Style)FindResource("BodyText");
        _estimate.FontWeight = FontWeights.SemiBold;
        _estimate.Margin = new Thickness(0, 14, 0, 0);
        AutomationProperties.SetAutomationId(_estimate, "EstimateText");
        stack.Children.Add(_estimate);

        var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(18, 6, 18, 16), Child = stack };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Style = (Style)FindResource("GhostIconButton"), Content = "", ToolTip = Loc.Get("Cancel"), IsCancel = true };
        AutomationProperties.SetAutomationId(cancel, "CancelButton");
        AutomationProperties.SetName(cancel, Loc.Get("Cancel"));
        var ok = new Button { Style = (Style)FindResource("IconButton"), Content = "", ToolTip = Loc.Get("Save"), IsDefault = true };
        AutomationProperties.SetAutomationId(ok, "OkButton");
        AutomationProperties.SetName(ok, Loc.Get("Save"));
        ok.Click += (_, _) =>
        {
            Result = Read();
            DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(card);
        root.Children.Add(buttons);
        Content = root;
        _ready = true;
        UpdateEstimate();
    }

    private ScanQuality Read() => new ScanQuality
    {
        Dpi = _camera ? Result.Dpi : (int)((ComboBoxItem)_dpi.SelectedItem).Tag,
        Color = _bw.IsChecked == true ? ColorMode.BlackWhite : _gray.IsChecked == true ? ColorMode.Grayscale : ColorMode.Color,
        Area = _camera ? Result.Area : (PageArea)((ComboBoxItem)_area.SelectedItem).Tag,
        Brightness = (int)Math.Round(_brightness.Value),
        Contrast = (int)Math.Round(_contrast.Value),
        JpegQuality = (int)Math.Round(_jpeg.Value),
    }.Normalize();

    private void UpdateEstimate()
    {
        if (!_ready)
            return;   // mientras se montan los controles, sus eventos saltan con la mitad sin valor
        var q = Read();
        if (_camera)
        {
            _estimate.Text = Loc.Format("QualityEstimateCamera", Loc.Get("Color" + q.Color));
            return;
        }
        var (mp, mb) = ScanQuality.Estimate(q.Dpi, q.Color, q.Area);
        var text = Loc.Format("QualityEstimate", Math.Round(mp, 1), Math.Round(mb, mb < 10 ? 1 : 0));
        if (mb > 200)
            text += " " + Loc.Get("QualityEstimateHuge");
        _estimate.Text = text;
    }

    private void Setup(ComboBox combo, string id, string nameKey)
    {
        combo.Style = (Style)FindResource("Combo");
        combo.MinWidth = 0;
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetAutomationId(combo, id);
        AutomationProperties.SetName(combo, Loc.Get(nameKey));
    }

    private FrameworkElement SliderRow(string labelKey, Slider slider, TextBlock value, string id, int min, int max, int current)
    {
        var panel = new StackPanel();
        panel.Children.Add(Label(labelKey));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        slider.Style = (Style)FindResource("Adjust");
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Value = current;
        slider.IsSnapToTickEnabled = true;
        slider.TickFrequency = 1;
        AutomationProperties.SetAutomationId(slider, id);
        AutomationProperties.SetName(slider, Loc.Get(labelKey));
        value.Style = (Style)FindResource("BodyText");
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Text = current.ToString(CultureInfo.CurrentCulture);
        slider.ValueChanged += (_, e) =>
        {
            value.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture);
            UpdateEstimate();
        };
        Grid.SetColumn(value, 1);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        panel.Children.Add(grid);
        return panel;
    }

    private TextBlock Label(string key) => new() { Text = Loc.Get(key), Style = (Style)FindResource("FieldLabel") };

    private TextBlock Hint(string text) => new() { Text = text, Style = (Style)FindResource("HintText"), Margin = new Thickness(0, 4, 0, 0) };
}
