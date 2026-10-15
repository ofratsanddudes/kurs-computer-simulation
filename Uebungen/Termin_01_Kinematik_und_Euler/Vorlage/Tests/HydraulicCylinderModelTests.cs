using Microsoft.VisualStudio.TestTools.UnitTesting;
using KinematikEuler.Models;
using KinematikEuler.Solvers;

namespace KinematikTests;

[TestClass]
public class HydraulicCylinderModelTests
{
    private readonly HydraulicCylinderModel _m = new();

    [TestMethod]
    public void Freilauf_Start_Beschleunigung750()
        => Assert.AreEqual(750.0, _m.Evaluate(0.0, 0.0).A, 1e-9);

    [TestMethod]
    public void Daempfungszone_ErwarteteBeschleunigung()    // (60000 - 60500) / 80
        => Assert.AreEqual(-6.25, _m.Evaluate(0.7, 2.0).A, 1e-9);

    [TestMethod]
    public void Anschlag_FederDruecktZurueck()              // -(5e7 * 0.001) / 80
        => Assert.AreEqual(-625.0, _m.Evaluate(0.801, 0.0).A, 1e-6);

    [TestMethod]
    public void Euler_AusDerRuhe()
    {
        var s = new EulerSolver().Step(new CylinderState(0, 0, 0), 1e-3, _m);
        Assert.AreEqual(0.75, s.V, 1e-12);      // v = a * dt
        Assert.AreEqual(0.0, s.X, 1e-12);       // Euler: Ort nutzt die alte Geschwindigkeit
    }

    [TestMethod]
    public void Heun_AusDerRuhe()
    {
        var s = new HeunSolver().Step(new CylinderState(0, 0, 0), 1e-3, _m);

        // Prädiktor: v~ = 0,75 m/s. Dort wirkt schon die viskose Reibung 250 * 0,75 = 187,5 N.
        double a2 = (60000.0 - 250.0 * 0.75) / 80.0;                  // 747,65625 m/s²
        Assert.AreEqual(0.5e-3 * (750.0 + a2), s.V, 1e-12);            // 0,748828125 m/s
        Assert.AreEqual(375e-6, s.X, 1e-12);                           // 0,5 * dt * (0 + 0,75)
    }
}