using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Windows;
using VectorTrussCanvas.Geometry;
using VectorTrussCanvas.Models;

namespace GeometryTests;

[TestClass]
public class GeometryTests
{
    // =====================================================================
    // Tests aus Stufe A (unverändert)
    // =====================================================================

    [TestMethod]
    public void Test_CoordinateTransformation_RoundTrip_WorldToScreenToWorld()
    {
        var transform = new Transform2D();
        transform.MarginX = 50.0;
        transform.MarginY = 50.0;

        // Bounding Box: [0, 10] x [0, 5], Canvas: 800 x 600
        transform.Update(0.0, 10.0, 0.0, 5.0, 800.0, 600.0);

        double[] testX = [0.0, 2.5, 5.0, 7.33, 10.0];
        double[] testY = [0.0, 1.25, 2.5, 4.88, 5.0];

        foreach (double xw in testX)
        {
            foreach (double yw in testY)
            {
                Point screenPt = transform.WorldToScreen(xw, yw);
                var (backX, backY) = transform.ScreenToWorld(screenPt.X, screenPt.Y);

                Assert.AreEqual(xw, backX, 1e-5, $"Rundreise-Fehler in X für ({xw}, {yw})");
                Assert.AreEqual(yw, backY, 1e-5, $"Rundreise-Fehler in Y für ({xw}, {yw})");
            }
        }
    }

    [TestMethod]
    public void Test_IsotropicScaling_PreservesSquareAspectRatio()
    {
        var transform = new Transform2D();
        transform.MarginX = 50.0;
        transform.MarginY = 50.0;
        // Asymmetrisches Fenster: 1000 x 400
        transform.Update(0.0, 4.0, 0.0, 4.0, 1000.0, 400.0);

        Point pOrigin = transform.WorldToScreen(0.0, 0.0);
        Point pRight = transform.WorldToScreen(1.0, 0.0);
        Point pUp = transform.WorldToScreen(0.0, 1.0);

        double deltaScreenX = Math.Abs(pRight.X - pOrigin.X);
        double deltaScreenY = Math.Abs(pUp.Y - pOrigin.Y);

        // Bei isotroper Skalierung MÜSSEN 1 Meter in X und 1 Meter in Y exakt gleich vielen Pixeln entsprechen!
        Assert.AreEqual(deltaScreenX, deltaScreenY, 1e-4,
            $"Isotropie verletzt: 1m in X = {deltaScreenX:F2}px, 1m in Y = {deltaScreenY:F2}px");
    }

