using Microsoft.VisualStudio.TestTools.UnitTesting;
using KinematikEuler.Models;
using KinematikEuler.Simulation;
using KinematikEuler.Solvers;

namespace KinematikTests;

[TestClass]
public class CylinderSimulationTests
{
    private readonly HydraulicCylinderModel _model = new();

    [TestMethod]
    public void Heun_Fein_IstStabil_UndErreichtDenAnschlag()
    {
        var r = new CylinderSimulation(_model, new HeunSolver()).Run(100e-6, 0.30);

        Assert.IsTrue(r.IsStable);
        Assert.IsNotNull(r.ContactTime);
        Assert.IsTrue(r.ContactTime!.Value > 0.10 && r.ContactTime.Value < 0.13);   // gerechnet ca. 0,1125 s
        Assert.AreEqual(2.0, r.ContactVelocity!.Value, 0.1);                         // ca. 1,99 m/s
        Assert.IsTrue(r.MaxPenetration > 0.0005 && r.MaxPenetration < 0.003);        // ca. 1,5 mm
    }

    [TestMethod]
    public void Euler_Grob_IstInstabil()
    {
        var r = new CylinderSimulation(_model, new EulerSolver()).Run(500e-6, 0.30);
        Assert.IsFalse(r.IsStable);
        Assert.IsNull(r.ContactTime);
    }

    [TestMethod]
    public void Kontaktzeit_HaengtKaumVonDtAb_DankInterpolation()
    {
        var sim = new CylinderSimulation(_model, new HeunSolver());
        double t10 = sim.Run(10e-6, 0.30).ContactTime!.Value;
        double t100 = sim.Run(100e-6, 0.30).ContactTime!.Value;

        // Unterschied deutlich kleiner als ein Zeitschritt des groben Laufs wäre ohne Interpolation nicht garantiert
        Assert.AreEqual(t10, t100, 1e-3);
    }

    [TestMethod]
    public void FindLimit_ErkenntSauberenUebergang()
    {
        var l = StabilityStudy.FindLimit("Test", d => d < 3.001e-4, 1e-4, 5e-4, 5e-6);

        Assert.AreEqual(3.0e-4, l.DtSafe, 1e-9);
        Assert.AreEqual(3.05e-4, l.DtUnsafe!.Value, 1e-9);
        Assert.AreEqual(0, l.GrayZoneRuns);
    }
}