using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlotTelemetry.Statistics;            // WelfordStatistics aus dem Stufe-1-Projekt (Projektreferenz)
using ScottPlotTelemetry.TrackA.Monitoring;
using ScottPlotTelemetry.TrackA.Simulation;
using ScottPlotTelemetry.TrackA.Topology;

// Aliase: "Color" und "Colors" gibt es in ScottPlot UND in System.Windows.Media
using MediaColor = System.Windows.Media.Color;
using SpColor = ScottPlot.Color;
using SpColors = ScottPlot.Colors;

namespace ScottPlotTelemetry.TrackA;

/// <summary>
/// Track A: Leitstand eines Motorprüfstands mit 3 ScottPlot-Panels + MSAGL-Graph.
/// Alle Plot-Objekte, Arrays und Brushes werden im Konstruktor angelegt;
/// der 50-Hz-Tick schreibt nur noch Werte hinein (kein new im Render-Loop).
/// </summary>
public partial class MainWindow : Window
{
    // ---------------------------------------------------------------------
    // Konstanten
    // ---------------------------------------------------------------------
    private const int StreamCapacity = 500;   // 500 · 20 ms = 10 s Zeitfenster in Panel 1
    private const int TrailLength = 150;      // letzte 3 s als Spur im Kennfeld (Panel 2)
    private const int HistBins = 20;          // laut Aufgabenblatt A.3
    private const double HistMin = -1.2;      // Bereich der Vibrationsamplitude [g]
    private const double HistMax = 1.2;
    private const int GaussPoints = 120;      // Stützstellen der Gauß-Kurve
    private const int TextUpdateDivider = 5;  // Texte mit 10 Hz statt 50 Hz

    // ---------------------------------------------------------------------
    // Eingefrorene WPF-Brushes für die Badges (wie Stufe 1, Aufgabe 1.1)
    // ---------------------------------------------------------------------
    private static readonly SolidColorBrush OkBg = new(MediaColor.FromRgb(27, 77, 27));
    private static readonly SolidColorBrush OkFg = new(MediaColor.FromRgb(152, 251, 152));
    private static readonly SolidColorBrush AlarmBg = new(MediaColor.FromRgb(160, 30, 30));
    private static readonly SolidColorBrush AlarmFg = new(MediaColor.FromRgb(255, 120, 120));

    static MainWindow()
    {
        OkBg.Freeze(); OkFg.Freeze(); AlarmBg.Freeze(); AlarmFg.Freeze();
    }

    // ScottPlot-Farben sind Structs -> Zuweisen erzeugt keine Heap-Objekte
    private static readonly SpColor BandColor = SpColors.Orange;
    private static readonly SpColor BandAlarmColor = SpColors.Crimson;
    private static readonly SpColor MarkerOkColor = SpColors.Yellow;
    private static readonly SpColor MarkerAlarmColor = SpColors.Crimson;

    // ---------------------------------------------------------------------
    // Modell, Statistik, Alarmlogik
    // ---------------------------------------------------------------------
    private readonly TestBenchModel _model = new();
    private readonly WelfordStatistics _welfordN = new();   // Drehzahl (nur Anzeige)
    private readonly WelfordStatistics _welfordM = new();   // Drehmoment -> ±3σ-Band (A.1)
    private readonly WelfordStatistics _welfordA = new();   // Vibration -> Band + Gauß-Kurve (A.3)

    // "> 3 aufeinanderfolgende Ticks" laut A.1, Alarm danach 1 s sichtbar
    private readonly ViolationDetector _slipDetector = new(minConsecutiveTicks: 3, holdTicks: 50);
    private readonly ViolationDetector _vibDetector = new(minConsecutiveTicks: 3, holdTicks: 50);
    private readonly ViolationDetector _overloadDetector = new(minConsecutiveTicks: 3, holdTicks: 50);

    private readonly LiveHistogram _histogram = new(HistBins, HistMin, HistMax);

