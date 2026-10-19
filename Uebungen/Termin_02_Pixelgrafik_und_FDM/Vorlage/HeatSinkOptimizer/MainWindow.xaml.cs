using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HeatSinkOptimizer.Rendering;
using HeatSinkOptimizer.Simulation;

namespace HeatSinkOptimizer;

public partial class MainWindow : Window
{
    // Temperaturbereich der Farbskala [°C]: blau = TempMin, rot = TempMax
    private const float TempMin = 25.0f;
    private const float TempMax = 300.0f;

    private const float FanOnH = 250.0f;    // forcierte Konvektion [W/(m^2*K)]
    private const float FanOffH = 20.0f;    // Lüfterausfall: freie Konvektion

    private readonly HeatSinkGrid _grid = new();
    private readonly HeatSinkRenderer _renderer;
    private readonly DispatcherTimer _timer;

    private double _simTime;    // simulierte Zeit [s]
    private bool _ready;        // WPF feuert Slider-/CheckBox-Events schon in InitializeComponent

    public MainWindow()
    {
        InitializeComponent();

        _renderer = new HeatSinkRenderer(HeatSinkGrid.Width, HeatSinkGrid.Height);
        HeatImage.Source = _renderer.Bitmap;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        _ready = true;
        ApplyParameters();
        RenderFrame();
    }

    /// <summary>Pro Tick: mehrere Simulationsschritte (dt ist winzig), dann ein Bild.</summary>
    private void OnTick(object? sender, EventArgs e)
    {
        int substeps = (int)SliderSubsteps.Value;
        for (int k = 0; k < substeps; k++)
            _grid.Step();

        _simTime += substeps * (double)_grid.Dt;
        RenderFrame();
    }

    private void RenderFrame()
    {
        _renderer.RenderTemperatures(_grid, TempMin, TempMax);

        float tMax = _grid.MaxDieTemperature();
        TxtCpuTemp.Text = float.IsFinite(tMax) ? $"{tMax:F1} °C" : "NaN / ∞";
        TxtSimTime.Text = $"t_sim = {_simTime:F2} s";
    }

    /// <summary>Übernimmt die Slider in die Simulation und aktualisiert die Stabilitätsanzeige.</summary>
    private void ApplyParameters()
    {
        if (!_ready) return;

        _grid.Dt = (float)SliderDtFactor.Value * _grid.CriticalDt;

        // s = alpha_max * dt / dx^2 (Grenze 0,25), wie in der Vorlesung
        float s = _grid.MaxAlpha * _grid.Dt / (HeatSinkGrid.Dx * HeatSinkGrid.Dx);

        TxtSubsteps.Text = $"{(int)SliderSubsteps.Value}";
        TxtDtFactor.Text = $"{SliderDtFactor.Value:F2}";
        TxtDt.Text = $"dt = {_grid.Dt:E3} s | dt_krit = {_grid.CriticalDt:E3} s";
        TxtInfo.Text = $"{HeatSinkGrid.Width}x{HeatSinkGrid.Height} Gitter | dx = {HeatSinkGrid.Dx * 1000:F1} mm | " +
                       $"alpha_max = {_grid.MaxAlpha:E3} m²/s | h = {_grid.ConvectionH:F0} W/m²K";

        if (_grid.IsStable)
        {
            TxtStability.Text = $"STABIL: s = {s:F3} (Grenze 0,25)";
            TxtStability.Foreground = new SolidColorBrush(Color.FromRgb(152, 251, 152));
        }
        else
        {
            TxtStability.Text = $"INSTABIL: s = {s:F3} > 0,25, Gitter explodiert!";
            TxtStability.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
        }
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyParameters();

    /// <summary>Lüfter an/aus: setzt h neu (Robin-Kopplung und dt_krit werden neu berechnet).</summary>
    private void FanChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;

        _grid.SetConvection(ChkFan.IsChecked == true ? FanOnH : FanOffH);
        ApplyParameters();
    }

    private void BtnStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            BtnStartStop.Content = "Fortsetzen";
        }
        else
        {
            _timer.Start();
            BtnStartStop.Content = "Pause";
        }
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        BtnStartStop.Content = "Start";
        _grid.Reset();
        _simTime = 0.0;
        RenderFrame();
    }
}