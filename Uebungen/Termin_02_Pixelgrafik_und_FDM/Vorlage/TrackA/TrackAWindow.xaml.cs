using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TrackA;

public partial class TrackAWindow : Window
{
    private readonly CpuCoolerGrid _grid;
    private readonly CpuHeatRenderer _renderer;
    private readonly DispatcherTimer _renderTimer;

    private CancellationTokenSource? _simCts;
    private volatile bool _isRunning = true;
    private bool _fanFail = false;

    // Standardmäßig 500 Schritte pro Block: 50.000 bis 80.000 Schritte/s
    private volatile int _stepsPerTick = 500;

    public TrackAWindow()
    {
        InitializeComponent();

        _grid = new CpuCoolerGrid();
        _renderer = new CpuHeatRenderer(CpuCoolerGrid.Width, CpuCoolerGrid.Height);
        CoolerImage.Source = _renderer.Bitmap;

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // 60 FPS
        };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();

        StartPhysicsWorker();
    }

    private void StartPhysicsWorker()
    {
        _simCts = new CancellationTokenSource();
        var token = _simCts.Token;

        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (_isRunning)
                {
                    int batch = _stepsPerTick;
                    for (int i = 0; i < batch; i++)
                    {
                        _grid.Step();
                    }

                    // Extrem kurzes Ausruhen, damit der UI-Thread 100 % weich bleibt
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }, token);
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        _renderer.Render(_grid, minTemp: 25.0f, maxTemp: 95.0f);

        float tMax = _grid.MaxCpuTemperature;
        if (float.IsNaN(tMax) || float.IsInfinity(tMax))
        {
            TxtMaxTemp.Text = "EXPLODIERT (NaN)!";
            TxtMaxTemp.Foreground = Brushes.Magenta;
        }
        else
        {
            TxtMaxTemp.Text = $"{tMax:F1} °C";
            TxtMaxTemp.Foreground = tMax > 85.0f ? Brushes.Red : Brushes.Orange;
        }
    }

    private void BtnStartStop_Click(object sender, RoutedEventArgs e)
    {
        _isRunning = !_isRunning;
        if (!_isRunning)
        {
            BtnStartStop.Content = "Start";
            BtnStartStop.Background = Brushes.DarkSlateGray;
        }
        else
        {
            BtnStartStop.Content = "Pause";
            BtnStartStop.Background = new SolidColorBrush(Color.FromRgb(46, 125, 50));
        }
    }

    private void BtnToggleFan_Click(object sender, RoutedEventArgs e)
    {
        _fanFail = !_fanFail;
        if (_fanFail)
        {
            _grid.HeatTransferCoeff = 20.0f;
            BtnToggleFan.Content = "Lüfter: AUSFALL (20 W/m²K)";
            BtnToggleFan.Background = Brushes.DarkRed;
        }
        else
        {
            _grid.HeatTransferCoeff = 250.0f;
            BtnToggleFan.Content = "Lüfter: NORMAL (250 W/m²K)";
            BtnToggleFan.Background = Brushes.SteelBlue;
        }
    }

    private void SliderDt_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtDtValue == null || _grid == null) return;

        float dtMicroSec = (float)SliderDt.Value;
        _grid.Dt = dtMicroSec * 1e-6f;

        if (dtMicroSec > 86.7f)
        {
            TxtDtValue.Text = $"{dtMicroSec:F1} µs (INSTABIL! s > 0.25)";
            TxtDtValue.Foreground = Brushes.Red;
        }
        else
        {
            TxtDtValue.Text = $"{dtMicroSec:F1} µs (Stabil)";
            TxtDtValue.Foreground = Brushes.LightGreen;
        }
    }

    private void SliderSpeed_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSpeedValue == null) return;
        _stepsPerTick = (int)SliderSpeed.Value;
        TxtSpeedValue.Text = $"{_stepsPerTick * 60} Schritte/s";
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _grid.ResetTemperatures();
        _renderer.Render(_grid, minTemp: 25.0f, maxTemp: 95.0f);
        TxtMaxTemp.Text = $"{_grid.AmbientTemp:F1} °C";
        TxtMaxTemp.Foreground = Brushes.Orange;
    }

    protected override void OnClosed(EventArgs e)
    {
        _simCts?.Cancel();
        base.OnClosed(e);
    }
}