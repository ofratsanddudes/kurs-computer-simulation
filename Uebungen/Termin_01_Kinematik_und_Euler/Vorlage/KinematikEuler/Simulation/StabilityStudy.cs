using KinematikEuler.Models;
using KinematikEuler.Solvers;

namespace KinematikEuler.Simulation;

/// <summary>Eine Tabellenzeile: Lauf plus Abweichung zur Referenzlösung.</summary>
public record StudyRow(SimulationResult Run, double? ContactTimeError, double? PenetrationError);

/// <summary>
/// Stabilitätsgrenze eines Solvers (aus einem feinen dt-Raster):
/// DtSafe     = größtes dt, bis zu dem ALLE getesteten Werte stabil sind
/// DtUnsafe   = kleinstes dt, ab dem ALLE getesteten Werte instabil sind
/// GrayZoneRuns = Anzahl getesteter Werte dazwischen (unregelmäßiges Verhalten)
/// </summary>
public record StabilityLimit(string SolverName, double DtSafe, double? DtUnsafe, int GrayZoneRuns);

public record StudyResult(
    SimulationResult Reference,
    IReadOnlyList<StudyRow> Rows,
    IReadOnlyList<StabilityLimit> Limits,
    double SweepStep);

public static class StabilityStudy
{
    public static StudyResult Run(
        HydraulicCylinderModel model,
        IReadOnlyList<IIntegrator> integrators,
        IReadOnlyList<double> stepSizes,
        double tEnd,
        double referenceDt = 10e-6,
        double sweepStep = 5e-6)
    {
        // 1. Referenzlösung: Heun mit sehr feiner Schrittweite (inkl. Trajektorie für die CSV)
        var reference = new CylinderSimulation(model, new HeunSolver())
            .Run(referenceDt, tEnd, recordTrajectory: true);

        // 2. Grobes Raster: alle Solver x alle vorgegebenen Schrittweiten
        var rows = new List<StudyRow>();
        foreach (var integrator in integrators)
        {
            var sim = new CylinderSimulation(model, integrator);
            foreach (double dt in stepSizes)
            {
                var run = sim.Run(dt, tEnd);

                double? timeError = null;
                if (run.ContactTime is double tc && reference.ContactTime is double tr)
                    timeError = tc - tr;

                double? penetrationError = null;
                if (run.IsStable && reference.IsStable)
                    penetrationError = run.MaxPenetration - reference.MaxPenetration;

                rows.Add(new StudyRow(run, timeError, penetrationError));
            }
        }

        // 3. Feines Raster: Stabilitätsgrenze pro Solver
        double dtMin = stepSizes.Min();
        double dtMax = stepSizes.Max();

        var limits = new List<StabilityLimit>();
        foreach (var integrator in integrators)
        {
            var sim = new CylinderSimulation(model, integrator);
            limits.Add(FindLimit(integrator.Name, d => sim.Run(d, tEnd).IsStable, dtMin, dtMax, sweepStep));
        }

        return new StudyResult(reference, rows, limits, sweepStep);
    }

    /// <summary>
    /// Tastet dt von dtMin bis dtMax in festen Schritten ab und bestimmt:
    /// - DtSafe: letzter Wert einer lückenlosen stabilen Reihe ab dtMin
    /// - DtUnsafe: erster Wert einer lückenlosen instabilen Reihe bis dtMax
    /// </summary>
    public static StabilityLimit FindLimit(
        string solverName, Func<double, bool> isStable, double dtMin, double dtMax, double dtStep)
    {
        var points = new List<(double Dt, bool Stable)>();
        for (int i = 0; ; i++)
        {
            double dt = dtMin + i * dtStep;
            if (dt > dtMax + 1e-12) break;
            points.Add((dt, isStable(dt)));
        }

        double safe = double.NaN;
        foreach (var p in points)
        {
            if (!p.Stable) break;
            safe = p.Dt;
        }

        double? unstableFrom = null;
        for (int i = points.Count - 1; i >= 0 && !points[i].Stable; i--)
        {
            unstableFrom = points[i].Dt;
        }

        int grayZone = points.Count(p =>
            (double.IsNaN(safe) || p.Dt > safe) &&
            (unstableFrom is null || p.Dt < unstableFrom.Value));

        return new StabilityLimit(solverName, safe, unstableFrom, grayZone);
    }
}