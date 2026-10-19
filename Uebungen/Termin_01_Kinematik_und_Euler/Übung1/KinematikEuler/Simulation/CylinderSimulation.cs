using KinematikEuler.Models;
using KinematikEuler.Solvers;

namespace KinematikEuler.Simulation;

public record TrajectoryPoint(double Time, double X, double V, double A, double FDaempf);

public record SimulationResult(
    string SolverName,
    double Dt,
    double AMin,
    double XMax,
    bool IsStable,
    List<TrajectoryPoint> Trajectory,
    double? ContactTime = null,
    double? ContactVelocity = null)
{
    /// <summary>
    /// Maximaler Anschlagweg s = x_max - 0,80 m in Metern.
    /// 0, falls der Anschlag nicht erreicht wurde; NaN bei instabilen Läufen.
    /// </summary>
    public double MaxPenetration =>
        IsStable ? Math.Max(0.0, XMax - HydraulicCylinderModel.X_Anschlag) : double.NaN;
}

public class CylinderSimulation
{
    // Abbruchkriterien für "instabil" (frei gewählt, im README begründen)
    public const double MaxAbsPosition = 5.0;   // m   (der Hub beträgt nur 0,8 m)
    public const double MaxAbsVelocity = 1e5;   // m/s

    private readonly HydraulicCylinderModel _model;
    private readonly IIntegrator _integrator;

    public CylinderSimulation(HydraulicCylinderModel model, IIntegrator integrator)
    {
        _model = model;
        _integrator = integrator;
    }

    public SimulationResult Run(double dt, double tEnd = 0.30, bool recordTrajectory = false)
    {
        var state = new CylinderState(0.0, 0.0, 0.0);
        var trajectory = new List<TrajectoryPoint>();

        double minA = double.MaxValue;
        double maxX = double.MinValue;
        bool isStable = true;

        // Ereignis: erster Kontakt mit dem Anschlag
        double? contactTime = null;
        double? contactVelocity = null;

        // Sicherheits-Schrittbegrenzung gegen Endlosschleifen
        long maxSteps = (long)(tEnd / dt) + 1000;
        long stepCount = 0;

        while (state.Time <= tEnd && stepCount < maxSteps)
        {
            stepCount++;

            // 1. Stabilitätsprüfung: Werte ungültig oder unphysikalisch groß?
            if (IsInvalid(state))
            {
                isStable = false;
                break;
            }

            var (a, fDaempf) = _model.Evaluate(state.X, state.V);

            if (double.IsNaN(a) || double.IsInfinity(a))
            {
                isStable = false;
                break;
            }

            // 2. Kennwerte an den Gitterpunkten
            if (a < minA) minA = a;
            if (state.X > maxX) maxX = state.X;

            if (recordTrajectory)
            {
                trajectory.Add(new TrajectoryPoint(state.Time, state.X, state.V, a, fDaempf));
            }

            // 3. Ein Zeitschritt
            var previous = state;
            state = _integrator.Step(previous, dt, _model);

            // 4. Ereignis: x überschreitet in diesem Schritt die Anschlagmarke
            //    -> Zeitpunkt und Geschwindigkeit linear interpolieren
            if (contactTime is null
                && previous.X < HydraulicCylinderModel.X_Anschlag
                && state.X >= HydraulicCylinderModel.X_Anschlag)
            {
                double tau = (HydraulicCylinderModel.X_Anschlag - previous.X) / (state.X - previous.X);
                contactTime = previous.Time + tau * (state.Time - previous.Time);
                contactVelocity = previous.V + tau * (state.V - previous.V);
            }
        }

        // Auch der allerletzte Zustand muss gültig sein
        if (isStable && IsInvalid(state))
        {
            isStable = false;
        }

        return new SimulationResult(
            _integrator.Name,
            dt,
            isStable ? minA : double.NaN,
            isStable ? maxX : double.NaN,
            isStable,
            trajectory,
            isStable ? contactTime : null,
            isStable ? contactVelocity : null);
    }

    private static bool IsInvalid(CylinderState s) =>
        double.IsNaN(s.X) || double.IsInfinity(s.X) || Math.Abs(s.X) > MaxAbsPosition ||
        double.IsNaN(s.V) || double.IsInfinity(s.V) || Math.Abs(s.V) > MaxAbsVelocity;
}