    // ---------------------------------------------------------------------
    // Panel 1: Zeitbereich
    // ---------------------------------------------------------------------
    // (nicht readonly, weil sie in den Setup-Methoden zugewiesen werden)
    private DataStreamer _speedStreamer = null!;    // n(t), linke Achse
    private DataStreamer _torqueStreamer = null!;   // M(t), rechte Achse
    private HorizontalLine _mMean = null!;
    private HorizontalLine _mUpper = null!;
    private HorizontalLine _mLower = null!;
    private Annotation _slipWarning = null!;        // Warn-Marker bei Schlupf

    // ---------------------------------------------------------------------
    // Panel 2: Kennfeld
    // ---------------------------------------------------------------------
    private readonly double[] _trailX = new double[TrailLength];   // vorallokiert, wird als Ring beschrieben
    private readonly double[] _trailY = new double[TrailLength];
    private int _trailIndex;
    private Marker _operatingPoint = null!;

    // ---------------------------------------------------------------------
    // Panel 3: Histogramm
    // ---------------------------------------------------------------------
    private readonly Bar[] _bars = new Bar[HistBins];   // Bar ist eine Klasse -> .Value direkt änderbar
    private readonly double[] _gaussX = new double[GaussPoints];
    private readonly double[] _gaussY = new double[GaussPoints];

    // ---------------------------------------------------------------------
    // Panel 4 + Laufzeit
    // ---------------------------------------------------------------------
    private DrivetrainGraph? _graph;   // wird erst im Loaded-Event erzeugt (MSAGL braucht ein geladenes Panel)
    private readonly DispatcherTimer _timer;
    private int _tickCounter;

    public MainWindow()
    {
        InitializeComponent();

        SetupTimePanel();
        SetupCharacteristicPanel();
        SetupHistogramPanel();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };   // 50 Hz
        _timer.Tick += OnTick;

