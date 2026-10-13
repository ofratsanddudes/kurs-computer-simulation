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
    List<TrajectoryPoint> Trajectory);

public class CylinderSimulation
{
    private readonly HydraulicCylinderModel _model;
    private readonly IIntegrator _integrator;

    public CylinderSimulation(HydraulicCylinderModel model, IIntegrator integrator)
    {
        _model = model;
        _integrator = integrator;
    }

    public SimulationResult Run(double dt, double tEnd = 0.10, bool recordTrajectory = false)
    {
        var state = new CylinderState(0.0, 0.0, 0.0);
        var trajectory = new List<TrajectoryPoint>();

        double minA = double.MaxValue;
        double maxX = double.MinValue;
        bool isStable = true;

        // Sicherheits-Schrittbegrenzung gegen Endlosschleifen
        long maxSteps = (long)(tEnd / dt) + 1000;
        long stepCount = 0;

        while (state.Time <= tEnd && stepCount < maxSteps)
        {
            stepCount++;

            // 1. Stabilitätsprüfung: Sobald Werte ungültig oder unphysikalisch werden
            if (double.IsNaN(state.X) || double.IsInfinity(state.X) || Math.Abs(state.X) > 5.0 ||
                double.IsNaN(state.V) || double.IsInfinity(state.V) || Math.Abs(state.V) > 1e5)
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

            if (a < minA) minA = a;
            if (state.X > maxX) maxX = state.X;

            if (recordTrajectory)
            {
                trajectory.Add(new TrajectoryPoint(state.Time, state.X, state.V, a, fDaempf));
            }

            state = _integrator.Step(state, dt, _model);
        }

        return new SimulationResult(
            _integrator.Name,
            dt,
            isStable ? minA : double.NaN,
            isStable ? maxX : double.NaN,
            isStable,
            trajectory);
    }
}