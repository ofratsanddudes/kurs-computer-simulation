using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlotTelemetry.Data;
using ScottPlotTelemetry.Statistics;

// Alias nötig: "Color" existiert sowohl in ScottPlot als auch in System.Windows.Media
using MediaColor = System.Windows.Media.Color;

namespace ScottPlotTelemetry;

/// <summary>
/// Stufe 1: 50-Hz-Telemetrie-Leitstand mit Welford-Statistik, gleitendem 10-s-Fenster,
/// entprellter 3-Sigma-Anomalie-Erkennung und Referenzkanal.
/// </summary>
public partial class MainWindow : Window
{
    // ---------------------------------------------------------------------
    // Konstanten
    // ---------------------------------------------------------------------
    private const int BufferCapacity = 500;      // 500 Werte · 20 ms = 10 s Zeitfenster
    private const int AnomalyHoldTicks = 25;     // 25 Ticks · 20 ms = 0,5 s Haltezeit fürs Badge
    private const int TextUpdateDivider = 5;     // Texte nur jeden 5. Tick -> 10 Hz statt 50 Hz

    // ---------------------------------------------------------------------
    // Aufgabe 1.1 – GC-Leak-Fix
    // Die Vorlage hat in jedem Tick "new SolidColorBrush(...)" erzeugt -> 50 Objekte/s
    // auf dem Heap, die der Garbage Collector später wieder einsammeln muss.
    // Lösung: Brushes EINMAL beim Laden der Klasse anlegen und nur noch referenzieren.
    // ---------------------------------------------------------------------
    private static readonly SolidColorBrush BadgeOkBg = new(MediaColor.FromRgb(27, 77, 27));
    private static readonly SolidColorBrush BadgeOkFg = new(MediaColor.FromRgb(152, 251, 152));
    private static readonly SolidColorBrush BadgeAlarmBg = new(MediaColor.FromRgb(160, 30, 30));
    private static readonly SolidColorBrush BadgeAlarmFg = new(MediaColor.FromRgb(255, 120, 120));

    // Statischer Konstruktor: läuft genau einmal, bevor die Klasse das erste Mal benutzt wird
    static MainWindow()
    {
        // Freeze() macht den Brush unveränderlich:
        // - threadsicher (darf von jedem Thread gelesen werden)
        // - WPF muss keine Änderungs-Benachrichtigungen mehr überwachen -> schneller
        BadgeOkBg.Freeze();
        BadgeOkFg.Freeze();
        BadgeAlarmBg.Freeze();
        BadgeAlarmFg.Freeze();
    }

    // ---------------------------------------------------------------------
    // Datenhaltung
    // ---------------------------------------------------------------------
    private readonly CircularBuffer<double> _buffer = new(BufferCapacity);  // Kurzzeit-Gedächtnis (letzte 10 s)
    private readonly WelfordStatistics _welford = new();                   // Langzeit-Gedächtnis (alle Werte, nur 3 Zahlen)

    // Aufgabe 1.2: Zielarray für CopyTo – einmalig angelegt, im Tick wird nur überschrieben
    private readonly double[] _windowCopy = new double[BufferCapacity];

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new(42);   // fester Seed -> reproduzierbares Rauschen

    // ---------------------------------------------------------------------
    // ScottPlot-Objekte (werden einmal angelegt, im Tick nur mit Daten gefüttert)
    // ---------------------------------------------------------------------
    private readonly DataStreamer _streamer;            // verrauschtes Messsignal
    private readonly DataStreamer _referenceStreamer;   // Aufgabe 1.3: ideales Referenzsignal
    private readonly HorizontalLine _meanLine;          // Welford-Mittelwert
    private readonly HorizontalLine _upperLine;         // Mittelwert + 3 Sigma
    private readonly HorizontalLine _lowerLine;         // Mittelwert - 3 Sigma

