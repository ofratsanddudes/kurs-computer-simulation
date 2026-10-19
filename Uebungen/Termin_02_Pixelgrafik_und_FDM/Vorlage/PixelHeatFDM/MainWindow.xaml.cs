using System.Windows;
using System.Windows.Input;     // MouseButtonState, MouseEventArgs, MouseButtonEventArgs
using System.Windows.Media;
using System.Windows.Threading;
using PixelHeatFDM.Rendering;
using PixelHeatFDM.Simulation;

namespace PixelHeatFDM;

public partial class MainWindow : Window
{
    // Anzahl Gitterzellen pro Seite (128 x 128 = 16 384 Zellen)
    private const int GridResolution = 128;

    // Pinselradius in Gitterzellen für das Maus-Zeichnen (Aufgabe 3: 2 Pixel)
    private const int BrushRadius = 2;

    private readonly HeatGrid _grid;                 // Simulation (Temperaturen, Materialgitter)
    private readonly BitmapHeatRenderer _renderer;   // Darstellung (Temperatur -> Farbe -> Bitmap)
    private readonly DispatcherTimer _timer;         // Takt der Simulation, läuft im UI-Thread (single-threaded)

    private int _stepCount = 0;

    public MainWindow()
    {
        InitializeComponent();

        _grid = new HeatGrid(GridResolution, GridResolution);
        _renderer = new BitmapHeatRenderer(GridResolution, GridResolution);

        // Die WriteableBitmap des Renderers wird dem Image-Control aus dem XAML zugewiesen
        HeatImage.Source = _renderer.Bitmap;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ca. 30 FPS
        };
        _timer.Tick += OnSimulationTick;

        UpdateUiFromParameters();
        _renderer.Render(_grid);   // Startbild zeichnen, auch wenn die Simulation noch pausiert
    }

    /// <summary>
    /// Wird bei jedem Timer-Tick aufgerufen: ein FDM-Schritt, dann neu zeichnen.
    /// </summary>
    private void OnSimulationTick(object? sender, EventArgs e)
    {
        // Physikalischer FDM-Schritt
        _grid.Step();
        _stepCount++;

        // Visualisierung aktualisieren
        _renderer.Render(_grid);

        TxtStatus.Text = $"Schritte: {_stepCount} | s = {_grid.StabilityFactor:F4} | Status: {(_grid.IsStable ? "OK" : "INSTABIL")}";
    }

    /// <summary>
    /// Beide Slider (Delta t und alpha) laufen über denselben Handler.
    /// </summary>
    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Schutz: Beim Laden des Fensters feuert WPF die Events schon,
        // bevor _grid und die TextBlocks existieren.
        if (_grid == null || TxtDtValue == null) return;

        _grid.Dt = (float)SliderDt.Value;
        _grid.Alpha = (float)SliderAlpha.Value;

        UpdateUiFromParameters();
    }

    /// <summary>
    /// Aktualisiert Textanzeigen und die Stabilitäts-Ampel passend zu den aktuellen Parametern.
    /// </summary>
    private void UpdateUiFromParameters()
    {
        TxtDtValue.Text = $"{_grid.Dt:F4} s";
        TxtAlphaValue.Text = $"{_grid.Alpha:E2}";

        // Diffusionszahl s = alpha * dt / dx^2, stabil nur für s <= 0.25
        float s = _grid.StabilityFactor;
        TxtStabilityFactor.Text = $"s = {s:F4} (Grenze: 0.2500)";
        TxtCriticalDt.Text = $"dt_krit = {_grid.CriticalDt:F5} s";

        if (_grid.IsStable)
        {
            // Grün: stabiler Bereich
            StabilityBadge.Background = new SolidColorBrush(Color.FromRgb(27, 77, 27));
            TxtStabilityFactor.Foreground = new SolidColorBrush(Color.FromRgb(152, 251, 152));
            TxtStabilityStatus.Text = "STABIL: Keine numerische Oszillation";
        }
        else
        {
            // Rot: Gitterexplosion zu erwarten
            StabilityBadge.Background = new SolidColorBrush(Color.FromRgb(120, 20, 20));
            TxtStabilityFactor.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));
            TxtStabilityStatus.Text = "WARNUNG: s > 0.25! Gitter explodiert!";
        }
    }

    /// <summary>
    /// Start/Pause: schaltet den Timer um.
    /// </summary>
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

    /// <summary>
    /// Zurücksetzen: Simulation anhalten, Gitter auf Startzustand, alle Wände entfernen.
    /// </summary>
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        BtnStartStop.Content = "Start";
        _stepCount = 0;
        _grid.Reset();
        _renderer.Render(_grid);
        TxtStatus.Text = "Simulation zurückgesetzt.";
    }

    /// <summary>
    /// Umschalten zwischen Dirichlet (feste 20 °C am Rand) und Neumann (adiabat isoliert).
    /// </summary>
    private void RbBoundary_Checked(object sender, RoutedEventArgs e)
    {
        // Schutz: Das Event feuert schon beim Laden des Fensters, bevor _grid existiert.
        if (_grid == null) return;

        if (RbNeumann?.IsChecked == true)
        {
            _grid.Boundary = BoundaryMode.NeumannAdiabatic;
        }
        else
        {
            _grid.Boundary = BoundaryMode.DirichletFixed;
        }
    }

    // ------------------------------------------------------------------
    // Maus-Zeichnen (Stufe A, Aufgabe 3)
    // Linke Maustaste  = heiße Quelle (100 °C)
    // Rechte Maustaste = Isolator-Wand (alpha = 0)
    // ------------------------------------------------------------------

    // Klick: sofort einmal zeichnen (sonst würde ein einzelner Klick ohne Bewegung nichts tun)
    private void HeatImage_MouseDown(object sender, MouseButtonEventArgs e) => PaintFromMouse(e);

    // Ziehen: zeichnen, solange eine Taste gedrückt gehalten wird
    private void HeatImage_MouseMove(object sender, MouseEventArgs e) => PaintFromMouse(e);

    private void PaintFromMouse(MouseEventArgs e)
    {
        // Welche Taste ist gerade gedrückt? Ohne Taste: nichts zeichnen (reines Mausbewegen).
        bool left = e.LeftButton == MouseButtonState.Pressed;
        bool right = e.RightButton == MouseButtonState.Pressed;
        if (!left && !right) return;

        // Mausposition relativ zum Image-Control. Das Image ist im XAML 256 x 256 groß
        // (die Viewbox skaliert nur die Darstellung, nicht diese Koordinaten).
        Point pos = e.GetPosition(HeatImage);

        // Umrechnung Bildschirmkoordinate -> Gitterzelle (0 .. 127):
        // Anteil an der Breite/Höhe mal Zellenanzahl, int schneidet die Nachkommastellen ab.
        int gx = (int)(pos.X / HeatImage.ActualWidth * GridResolution);
        int gy = (int)(pos.Y / HeatImage.ActualHeight * GridResolution);

        // Rechts = Wand (insulator: true), links = heiße Quelle (insulator: false)
        _grid.Paint(gx, gy, BrushRadius, insulator: right);

        // Bei pausierter Simulation gibt es keinen Tick, der neu zeichnet,
        // darum hier einmal manuell rendern, damit man das Gezeichnete sofort sieht.
        if (!_timer.IsEnabled)
            _renderer.Render(_grid);
    }
}