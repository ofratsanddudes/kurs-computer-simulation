namespace PixelHeatFDM.Simulation;

/// <summary>
/// Randbedingungs-Modus für das 2D-Temperaturgitter.
/// </summary>
public enum BoundaryMode
{
    /// <summary>Feste Randtemperatur (Dirichlet-Randbedingung, z.B. 20 °C)</summary>
    DirichletFixed,
    /// <summary>Vollkommen isolierter Rand ohne Wärmestrom (Neumann-Randbedingung dT/dn = 0)</summary>
    NeumannAdiabatic
}

/// <summary>
/// 2D-Temperaturgitter mit FDM-Laplace-Stern (5-Punkt-Schablone) zur Lösung
/// der instationären Wärmeleitungsgleichung dT/dt = alpha * Laplace(T).
/// Erweitert um ein ortsabhängiges Materialgitter (alpha je Zelle) und innere Isolatorwände.
/// </summary>
public class HeatGrid
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Gitterabstand Delta x = Delta y in Metern [m].</summary>
    public float Dx { get; set; } = 0.001f;

    /// <summary>
    /// Temperaturleitfähigkeit alpha = lambda / (rho * c_p) in [m^2/s] für normales Material.
    /// Beim Setzen werden alle Nicht-Wand-Zellen der _alphaMap nachgeführt, damit der
    /// Slider (und die Tests) weiterhin wirken.
    /// </summary>
    public float Alpha
    {
        get => _alpha;
        set
        {
            _alpha = value;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (_alphaMap[x, y] != 0f)      // Wandzellen (alpha = 0) bleiben Wand
                        _alphaMap[x, y] = value;
        }
    }

    /// <summary>Zeitschrittweite Delta t in Sekunden [s].</summary>
    public float Dt { get; set; } = 0.002f;

    /// <summary>Aktuelle Randbedingung an den Außenkanten.</summary>
    public BoundaryMode Boundary { get; set; } = BoundaryMode.DirichletFixed;

    /// <summary>Festgelegte Randtemperatur für Dirichlet-Randbedingungen [°C].</summary>
    public float AmbientTemperature { get; set; } = 20.0f;

    /// <summary>Zentraler Hotspot aktiv halten (Dauer-Wärmequelle).</summary>
    public bool MaintainHotspot { get; set; } = true;

    private float _alpha = 1.0e-4f;

    // Ping-Pong-Puffer: _tCurrent nur lesen, _tNext nur schreiben, am Ende tauschen
    private float[,] _tCurrent;
    private float[,] _tNext;

    // Materialgitter: alpha je Zelle. alpha == 0 bedeutet Isolator-Wand.
    private readonly float[,] _alphaMap;

    /// <summary>
    /// CFL-Diffusionszahl für normales Material: s = alpha * dt / (dx^2).
    /// Für ein stabiles 2D-Verfahren MUSS s <= 0.25 (1/4) gelten!
    /// </summary>
    public float StabilityFactor => (Alpha * Dt) / (Dx * Dx);

    /// <summary>
    /// Prüft das Von-Neumann / CFL-Stabilitätskriterium.
    /// </summary>
    public bool IsStable => StabilityFactor <= 0.25f;

    /// <summary>
    /// Maximale theoretische Zeitschrittweite dt_krit = dx^2 / (4 * alpha).
    /// </summary>
    public float CriticalDt => (Dx * Dx) / (4.0f * Math.Max(Alpha, 1e-12f));

    public HeatGrid(int width = 128, int height = 128)
    {
        Width = width;
        Height = height;
        _tCurrent = new float[width, height];
        _tNext = new float[width, height];
        _alphaMap = new float[width, height];   // muss vor Reset() existieren
        Reset();
    }

    /// <summary>
    /// Setzt das Gitter auf Umgebungstemperatur zurück, entfernt alle Wände
    /// und platziert den zentralen Hotspot.
    /// </summary>
    public void Reset()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                _tCurrent[x, y] = AmbientTemperature;
                _tNext[x, y] = AmbientTemperature;
                _alphaMap[x, y] = _alpha;       // überall wieder normales Material
            }
        }

        if (MaintainHotspot)
        {
            ApplyHotspot(_tCurrent);
        }
    }

    /// <summary>
    /// Erzeugt eine Wärmequelle (z. B. 100 °C) in der Gittermitte.
    /// Wandzellen werden übersprungen, damit der Hotspot keine Wände "überschreibt".
    /// </summary>
    public void ApplyHotspot(float[,] grid, float temp = 100.0f)
    {
        int midX = Width / 2;
        int midY = Height / 2;
        int radius = Math.Max(2, Width / 16);

        for (int y = midY - radius; y <= midY + radius; y++)
        {
            for (int x = midX - radius; x <= midX + radius; x++)
            {
                if (x >= 0 && x < Width && y >= 0 && y < Height && _alphaMap[x, y] != 0f)
                {
                    grid[x, y] = temp;
                }
            }
        }
    }

    /// <summary>
    /// Führt einen FDM-Zeitschritt über den 5-Punkt-Differenzenstern aus.
    /// T^{k+1} = T^k + s_xy * (T_rechts + T_links + T_unten + T_oben - 4*T), mit s_xy = alpha_xy * dt / dx^2.
    /// An Wand-Nachbarn wird die eigene Temperatur gespiegelt (lokale Neumann-Bedingung, kein Wärmestrom).
    /// </summary>
    public void Step()
    {
        float dtOverDx2 = Dt / (Dx * Dx);

        // FDM-Berechnung für innere Gitterpunkte (1 .. Width-2, 1 .. Height-2)
        for (int y = 1; y < Height - 1; y++)
        {
            for (int x = 1; x < Width - 1; x++)
            {
                float a = _alphaMap[x, y];
                float tCenter = _tCurrent[x, y];

                // Wandzelle: Temperatur unverändert übernehmen.
                // Wichtig wegen Ping-Pong: sonst stünde in _tNext ein veralteter Wert.
                if (a == 0f)
                {
                    _tNext[x, y] = tCenter;
                    continue;
                }

                // Nachbarn lesen. Ist ein Nachbar eine Wand, nehmen wir die EIGENE Temperatur:
                // Delta T über diese Kante = 0  ->  Wärmestrom = 0  ->  adiabate Wand.
                // (Ohne Spiegelung würde die Wand als "Kühlakku" Wärme abziehen.)
                float tRight = _alphaMap[x + 1, y] == 0f ? tCenter : _tCurrent[x + 1, y];
                float tLeft = _alphaMap[x - 1, y] == 0f ? tCenter : _tCurrent[x - 1, y];
                float tDown = _alphaMap[x, y + 1] == 0f ? tCenter : _tCurrent[x, y + 1];
                float tUp = _alphaMap[x, y - 1] == 0f ? tCenter : _tCurrent[x, y - 1];

                // 5-Punkt-Stern (Laplace-Zähler)
                float laplace = tRight + tLeft + tDown + tUp - 4.0f * tCenter;

                // Lokale Diffusionszahl s_xy = alpha_xy * dt / dx^2
                _tNext[x, y] = tCenter + a * dtOverDx2 * laplace;
            }
        }

        // Randbedingungen anwenden
        ApplyBoundaryConditions(_tNext);

        if (MaintainHotspot)
        {
            ApplyHotspot(_tNext);
        }

        // Puffer tauschen (Pointer-Swap, 0 GC-Allokationen)
        (_tCurrent, _tNext) = (_tNext, _tCurrent);
    }

    private void ApplyBoundaryConditions(float[,] grid)
    {
        if (Boundary == BoundaryMode.DirichletFixed)
        {
            // Feste Temperatur an den Rändern
            for (int x = 0; x < Width; x++)
            {
                grid[x, 0] = AmbientTemperature;
                grid[x, Height - 1] = AmbientTemperature;
            }
            for (int y = 0; y < Height; y++)
            {
                grid[0, y] = AmbientTemperature;
                grid[Width - 1, y] = AmbientTemperature;
            }
        }
        else if (Boundary == BoundaryMode.NeumannAdiabatic)
        {
            // Isolierter Rand (dT/dn = 0): Randwerte übernehmen den Wert des inneren Nachbarn
            for (int x = 0; x < Width; x++)
            {
                grid[x, 0] = grid[x, 1];
                grid[x, Height - 1] = grid[x, Height - 2];
            }
            for (int y = 0; y < Height; y++)
            {
                grid[0, y] = grid[1, y];
                grid[Width - 1, y] = grid[Width - 2, y];
            }
        }
    }

    /// <summary>
    /// Zeichnet mit einem kreisförmigen Pinsel ins laufende Gitter.
    /// insulator = true: Isolator-Wand (alpha = 0); false: heiße Quelle (100 °C, nur auf Nicht-Wand-Zellen).
    /// </summary>
    public void Paint(int gx, int gy, int radius, bool insulator)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dy * dy > radius * radius) continue;   // kreisförmig

                int x = gx + dx;
                int y = gy + dy;

                // Nur innere Zellen: der Rand wird von den Randbedingungen verwaltet
                if (x < 1 || x >= Width - 1 || y < 1 || y >= Height - 1) continue;

                if (insulator)
                {
                    _alphaMap[x, y] = 0f;
                }
                else if (_alphaMap[x, y] != 0f)
                {
                    _tCurrent[x, y] = 100.0f;
                }
            }
        }
    }

    /// <summary>True, wenn die Zelle eine Isolator-Wand ist (alpha == 0).</summary>
    public bool IsInsulator(int x, int y) => _alphaMap[x, y] == 0f;

    /// <summary>
    /// Berechnet die thermische Summe / Energiebilanz im Gitter (zu Test- und Validierungszwecken).
    /// </summary>
    public double CalculateTotalHeatSum()
    {
        double sum = 0.0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                sum += _tCurrent[x, y];
            }
        }
        return sum;
    }

    public float GetTemperature(int x, int y) => _tCurrent[x, y];
    public void SetTemperature(int x, int y, float value) => _tCurrent[x, y] = value;

    public float[,] GetCurrentGrid() => _tCurrent;

    // =========================================================================
    // TODO: [Stufe B Track A: Industrie - CPU-Kühlkörper-Optimizer]
    // - Ortsabhängiges Materialgitter mit Alpha(x, y) (Si-Die, TIM, Kupfer, Alu-Finnen)
    // - Verlustleistungs-Quellterm dot_q_v im CPU-Die
    // - Robin-Randbedingungen: Lüfterbetrieb (h = 250) vs. Lüfterausfall (h = 20)
    // =========================================================================

    // =========================================================================
    // TODO: [Stufe B Track B: Simulation Game - Falling Sand / Doom Fire]
    // - Zelluläre Automaten-Partikel oder Konvektionseffekte
    // =========================================================================
}