        // MSAGL-Graph erst aufbauen, wenn das Fenster geladen ist, dann Timer starten
        Loaded += (_, _) =>
        {
            _graph = new DrivetrainGraph(GraphHost);
            _timer.Start();
        };
    }

    // =====================================================================
    // Einmaliger Aufbau der Panels
    // =====================================================================

    /// <summary>A.1: n(t) links, M(t) rechts, Welford-Band für M auf der rechten Achse.</summary>
    private void SetupTimePanel()
    {
        Plot p = Plot1.Plot;
        ApplyDarkStyle(p, "A.1 Zeitbereich: Drehzahl & Drehmoment", "Abtastpunkte (50 Hz)", "Drehzahl n [U/min]");
        p.Axes.Right.Label.Text = "Drehmoment M [Nm]";

        // Drehzahl auf der linken Achse (Standard)
        _speedStreamer = p.Add.DataStreamer(StreamCapacity);
        _speedStreamer.ViewScrollLeft();
        _speedStreamer.Color = SpColors.DeepSkyBlue;
        _speedStreamer.LegendText = "n(t)";

        // Drehmoment auf der rechten Achse
        _torqueStreamer = p.Add.DataStreamer(StreamCapacity);
        _torqueStreamer.ViewScrollLeft();
        _torqueStreamer.Color = SpColors.LimeGreen;
        _torqueStreamer.LegendText = "M(t)";
        _torqueStreamer.Axes.YAxis = p.Axes.Right;

        // M̄ ± 3σ_M – die Linien müssen ebenfalls auf die rechte Achse!
        _mMean = p.Add.HorizontalLine(TestBenchModel.NominalTorque);
        _mMean.Color = SpColors.Yellow;
        _mMean.LineWidth = 1.5f;
        _mMean.Axes.YAxis = p.Axes.Right;

        _mUpper = p.Add.HorizontalLine(TestBenchModel.NominalTorque + 1);
        _mLower = p.Add.HorizontalLine(TestBenchModel.NominalTorque - 1);
        foreach (HorizontalLine line in new[] { _mUpper, _mLower })   // einmalig beim Aufbau, nicht im Tick
        {
            line.Color = BandColor;
            line.LineWidth = 1.5f;
            line.LinePattern = LinePattern.Dashed;
            line.Axes.YAxis = p.Axes.Right;
        }

        // Warn-Marker: unsichtbar, wird bei Schlupf eingeblendet
        _slipWarning = p.Add.Annotation("SCHLUPF: M außerhalb ±3σ", Alignment.UpperRight);
        _slipWarning.LabelFontColor = SpColors.White;
        _slipWarning.LabelBackgroundColor = SpColors.Crimson;
        _slipWarning.LabelBorderColor = SpColors.Crimson;
        _slipWarning.LabelFontSize = 14;
        _slipWarning.LabelBold = true;
        _slipWarning.IsVisible = false;

        p.Axes.SetLimitsY(0, 3200);                 // linke Achse (n)
        p.Axes.SetLimitsY(0, 10, p.Axes.Right);     // rechte Achse (M)
        p.ShowLegend(Alignment.UpperLeft);
    }

    /// <summary>A.2: Kennfeld M über n mit Leistungshyperbel M = P_max / (2π n/60).</summary>
    private void SetupCharacteristicPanel()
    {
        Plot p = Plot2.Plot;
        ApplyDarkStyle(p, "A.2 Betriebskennfeld M(n)", "Drehzahl n [U/min]", "Drehmoment M [Nm]");

        // Grenzhyperbel – einmalig berechnet, danach statisch
        const int hypPoints = 100;
        double[] hx = new double[hypPoints], hy = new double[hypPoints];
        for (int i = 0; i < hypPoints; i++)
        {
            hx[i] = 700.0 + i * (3200.0 - 700.0) / (hypPoints - 1);
            hy[i] = TestBenchModel.PMax / (2.0 * Math.PI * hx[i] / 60.0);
        }
        Scatter hyp = p.Add.ScatterLine(hx, hy);
        hyp.Color = SpColors.Crimson;
        hyp.LineWidth = 2;
        hyp.LinePattern = LinePattern.Dashed;
        hyp.LegendText = $"P_max = {TestBenchModel.PMax:F0} W";

        // Spur der letzten 3 s: Scatter hält eine REFERENZ auf die Arrays,
        //     im Tick werden nur die Inhalte überschrieben
        Array.Fill(_trailX, TestBenchModel.NominalSpeed);
        Array.Fill(_trailY, TestBenchModel.NominalTorque);
        Scatter trail = p.Add.ScatterPoints(_trailX, _trailY);
        trail.Color = SpColors.DeepSkyBlue.WithAlpha(0.35);
        trail.MarkerSize = 4;
        trail.LegendText = "Verlauf (3 s)";

        // Aktueller Arbeitspunkt
        _operatingPoint = p.Add.Marker(TestBenchModel.NominalSpeed, TestBenchModel.NominalTorque, MarkerShape.FilledSquare, 12);
        _operatingPoint.Color = MarkerOkColor;
        _operatingPoint.LegendText = "Arbeitspunkt";

        p.Axes.SetLimits(0, 3200, 0, 10);
        p.ShowLegend(Alignment.UpperRight);
    }

    /// <summary>A.3: Histogramm mit 20 Klassen + Gauß-Kurve aus Welford-Mittelwert und -Streuung.</summary>
    private void SetupHistogramPanel()
    {
        Plot p = Plot3.Plot;
        ApplyDarkStyle(p, "A.3 Vibrations-Histogramm", "Vibrationsamplitude a_vib [g]", "Häufigkeit");

        // Balken einmalig anlegen; im Tick wird nur Value gesetzt
        for (int i = 0; i < HistBins; i++)
        {
            _bars[i] = new Bar
            {
                Position = _histogram.BinCenter(i),
                Value = 0,
                Size = _histogram.BinWidth * 0.9,
                FillColor = SpColors.SteelBlue.WithAlpha(0.75),
                LineColor = SpColors.SteelBlue,
            };
        }
        p.Add.Bars(_bars);

        // Gauß-Kurve: x-Werte fix, y-Werte werden im Tick überschrieben
        for (int i = 0; i < GaussPoints; i++)
            _gaussX[i] = HistMin + i * (HistMax - HistMin) / (GaussPoints - 1);
        Scatter gauss = p.Add.ScatterLine(_gaussX, _gaussY);
        gauss.Color = SpColors.Crimson;
        gauss.LineWidth = 2;
        gauss.LegendText = "N(μ, σ²) · n · h";

        p.Axes.SetLimits(HistMin, HistMax, 0, 10);
        p.ShowLegend(Alignment.UpperRight);
    }

    private static void ApplyDarkStyle(Plot p, string title, string xLabel, string yLabel)
    {
        p.Title(title);
        p.Axes.Bottom.Label.Text = xLabel;
        p.Axes.Left.Label.Text = yLabel;
        p.FigureBackground.Color = SpColor.FromHex("#141414");
        p.DataBackground.Color = SpColor.FromHex("#1E1E1E");
        p.Axes.Color(SpColor.FromHex("#CCCCCC"));
        p.Grid.MajorLineColor = SpColor.FromHex("#2E2E2E");
        p.Legend.BackgroundColor = SpColor.FromHex("#2D2D30");
        p.Legend.FontColor = SpColor.FromHex("#CCCCCC");
        p.Legend.OutlineColor = SpColor.FromHex("#3F3F46");
    }

    // =====================================================================
    // 50-Hz-Tick
    // =====================================================================
    private void OnTick(object? sender, EventArgs e)
    {
        // --- 1. Neue Messwerte --------------------------------------------
        _model.Step();
        double n = _model.Speed;
        double m = _model.Torque;
        double a = _model.Vibration;
        double power = _model.Power;

        // --- 2. Statistik + Alarmlogik --------------------------------------
        // Erst prüfen, dann einspeisen: verletzende Werte gehen NICHT in Welford ein,
        // sonst würde jeder Fehler das Toleranzband aufblähen und sich selbst "maskieren".
        bool mViolated = _welfordM.IsAnomaly(m, 3.0);
        if (!mViolated) _welfordM.Add(m);
        bool slipChanged = _slipDetector.Update(mViolated);

        bool aViolated = _welfordA.IsAnomaly(a, 3.0);
        if (!aViolated) _welfordA.Add(a);
        bool vibChanged = _vibDetector.Update(aViolated);

        bool overloadChanged = _overloadDetector.Update(power > TestBenchModel.PMax);

        _welfordN.Add(n);

        // --- 3. Panel 1: Zeitbereich -----------------------------------------
        _speedStreamer.Add(n);
        _torqueStreamer.Add(m);
        if (_welfordM.Count > 10)
        {
            var (lower, upper) = _welfordM.GetToleranceBand(3.0);   // ValueTuple -> Stack
            _mMean.Y = _welfordM.Mean;
            _mUpper.Y = upper;
            _mLower.Y = lower;
        }

        // --- 4. Panel 2: Kennfeld --------------------------------------------
        _trailX[_trailIndex] = n;
        _trailY[_trailIndex] = m;
        _trailIndex = (_trailIndex + 1) % TrailLength;   // Ringpuffer-Prinzip auf den Plot-Arrays
        _operatingPoint.Location = new Coordinates(n, m);   // Coordinates ist ein Struct

        // --- 5. Panel 3: Histogramm + Gauß-Kurve ------------------------------
        _histogram.Add(a);
        UpdateHistogram();

        // --- 6. Alarmanzeigen nur bei Zustandswechsel -------------------------
        if (slipChanged) ShowSlipAlarm(_slipDetector.IsAlarm);
        if (vibChanged) ShowVibrationAlarm(_vibDetector.IsAlarm);
        if (overloadChanged) ShowOverloadAlarm(_overloadDetector.IsAlarm);

        // --- 7. Zeichnen -------------------------------------------------------
        Plot1.Refresh();
        Plot2.Refresh();
        Plot3.Refresh();

        if (++_tickCounter % TextUpdateDivider == 0)
            UpdateFooter(n, m, power);
    }

    /// <summary>Balkenhöhen setzen und Gauß-Dichte auf die Histogrammfläche n·h skalieren.</summary>
    private void UpdateHistogram()
    {
        for (int i = 0; i < HistBins; i++)
            _bars[i].Value = _histogram.Counts[i];

        double mu = _welfordA.Mean;
        double sigma = _welfordA.StdDev;
        double area = _histogram.Total * _histogram.BinWidth;   // Fläche aller Balken = n · h
        double peak = 0.0;

        if (sigma > 1e-9)
        {
            double norm = 1.0 / (sigma * Math.Sqrt(2.0 * Math.PI));
            for (int i = 0; i < GaussPoints; i++)
            {
                double z = (_gaussX[i] - mu) / sigma;
                _gaussY[i] = area * norm * Math.Exp(-0.5 * z * z);   // pdf · n · h
            }
            peak = area * norm;
        }

        // Y-Achse mitwachsen lassen (Zähler steigen unbegrenzt)
        double top = Math.Max(10.0, Math.Max(_histogram.MaxCount, peak) * 1.15);
        Plot3.Plot.Axes.SetLimitsY(0, top);
    }

    // =====================================================================
    // Alarmanzeigen (nur bei Zustandswechsel aufgerufen)
    // =====================================================================
    private void ShowSlipAlarm(bool alarm)
    {
        SetBadge(BadgeSlip, TxtBadgeSlip, alarm, alarm ? $"SCHLUPF #{_slipDetector.EventCount}" : "MOMENT OK");
        _slipWarning.IsVisible = alarm;
        _mUpper.Color = alarm ? BandAlarmColor : BandColor;
        _mLower.Color = alarm ? BandAlarmColor : BandColor;
        _graph?.SetAlarm(SensorId.Torque, alarm);
    }

    private void ShowVibrationAlarm(bool alarm)
    {
        SetBadge(BadgeVib, TxtBadgeVib, alarm, alarm ? $"LAGERSCHWINGUNG #{_vibDetector.EventCount}" : "VIBRATION OK");
        _graph?.SetAlarm(SensorId.Vibration, alarm);
    }

    private void ShowOverloadAlarm(bool alarm)
    {
        SetBadge(BadgeOverload, TxtBadgeOverload, alarm, alarm ? $"ÜBERLAST P > P_max #{_overloadDetector.EventCount}" : "LEISTUNG OK");
        _operatingPoint.Color = alarm ? MarkerAlarmColor : MarkerOkColor;
        _graph?.SetAlarm(SensorId.Speed, alarm);
    }

    private static void SetBadge(Border badge, TextBlock text, bool alarm, string label)
    {
        badge.Background = alarm ? AlarmBg : OkBg;   // nur Referenzen auf eingefrorene Brushes
        text.Foreground = alarm ? AlarmFg : OkFg;
        text.Text = label;
    }

    private void UpdateFooter(double n, double m, double power)
    {
        TxtFooter.Text =
            $"n = {n,6:F0} U/min   M = {m,5:F2} Nm   P = {power,5:F0} W ({power / TestBenchModel.PMax,4:P0} P_max)   |   " +
            $"M̄ = {_welfordM.Mean:F2} ± 3·{_welfordM.StdDev:F3} Nm   σ_a = {_welfordA.StdDev:F3} g   |   " +
            $"Ereignisse: Schlupf {_slipDetector.EventCount}, Vibration {_vibDetector.EventCount}, Überlast {_overloadDetector.EventCount}   |   k = {_welfordN.Count:N0}";
    }

    // =====================================================================
    // Buttons
    // =====================================================================
    private void BtnStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.IsEnabled) { _timer.Stop(); BtnStartStop.Content = "Fortsetzen"; }
        else { _timer.Start(); BtnStartStop.Content = "Pause"; }
    }

    private void BtnSlip_Click(object sender, RoutedEventArgs e) => _model.InjectSlip();
    private void BtnBearing_Click(object sender, RoutedEventArgs e) => _model.InjectBearingDamage();
    private void BtnOverload_Click(object sender, RoutedEventArgs e) => _model.InjectOverload();

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _model.Reset();
        _welfordN.Reset(); _welfordM.Reset(); _welfordA.Reset();
        _slipDetector.Reset(); _vibDetector.Reset(); _overloadDetector.Reset();
        _histogram.Reset();

        _speedStreamer.Clear();
        _torqueStreamer.Clear();
        Array.Fill(_trailX, TestBenchModel.NominalSpeed);
        Array.Fill(_trailY, TestBenchModel.NominalTorque);
        Array.Clear(_gaussY);
        _trailIndex = 0;
        _tickCounter = 0;

        ShowSlipAlarm(false);
        ShowVibrationAlarm(false);
        ShowOverloadAlarm(false);
        UpdateHistogram();

        Plot1.Refresh(); Plot2.Refresh(); Plot3.Refresh();
        TxtFooter.Text = "Zurückgesetzt.";
    }
}
