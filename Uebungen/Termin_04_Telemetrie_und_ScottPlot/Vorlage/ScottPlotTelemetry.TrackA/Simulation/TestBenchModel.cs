namespace ScottPlotTelemetry.TrackA.Simulation;

/// <summary>
/// Signalmodell des Motorprüfstands: liefert pro Tick Drehzahl n, Drehmoment M und Lagervibration a_vib.
/// Alle Werte sind Felder/Properties – Step() erzeugt keine Objekte (0 Byte Heap pro Tick).
/// </summary>
public sealed class TestBenchModel
{
    public const double Dt = 0.02;              // 50 Hz -> 20 ms pro Tick
    public const double PMax = 1200.0;          // thermische Dauerleistungsgrenze [W]
    public const double NominalSpeed = 1500.0;  // [U/min]
    public const double NominalTorque = 6.0;    // [Nm]

    private readonly Random _rnd = new(42);     // fester Seed -> reproduzierbar

    // Restdauer der injizierten Fehler in Ticks (0 = inaktiv)
    private int _slipTicks;
    private int _bearingTicks;
    private int _overloadTicks;
    private int _tick;

    public double Time { get; private set; }        // [s]
    public double Speed { get; private set; }       // n [U/min]
    public double Torque { get; private set; }      // M [Nm]
    public double Vibration { get; private set; }   // a_vib [g]

    /// <summary>Mechanische Leistung P = 2π · (n/60) · M  [W]  (n in U/min -> durch 60 teilen!)</summary>
    public double Power => 2.0 * Math.PI * (Speed / 60.0) * Torque;

    /// <summary>Schlupf: Kupplung rutscht -> Moment bricht ein, Drehzahl springt leicht hoch.</summary>
    public void InjectSlip() => _slipTicks = 15;            // 0,3 s

    /// <summary>Lagerschaden/Kavitation: periodische Stöße in der Vibration.</summary>
    public void InjectBearingDamage() => _bearingTicks = 100; // 2 s

    /// <summary>Überlast: Drehzahl deutlich über Nenndrehzahl -> P > P_max.</summary>
    public void InjectOverload() => _overloadTicks = 150;    // 3 s

    public void Step()
    {
        _tick++;
        Time += Dt;

        // Normalbetrieb: langsam schwankender Arbeitspunkt (0,05 Hz) + Gaußsches Messrauschen
        double n = NominalSpeed + 250.0 * Math.Sin(2.0 * Math.PI * 0.05 * Time) + NextGaussian(5.0);
        double m = NominalTorque + NextGaussian(0.15);
        double a = NextGaussian(0.2);

        if (_slipTicks > 0)
        {
            _slipTicks--;
            m -= 2.5;   // Momenteinbruch
            n += 80.0;  // Motor "dreht hoch", weil die Last wegrutscht
        }

        if (_bearingTicks > 0)
        {
            _bearingTicks--;
            a += (_tick % 2 == 0 ? 0.9 : -0.9);   // Stöße mit wechselndem Vorzeichen -> Mittelwert bleibt ~0
        }

        if (_overloadTicks > 0)
        {
            _overloadTicks--;
            n += 900.0;   // min. 2100 U/min · 6 Nm ≈ 1320 W > P_max
        }

        Speed = n;
        Torque = m;
        Vibration = a;
    }

    public void Reset()
    {
        _slipTicks = _bearingTicks = _overloadTicks = _tick = 0;
        Time = 0.0;
        Speed = NominalSpeed;
        Torque = NominalTorque;
        Vibration = 0.0;
    }

    /// <summary>
    /// Normalverteilte Zufallszahl (Box-Muller). Nötig, damit das Histogramm wirklich
    /// zur Gauß-Kurve passt – die Vorlage nutzt gleichverteiltes Rauschen.
    /// </summary>
    private double NextGaussian(double sigma)
    {
        double u1 = 1.0 - _rnd.NextDouble();   // (0, 1] -> verhindert Log(0)
        double u2 = _rnd.NextDouble();
        return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