    private double _simTime;            // Simulationszeit in Sekunden
    private bool _injectAnomalyFlag;    // wird vom Button gesetzt, im nächsten Tick verbraucht

    // ---------------------------------------------------------------------
    // Aufgabe 1.3 – Entprellung
    // ---------------------------------------------------------------------
    private int _anomalyHold;       // Rest-Ticks, die das Badge noch rot bleibt (0 = kein Alarm)
    private long _anomalyCount;     // Gesamtzahl erkannter Anomalie-EREIGNISSE
    private bool _alarmShown;       // aktuell angezeigter Badge-Zustand (für Wechsel-Erkennung)
    private int _tickCounter;       // für die gedrosselte Textaktualisierung

    public MainWindow()
    {
        InitializeComponent();

        // Titel und Achsenbeschriftung (ScottPlot-5-API: Plot.Axes...)
        Plot1.Plot.Title("Echtzeit-Signaltelemetrie & Dynamisches 3-Sigma-Toleranzband");
        Plot1.Plot.Axes.Left.Label.Text = "Amplitude [Einheit]";
        Plot1.Plot.Axes.Bottom.Label.Text = "Abtastpunkte";

        // Dunkles Farbschema passend zum restlichen UI
        Plot1.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#141414");
        Plot1.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#1E1E1E");
        Plot1.Plot.Axes.Color(ScottPlot.Color.FromHex("#CCCCCC"));
        Plot1.Plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#2E2E2E");

        // Aufgabe 1.3: Referenzkanal ZUERST hinzufügen.
        // ScottPlot zeichnet in Hinzufüge-Reihenfolge (Painter's Algorithm),
        // die Referenz liegt dadurch unter dem Messsignal.
        _referenceStreamer = Plot1.Plot.Add.DataStreamer(BufferCapacity);  // Ringpuffer mit 500 Plätzen
        _referenceStreamer.ViewScrollLeft();                               // Rollband: neuester Wert rechts
        _referenceStreamer.Color = ScottPlot.Colors.Orange;
        _referenceStreamer.LineWidth = 2;
        _referenceStreamer.LegendText = "Referenz y_ref(t)";

        // Messsignal
        _streamer = Plot1.Plot.Add.DataStreamer(BufferCapacity);
        _streamer.ViewScrollLeft();
        _streamer.Color = ScottPlot.Color.FromHex("#00D2FF");
        _streamer.LegendText = "Messsignal y(t)";

        // Welford-Linien mit Startwerten; im Tick wird nur noch .Y verschoben
        _meanLine = Plot1.Plot.Add.HorizontalLine(100.0);
        _meanLine.Color = ScottPlot.Colors.Yellow;
        _meanLine.LineWidth = 1.5f;

        _upperLine = Plot1.Plot.Add.HorizontalLine(115.0);
        _upperLine.Color = ScottPlot.Colors.Red;
        _upperLine.LineWidth = 1.5f;
        _upperLine.LinePattern = LinePattern.Dashed;

        _lowerLine = Plot1.Plot.Add.HorizontalLine(85.0);
        _lowerLine.Color = ScottPlot.Colors.Red;
        _lowerLine.LineWidth = 1.5f;
        _lowerLine.LinePattern = LinePattern.Dashed;

        // Legende im dunklen Stil (optional – bei Compilerfehler einfach löschen)
        Plot1.Plot.ShowLegend(Alignment.UpperLeft);
        Plot1.Plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#2D2D30");
        Plot1.Plot.Legend.FontColor = ScottPlot.Color.FromHex("#CCCCCC");
        Plot1.Plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#3F3F46");

        // Feste Y-Achse, damit das Bild beim Streamen nicht springt
        Plot1.Plot.Axes.SetLimitsY(50, 150);

        // UI-Timer: 20 ms -> 50 Hz. Läuft im UI-Thread, darf also direkt Controls ändern.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _timer.Tick += OnTelemetryTick;
        _timer.Start();
    }

