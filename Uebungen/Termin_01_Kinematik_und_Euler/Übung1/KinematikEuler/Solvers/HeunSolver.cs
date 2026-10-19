using KinematikEuler.Models;

namespace KinematikEuler.Solvers;

public class HeunSolver : IIntegrator
{
    public string Name => "Heun (RK2)";

    public CylinderState Step(CylinderState current, double dt, HydraulicCylinderModel model)
    {
        // 1. Prädiktor (Euler-Hilfsschritt)
        var (a1, _) = model.Evaluate(current.X, current.V);
        double predX = current.X + dt * current.V;
        double predV = current.V + dt * a1;

        // 2. Ableitung am Intervallende
        var (a2, _) = model.Evaluate(predX, predV);

        // 3. Korrektor (Trapezregel)
        double nextTime = current.Time + dt;
        double nextX = current.X + 0.5 * dt * (current.V + predV);
        double nextV = current.V + 0.5 * dt * (a1 + a2);

        return new CylinderState(nextTime, nextX, nextV);
    }
}