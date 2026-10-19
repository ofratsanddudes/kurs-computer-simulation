namespace HeatSinkOptimizer.Simulation;

/// <summary>
/// 200 x 200 Gitter eines CPU-Kühlkörper-Querschnitts (Track A).
/// Alle Felder sind flache 1D-Arrays, Index = y * Width + x (Row-Major, x innen).
/// Es werden nur Festkörperzellen gerechnet (Liste _active); Luft wirkt über die Robin-Kopplung.
/// </summary>
public class HeatSinkGrid
{
    public const int Width = 200;
    public const int Height = 200;

    /// <summary>Gitterweite Delta x = 0,2 mm, ergibt 40 x 40 mm Simulationsfläche.</summary>
    public const float Dx = 0.0002f;

    /// <summary>Umgebungs- und Starttemperatur, zugleich T_inf der Kühlluft [°C].</summary>
    public const float AmbientTemperature = 25.0f;

    /// <summary>Verlustleistung des CPU-Dies [W].</summary>
    public const float DiePowerW = 150.0f;

    /// <summary>
    /// Modellannahme: Tiefe senkrecht zur Bildebene [m]. Nötig, um aus der Leistung [W]
    /// eine Volumenleistung [W/m^3] zu machen (das Gitter ist nur 2D). Gehört in die README.
    /// </summary>
    public const float Depth = 0.04f;

    // ---- Geometrie in Zellen (x nach rechts, y nach unten, y = 199 ist die PCB-Unterkante) ----
    private const int DieX = 80, DieY = 190, DieW = 40, DieH = 10;
    private const int TimX = 80, TimY = 188, TimW = 40, TimH = 2;
    private const int CuX = 40, CuY = 168, CuW = 120, CuH = 20;
    private const int FinCount = 10;
    private const int FinW = 6, FinPitch = 12;
    private const int FinX0 = 43, FinY = 10, FinH = 158;

    private readonly Material[] _material = new Material[Width * Height];
    private readonly float[] _lambda = new float[Width * Height];   // lambda je Zelle [W/(m*K)]
    private readonly float[] _rhoCp = new float[Width * Height];    // rho * c_p je Zelle [J/(m^3*K)]
    private readonly byte[] _airFaces = new byte[Width * Height];   // Anzahl Kanten zur Luft (0..4)

    // Vorberechnete Kopplungskoeffizienten pro Zelle [1/s]: k = lambda_face / (rho*c_p*dx^2)
    private readonly float[] _kR = new float[Width * Height];
    private readonly float[] _kL = new float[Width * Height];
    private readonly float[] _kU = new float[Width * Height];
    private readonly float[] _kD = new float[Width * Height];
    private readonly float[] _kAir = new float[Width * Height];     // Robin-Kopplung an T_inf [1/s]
    private readonly float[] _qSrc = new float[Width * Height];     // Quellterm q_v / (rho*c_p) [K/s]

    private int[] _active = Array.Empty<int>();    // Indizes aller Festkörperzellen (aufsteigend)
    private int[] _dieCells = Array.Empty<int>();  // Indizes der Silizium-Zellen

    // Ping-Pong-Puffer: _tCurrent nur lesen, _tNext nur schreiben, am Ende tauschen
    private float[] _tCurrent = new float[Width * Height];
    private float[] _tNext = new float[Width * Height];

    /// <summary>Größtes alpha aller Festkörper (Kupfer), nur zur Anzeige von s = alpha*dt/dx^2.</summary>
    public float MaxAlpha { get; private set; }

    /// <summary>Kritischer Zeitschritt [s]: dt_krit = 1 / max_i (Summe der Kopplungen von Zelle i).</summary>
    public float CriticalDt { get; private set; }

    /// <summary>Aktueller Zeitschritt [s].</summary>
    public float Dt { get; set; }

    /// <summary>Wärmeübergangskoeffizient der Kühlluft [W/(m^2*K)].</summary>
    public float ConvectionH { get; private set; }

    /// <summary>Stabil, wenn dt nicht über dt_krit liegt.</summary>
    public bool IsStable => Dt <= CriticalDt;

    public HeatSinkGrid()
    {
        BuildGeometry();
        BuildCoupling();
        SetConvection(250.0f);          // Lüfter aktiv
        Dt = 0.9f * CriticalDt;
        Reset();
    }

    public static int Index(int x, int y) => y * Width + x;

    public Material GetMaterial(int x, int y) => _material[Index(x, y)];

    public float[] GetCurrentGrid() => _tCurrent;

    /// <summary>Setzt alle Temperaturen auf Umgebungstemperatur zurück.</summary>
    public void Reset()
    {
        Array.Fill(_tCurrent, AmbientTemperature);
        Array.Fill(_tNext, AmbientTemperature);
    }

    /// <summary>Maximale Temperatur im CPU-Die [°C].</summary>
    public float MaxDieTemperature()
    {
        float max = float.NegativeInfinity;
        foreach (int i in _dieCells)
        {
            float t = _tCurrent[i];
            if (float.IsNaN(t)) return float.NaN;   // Math.Max würde NaN sonst verschlucken
            if (t > max) max = t;
        }
        return max;
    }