    /// <summary>
    /// Wird 50-mal pro Sekunde aufgerufen. Ziel: keine Heap-Allokation in diesem Pfad
    /// (Ausnahme: Strings für die Textfelder, siehe UpdateTexts).
    /// </summary>
    private void OnTelemetryTick(object? sender, EventArgs e)
    {
        _simTime += 0.02;   // ein Tick = 20 ms

        // --- 1. Signal erzeugen ---------------------------------------------
        // Ideales Referenzsignal: y_ref(t) = 100 + 15 · sin(2π · 0,5 Hz · t)
        double reference = 100.0 + 15.0 * Math.Sin(2.0 * Math.PI * 0.5 * _simTime);

        // Rauschen aus der Vorlage: gleichverteilt in [-3; +3] (NICHT normalverteilt!)
        double noise = (_random.NextDouble() - 0.5) * 6.0;
        double signal = reference + noise;

        // Button "Anomalie injizieren": einmaliger Peak von +45
        if (_injectAnomalyFlag)
        {
            signal += 45.0;
            _injectAnomalyFlag = false;
        }

        // --- 2. Daten verteilen (alles O(1), kein new) -----------------------
        _buffer.Add(signal);                 // Ringpuffer überschreibt ältesten Wert
        _welford.Add(signal);                // Mean und M2 rekursiv nachführen
        _streamer.Add(signal);               // ScottPlot-interner Ringpuffer
        _referenceStreamer.Add(reference);   // 1.3: zweiter Kanal synchron

        // --- 3. Aufgabe 1.2: Gleitendes 10-s-Fenster auswerten -------------------
        // CopyTo schreibt chronologisch in das vorallokierte Array.
        // Bewusst for statt foreach: der yield-Enumerator von CircularBuffer
        // wäre ein neues Objekt auf dem Heap.
        _buffer.CopyTo(_windowCopy);
        int n = _buffer.Count;   // < 500 in den ersten 10 s, danach immer 500

        double sum = 0.0;
        double wMin = double.PositiveInfinity;
        double wMax = double.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            double v = _windowCopy[i];
            sum += v;
            if (v < wMin) wMin = v;
            if (v > wMax) wMax = v;
        }
        // Naive Summe ist hier ok: Auslöschung betrifft nur die Varianzformel, nicht den Mittelwert
        double wMean = sum / n;

        // --- 4. Toleranzband und Anomalie-Erkennung --------------------------
        // Erst ab 10 Werten, vorher ist Sigma statistisch noch nicht aussagekräftig
        if (_welford.Count > 10)
        {
            // Linien nur verschieben – keine neuen Plot-Objekte
            _meanLine.Y = _welford.Mean;
            var (lower, upper) = _welford.GetToleranceBand(3.0);   // ValueTuple -> liegt auf dem Stack
            _upperLine.Y = upper;
            _lowerLine.Y = lower;

            // Aufgabe 1.3: Entprellung
            if (_welford.IsAnomaly(signal, 3.0))
            {
                // Nur zählen, wenn gerade KEIN Alarm aktiv ist -> ein Ereignis = +1,
                // auch wenn es mehrere Ticks dauert
                if (_anomalyHold == 0) _anomalyCount++;

                // Haltezeit (neu) starten: Badge bleibt mind. 0,5 s rot
                _anomalyHold = AnomalyHoldTicks;
            }
            else if (_anomalyHold > 0)
            {
                _anomalyHold--;   // Haltezeit ablaufen lassen
            }

            SetAlarmBadge(_anomalyHold > 0);
        }

        // --- 5. Anzeige ------------------------------------------------------
        // Textfelder nur jeden 5. Tick: 10 Hz reicht zum Ablesen und spart 80 % der String-Allokationen
        if (++_tickCounter % TextUpdateDivider == 0)
        {
            UpdateTexts(signal, n, wMean, wMin, wMax);
        }