    [TestMethod]
    public void Test_YInversion_WorldUpIsScreenDown()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 10.0, 0.0, 10.0, 500.0, 500.0);

        Point pBottom = transform.WorldToScreen(5.0, 0.0);
        Point pTop = transform.WorldToScreen(5.0, 10.0);

        // Welt Y=10 muss auf dem Canvas einen KLEINEREN Y-Wert haben als Welt Y=0 (Y wächst nach unten!)
        Assert.IsTrue(pTop.Y < pBottom.Y,
            $"Y-Inversion fehlerhaft: pTop.Y ({pTop.Y}) muss kleiner sein als pBottom.Y ({pBottom.Y})");
    }

    [TestMethod]
    public void Test_TrussGeometry_BoundsCalculation()
    {
        var sprint = TrussGeometry.CreateSprintTriangle();
        var (xMin, xMax, yMin, yMax) = sprint.GetBounds();

        // Dreieck: (0,0), (4,0), (2,2)
        Assert.AreEqual(0.0, xMin, 1e-6);
        Assert.AreEqual(4.0, xMax, 1e-6);
        Assert.AreEqual(0.0, yMin, 1e-6);
        Assert.AreEqual(2.0, yMax, 1e-6);
    }

    // =====================================================================
    // Neue Tests für Track A: Zoom und Pan
    // =====================================================================

    [TestMethod]
    public void Test_Camera_RoundTrip_WithZoomAndPan()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 12.0, 0.0, 4.0, 900.0, 600.0);

        // Beliebige Kameraeinstellung
        transform.ZoomAt(300.0, 200.0, 2.5);
        transform.PanBy(37.0, -21.0);

        double[] testX = [0.0, 3.0, 6.5, 12.0];
        double[] testY = [0.0, 1.0, 2.75, 4.0];

        foreach (double xw in testX)
        {
            foreach (double yw in testY)
            {
                Point p = transform.WorldToScreen(xw, yw);
                var (backX, backY) = transform.ScreenToWorld(p.X, p.Y);

                Assert.AreEqual(xw, backX, 1e-6, $"X-Rundreise mit Zoom/Pan fehlgeschlagen für ({xw}, {yw})");
                Assert.AreEqual(yw, backY, 1e-6, $"Y-Rundreise mit Zoom/Pan fehlgeschlagen für ({xw}, {yw})");
            }
        }
    }

    [TestMethod]
    public void Test_ZoomAt_KeepsPointUnderCursorFixed()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 10.0, 0.0, 5.0, 800.0, 600.0);

        // Weltpunkt (3, 2) liegt an einer bestimmten Bildschirmposition = "Mauszeiger"
        Point cursor = transform.WorldToScreen(3.0, 2.0);

        transform.ZoomAt(cursor.X, cursor.Y, 2.0);
        Point after = transform.WorldToScreen(3.0, 2.0);

        // Fixpunkt-Zoom: der Punkt unter dem Cursor darf nicht wandern
        Assert.AreEqual(cursor.X, after.X, 1e-6);
        Assert.AreEqual(cursor.Y, after.Y, 1e-6);
    }

    [TestMethod]
    public void Test_Zoom_PreservesIsotropy()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 4.0, 0.0, 4.0, 1000.0, 400.0);
        transform.ZoomAt(500.0, 200.0, 3.0);

        Point pOrigin = transform.WorldToScreen(0.0, 0.0);
        Point pRight = transform.WorldToScreen(1.0, 0.0);
        Point pUp = transform.WorldToScreen(0.0, 1.0);

        Assert.AreEqual(Math.Abs(pRight.X - pOrigin.X), Math.Abs(pUp.Y - pOrigin.Y), 1e-6,
            "Auch nach dem Zoomen muss 1 m in X gleich 1 m in Y sein.");
    }

    [TestMethod]
    public void Test_ResetCamera_RestoresAutoFitView()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 10.0, 0.0, 5.0, 800.0, 600.0);
        Point before = transform.WorldToScreen(4.0, 3.0);

        transform.ZoomAt(100.0, 100.0, 4.0);
        transform.PanBy(55.0, 12.0);
        transform.ResetCamera();

        Point after = transform.WorldToScreen(4.0, 3.0);
        Assert.AreEqual(before.X, after.X, 1e-9);
        Assert.AreEqual(before.Y, after.Y, 1e-9);
    }

    [TestMethod]
    public void Test_Zoom_IsClamped()
    {
        var transform = new Transform2D();
        transform.Update(0.0, 10.0, 0.0, 5.0, 800.0, 600.0);

        for (int i = 0; i < 100; i++) transform.ZoomAt(400.0, 300.0, 2.0);
        Assert.AreEqual(Transform2D.MaxZoom, transform.Zoom, 1e-9);

        for (int i = 0; i < 100; i++) transform.ZoomAt(400.0, 300.0, 0.5);
        Assert.AreEqual(Transform2D.MinZoom, transform.Zoom, 1e-9);
    }

    // =====================================================================
    // Neue Tests für Track A: Kranmodell
    // =====================================================================

    [TestMethod]
    public void Test_CraneTruss_HasTenNodesAndSeventeenBars_StaticallyDeterminate()
    {
        var crane = TrussGeometry.CreateCantileverCraneTruss();

        Assert.AreEqual(10, crane.Nodes.Count);
        Assert.AreEqual(17, crane.Bars.Count);

        // Abzählkriterium für ein statisch bestimmtes Fachwerk: b = 2k - 3
        Assert.AreEqual(2 * crane.Nodes.Count - 3, crane.Bars.Count);
    }

    [TestMethod]
    public void Test_CraneTruss_AllBarsReferenceExistingNodes()
    {
        var crane = TrussGeometry.CreateCantileverCraneTruss();
        var ids = crane.Nodes.Select(n => n.Id).ToHashSet();

        foreach (var bar in crane.Bars)
        {
            Assert.IsTrue(ids.Contains(bar.NodeAId), $"Stab {bar.Id}: Knoten A {bar.NodeAId} fehlt");
            Assert.IsTrue(ids.Contains(bar.NodeBId), $"Stab {bar.Id}: Knoten B {bar.NodeBId} fehlt");
        }
    }

    // =====================================================================
    // Neue Tests für Track A: JSON
    // =====================================================================

    [TestMethod]
    public void Test_Json_RoundTrip_PreservesTruss()
    {
        var original = TrussGeometry.CreateIndustrialPrattTruss();

        string json = TrussSerializer.ToJson(original);
        var restored = TrussSerializer.FromJson(json);

        Assert.AreEqual(original.Nodes.Count, restored.Nodes.Count);
        Assert.AreEqual(original.Bars.Count, restored.Bars.Count);
        Assert.AreEqual(original.Loads.Count, restored.Loads.Count);

        for (int i = 0; i < original.Nodes.Count; i++)
        {
            Assert.AreEqual(original.Nodes[i].Id, restored.Nodes[i].Id);
            Assert.AreEqual(original.Nodes[i].X, restored.Nodes[i].X, 1e-9);
            Assert.AreEqual(original.Nodes[i].Y, restored.Nodes[i].Y, 1e-9);
            Assert.AreEqual(original.Nodes[i].IsFixedSupport, restored.Nodes[i].IsFixedSupport);
            Assert.AreEqual(original.Nodes[i].IsRollerSupport, restored.Nodes[i].IsRollerSupport);
        }

        Assert.AreEqual(original.Loads[0].Fy, restored.Loads[0].Fy, 1e-9);
    }

    [TestMethod]
    public void Test_Json_SaveAndLoad_File()
    {
        var original = TrussGeometry.CreateCantileverCraneTruss();
        string path = Path.Combine(Path.GetTempPath(), $"truss_test_{Guid.NewGuid():N}.json");

        try
        {
            TrussSerializer.Save(original, path);
            var restored = TrussSerializer.Load(path);

            Assert.AreEqual(10, restored.Nodes.Count);
            Assert.AreEqual(17, restored.Bars.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public void Test_Json_InvalidBarReference_IsRejected()
    {
        const string json = """
            {
              "Nodes": [ { "Id": 1, "Name": "A", "X": 0, "Y": 0, "IsFixedSupport": false, "IsRollerSupport": false } ],
              "Bars":  [ { "Id": 1, "NodeAId": 1, "NodeBId": 99 } ],
              "Loads": []
            }
            """;

        try
        {
            TrussSerializer.FromJson(json);
            Assert.Fail("Ein Stab mit nicht vorhandenem Knoten hätte abgelehnt werden müssen.");
        }
        catch (InvalidDataException)
        {
            // erwartet
        }
    }
}