    /// <summary>
    /// Stellt den Wärmeübergang h ein (Lüfter an: 250, Lüfterausfall: 20) und
    /// berechnet die Robin-Kopplungen sowie dt_krit neu.
    /// </summary>
    public void SetConvection(float h)
    {
        ConvectionH = h;
        float dx2 = Dx * Dx;

        foreach (int i in _active)
        {
            // Robin: lambda_eff = lambda * h * dx / (lambda + h * dx) je Kante zur Luft
            float lam = _lambda[i];
            float lamEff = lam * h * Dx / (lam + h * Dx);
            _kAir[i] = _airFaces[i] * lamEff / (_rhoCp[i] * dx2);
        }

        // dt_krit: Das Eigengewicht (1 - dt * Summe k) darf nirgends negativ werden
        float maxSum = 0f;
        foreach (int i in _active)
        {
            float sum = _kR[i] + _kL[i] + _kU[i] + _kD[i] + _kAir[i];
            if (sum > maxSum) maxSum = sum;
        }
        CriticalDt = 1.0f / maxSum;
    }

    /// <summary>
    /// Ein FDM-Zeitschritt (explizit, Ping-Pong): liest nur _tCurrent, schreibt nur _tNext.
    /// Luftzellen werden nicht berechnet, sie sind über _kAir als Robin-Rand angebunden.
    /// </summary>
    public void Step()
    {
        float[] t = _tCurrent;
        float[] tn = _tNext;
        int n = t.Length;
        float dt = Dt;

        for (int a = 0; a < _active.Length; a++)
        {
            int i = _active[a];
            float tc = t[i];

            // Nachbarindizes absichern. Wo kein Nachbar existiert (Gitterrand), ist k = 0,
            // der Wert fließt also nicht in die Rechnung ein (adiabat, z.B. PCB-Unterseite).
            int iR = Math.Min(i + 1, n - 1);
            int iL = Math.Max(i - 1, 0);
            int iD = Math.Min(i + Width, n - 1);
            int iU = Math.Max(i - Width, 0);

            float flux = _kR[i] * (t[iR] - tc)
                       + _kL[i] * (t[iL] - tc)
                       + _kU[i] * (t[iU] - tc)
                       + _kD[i] * (t[iD] - tc)
                       + _kAir[i] * (AmbientTemperature - tc)   // Robin: Konvektion an die Luft
                       + _qSrc[i];                              // Heizleistung (nur Die-Zellen)

            tn[i] = tc + dt * flux;
        }

        // Puffer tauschen (Referenz-Swap, 0 GC-Allokationen)
        (_tCurrent, _tNext) = (_tNext, _tCurrent);
    }

    // ---------------------------------------------------------------------------------

    private void BuildGeometry()
    {
        Array.Fill(_material, Material.Air);

        FillRect(DieX, DieY, DieW, DieH, Material.Silicon);
        FillRect(TimX, TimY, TimW, TimH, Material.Tim);
        FillRect(CuX, CuY, CuW, CuH, Material.Copper);

        for (int i = 0; i < FinCount; i++)
            FillRect(FinX0 + i * FinPitch, FinY, FinW, FinH, Material.Aluminium);

        MaxAlpha = 0f;
        for (int i = 0; i < _material.Length; i++)
        {
            MaterialData d = Materials.Get(_material[i]);
            _lambda[i] = d.Lambda;
            _rhoCp[i] = d.RhoCp;

            if (_material[i] != Material.Air && d.Alpha > MaxAlpha)
                MaxAlpha = d.Alpha;
        }
    }

    /// <summary>Baut Zelllisten, Kopplungskoeffizienten und den Quellterm einmalig auf.</summary>
    private void BuildCoupling()
    {
        float dx2 = Dx * Dx;
        var active = new List<int>();
        var die = new List<int>();

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = Index(x, y);
                if (_material[i] == Material.Air) continue;

                active.Add(i);
                if (_material[i] == Material.Silicon) die.Add(i);

                float rc = _rhoCp[i] * dx2;
                int air = 0;

                // Am Gitterrand gibt es keinen Nachbarn: k = 0 (adiabat). Beim Die ist das die PCB-Unterkante.
                _kR[i] = x < Width - 1 ? Coupling(i, i + 1, rc, ref air) : 0f;
                _kL[i] = x > 0 ? Coupling(i, i - 1, rc, ref air) : 0f;
                _kD[i] = y < Height - 1 ? Coupling(i, i + Width, rc, ref air) : 0f;
                _kU[i] = y > 0 ? Coupling(i, i - Width, rc, ref air) : 0f;
                _airFaces[i] = (byte)air;
            }
        }

        _active = active.ToArray();
        _dieCells = die.ToArray();

        // Quellterm: q_v = P / (N * dx^2 * Tiefe), Temperaturrate = q_v / (rho * c_p) [K/s]
        float volume = _dieCells.Length * dx2 * Depth;
        float qv = DiePowerW / volume;
        foreach (int i in _dieCells)
            _qSrc[i] = qv / _rhoCp[i];
    }

    /// <summary>
    /// Kopplung zur Nachbarzelle j: harmonisch gemitteltes lambda_face / (rho*c_p*dx^2).
    /// Ist j Luft, wird die Kante nur gezählt (Robin-Rand, siehe SetConvection) und es gibt keine Festkörper-Kopplung.
    /// </summary>
    private float Coupling(int i, int j, float rc, ref int airFaces)
    {
        if (_material[j] == Material.Air)
        {
            airFaces++;
            return 0f;
        }

        float lamFace = 2.0f * _lambda[i] * _lambda[j] / (_lambda[i] + _lambda[j]);
        return lamFace / rc;
    }

    private void FillRect(int x0, int y0, int w, int h, Material m)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                _material[Index(x, y)] = m;
    }
}