        // Plot neu zeichnen (einmal pro Tick = 50 FPS)
        Plot1.Refresh();
    }

    /// <summary>
    /// Setzt Badge-Farbe und -Text. Nur bei Zustandswechsel aktiv,
    /// damit WPF nicht 50-mal pro Sekunde unnötig neu layoutet.
    /// </summary>
    private void SetAlarmBadge(bool alarm)
    {
        if (alarm == _alarmShown) return;   // nichts geändert -> nichts tun
        _alarmShown = alarm;

        // Nur Referenzen auf die eingefrorenen Brushes zuweisen -> 0 Byte
        StatusBadge.Background = alarm ? BadgeAlarmBg : BadgeOkBg;
        TxtStatusBadge.Foreground = alarm ? BadgeAlarmFg : BadgeOkFg;
        TxtStatusBadge.Text = alarm
            ? $"! ANOMALIE #{_anomalyCount} (> 3 SIGMA) !"
            : "SIGNAL IM NORMALBEREICH";   // String-Literal: wird nicht neu allokiert
    }

    /// <summary>
    /// Aktualisiert Seitenleiste und Footer. Interpolierte Strings ($"...")
    /// erzeugen immer neue String-Objekte – deshalb gedrosselt aufgerufen.
    /// </summary>
    private void UpdateTexts(double signal, int n, double wMean, double wMin, double wMax)
    {
        // Welford-Langzeitstatistik (Seitenleiste)
        TxtCount.Text = $"{_welford.Count:N0}";
        TxtCurrent.Text = $"{signal:F2}";
        TxtMean.Text = $"{_welford.Mean:F2}";
        TxtStdDev.Text = $"{_welford.StdDev:F2}";
        TxtVariance.Text = $"{_welford.Variance:F2}";

        if (_welford.Count > 10)
        {
            var (lower, upper) = _welford.GetToleranceBand(3.0);
            TxtToleranceBand.Text = $"[{lower:F1} ... {upper:F1}]";
        }

        // Aufgabe 1.2: Fensterstatistik im Vergleich zum globalen Welford-Mittelwert
        TxtFooter.Text =
            $"Fenster 10 s (n = {n}): Ø {wMean:F2} | Min {wMin:F2} | Max {wMax:F2}   ||   " +
            $"Welford global: Ø {_welford.Mean:F2}   |   Anomalien: {_anomalyCount}";
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

    /// <summary>
    /// Setzt alle Puffer, Statistiken, Zähler und Anzeigen zurück.
    /// </summary>
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        // Daten und Statistik
        _buffer.Clear();
        _welford.Reset();
        _streamer.Clear();
        _referenceStreamer.Clear();
        _simTime = 0.0;

        // Entprellung und Zähler (1.3)
        _anomalyHold = 0;
        _anomalyCount = 0;
        _tickCounter = 0;

        // Badge direkt zurücksetzen (auch Foreground – fehlte in der Vorlage)
        _alarmShown = false;
        StatusBadge.Background = BadgeOkBg;
        TxtStatusBadge.Foreground = BadgeOkFg;
        TxtStatusBadge.Text = "SIGNAL IM NORMALBEREICH";

        // Textfelder
        TxtCount.Text = "0";
        TxtCurrent.Text = "--.-";
        TxtMean.Text = "--.-";
        TxtStdDev.Text = "--.-";
        TxtVariance.Text = "--.-";
        TxtToleranceBand.Text = "[--.- ... --.-]";
        TxtFooter.Text = "Telemetrie aktiv. Abtastrate: 50 Hz (20 ms DispatcherTimer).";

        Plot1.Refresh();
    }

    private void BtnInjectAnomaly_Click(object sender, RoutedEventArgs e)
    {
        // Nur ein Flag setzen; der eigentliche Peak entsteht im nächsten Timer-Tick
        _injectAnomalyFlag = true;
    }
}