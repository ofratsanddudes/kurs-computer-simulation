using KinematikEuler.Models;

namespace KinematikEuler.Solvers;

public class EulerSolver : IIntegrator
{
    public string Name => "Euler (RK1)";

    public CylinderState Step(CylinderState current, double dt, HydraulicCylinderModel model)
    {
        var (a, _) = model.Evaluate(current.X, current.V);

        double nextTime = current.Time + dt;
        double nextX = current.X + dt * current.V;
        double nextV = current.V + dt * a;

        return new CylinderState(nextTime, nextX, nextV);
    }
}