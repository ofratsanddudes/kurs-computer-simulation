using KinematikEuler.Models;

namespace KinematikEuler.Solvers;

public interface IIntegrator
{
    string Name { get; }
    CylinderState Step(CylinderState current, double dt, HydraulicCylinderModel model);
}