using System.Globalization;
using KinematikEuler.Models;
using KinematikEuler.Output;
using KinematikEuler.Simulation;
using KinematikEuler.Solvers;
using System.Text;

namespace KinematikEuler;

internal class Program
{
    private const double G = 9.81; // Erdbeschleunigung [m/s^2]
    private const double TEnd = 0.30;   // 0,10 s reicht nicht bis zum Anschlag (siehe README)

    public static void Main(string[] args)
    {
        // Einheitliche Dezimalpunktausgabe (Punkt statt Komma)
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;

        Console.WriteLine("===============================================================");
        Console.WriteLine("  Termin 01: Kinematik, Zustandsraum & Expliziter Euler-Integrator");
        Console.WriteLine("  Kurs: Systemsimulation / Digitaler Zwilling (FH OÖ Campus Wels)");
        Console.WriteLine("===============================================================\n");

        Console.WriteLine("Wählen Sie einen Modus:");
        Console.WriteLine("  1: Stufe A (In-Class Sprint: Schiefer Wurf im Vakuum)");
        Console.WriteLine("  2: Stufe B (Track A: Hydraulikzylinder-Endlagendämpfung)");
        Console.WriteLine("  3: Stufe B (Track B: Artillery Duel mit Wind & Drag)");
        Console.Write("Auswahl [Standard: 1]: ");

        string? input = Console.ReadLine();
        char choice = string.IsNullOrWhiteSpace(input) ? '1' : input.Trim()[0];

        try
        {
            switch (choice)
            {
                case '2':
                    RunTrackA_HydraulicCylinder();
                    break;
                case '3':
                    RunTrackB_ArtilleryGame();
                    break;
                default:
                    RunSprint_BallisticTrajectory();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FEHLER]: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }

        // Tastaturpuffer leeren, falls noch ein Enter ansteht
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }

        Console.WriteLine("\nDrücken Sie eine beliebige Taste, um das Fenster zu schließen...");
        Console.ReadKey(true);
    }

    /// <summary>
    /// Stufe A: In-Class Sprint - Schiefer Wurf im Vakuum.
    /// Vergleich des expliziten Euler-Verfahrens mit der analytischen Lösung.
    /// </summary>
    public static void RunSprint_BallisticTrajectory()
    {
        Console.WriteLine("\n--- [Stufe A Sprint] Schiefer Wurf im Vakuum ---");

        // Physikalische Randbedingungen
        double v0 = 150.0;             // Abgangsgeschwindigkeit [m/s]
        double alphaDeg = 45.0;        // Abschusswinkel [Grad]
        double alphaRad = alphaDeg * Math.PI / 180.0;

        double vx0 = v0 * Math.Cos(alphaRad);
        double vy0 = v0 * Math.Sin(alphaRad);

        // Analytische Vakuumlösung
        double tAna = 2.0 * vy0 / G;
        double xAna = (v0 * v0 * Math.Sin(2.0 * alphaRad)) / G;
        double yMaxAna = (vy0 * vy0) / (2.0 * G);

        Console.WriteLine($"Parameter: v0 = {v0:F1} m/s, alpha = {alphaDeg:F1}°");
        Console.WriteLine($"Analytisch: t_Flug = {tAna:F3} s | x_Impact = {xAna:F2} m | y_Max = {yMaxAna:F2} m\n");

        double[] stepSizes = [0.1, 0.01];

        foreach (double dt in stepSizes)
        {
            var state = new KinematicState(Time: 0.0, X: 0.0, Y: 0.0, Vx: vx0, Vy: vy0);
            var previous = state;
            double yMaxNum = 0.0;

            while (state.Y >= 0.0)
            {
                if (state.Y > yMaxNum)
                    yMaxNum = state.Y;

                previous = state;
                state = EulerIntegrator.StepEuler(state, dt, _ => (0.0, -G));
            }

            // Lineare Schnittpunkt-Interpolation bei Bodenberührung (y = 0)
            var impactState = EulerIntegrator.InterpolateGroundImpact(previous, state);
            double diffX = impactState.X - xAna;
            double diffT = impactState.Time - tAna;

            Console.WriteLine($"Euler (dt = {dt:F2}s):");
            Console.WriteLine($"  t_Impact = {impactState.Time:F3} s (Fehler: {diffT:+0.0000;-0.0000} s)");
            Console.WriteLine($"  x_Impact = {impactState.X:F2} m (Fehler: {diffX:+0.000;-0.000} m)");
            Console.WriteLine($"  y_Max    = {yMaxNum:F2} m (Fehler: {yMaxNum - yMaxAna:+0.000;-0.000} m)\n");
        }
    }

    /// <summary>
    /// Stufe B: Track A - Hydraulikzylinder-Endlagendämpfung.
    /// Parameterstudie über Euler vs. Heun mit CSV-Export.
    /// </summary>
    /// <summary>
    /// Stufe B: Track A - Hydraulikzylinder-Endlagendämpfung.
    /// Parameterstudie Euler vs. Heun mit Stabilitätsgrenzen und CSV-Export.
    /// </summary>
    private static void RunTrackA_HydraulicCylinder()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("  Track A: Hydraulikzylinder-Endlagendämpfung");
        Console.WriteLine("=======================================================");

        var model = new HydraulicCylinderModel();
        var integrators = new IIntegrator[] { new EulerSolver(), new HeunSolver() };

        // Vorgegebene Schrittweiten: 10 µs, 100 µs, 500 µs, 2 ms
        double[] stepSizes = [10e-6, 100e-6, 500e-6, 2e-3];

        var study = StabilityStudy.Run(model, integrators, stepSizes, TEnd);

        ConsoleReport.PrintStudy(study, TEnd);

        // CSV-Export der Referenztrajektorie
        if (study.Reference.Trajectory.Count > 0)
        {
            CSVExporter.Export("simulation_cylinder.csv", study.Reference.Trajectory);
            Console.WriteLine($"Referenztrajektorie ({study.Reference.SolverName}, dt = 10 µs) nach 'simulation_cylinder.csv' exportiert.");
        }
    }

    /// <summary>
    /// Platzhalter für den nicht gewählten Track B.
    /// </summary>
    private static void RunTrackB_ArtilleryGame()
    {
        Console.WriteLine("\n--- [Stufe B Track B] Retro Artillery Duel ---");
        Console.WriteLine("Track B wurde nicht gewählt (Track A Industrie & Mechatronik ist aktiv).");
    }
}