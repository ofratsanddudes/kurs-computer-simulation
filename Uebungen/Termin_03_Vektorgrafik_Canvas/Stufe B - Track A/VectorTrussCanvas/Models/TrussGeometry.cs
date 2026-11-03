using System;
using System.Collections.Generic;
using System.Linq;

namespace VectorTrussCanvas.Models;

/// <summary>
/// Knoten des Fachwerks in Weltkoordinaten [m] (Y zeigt nach oben).
/// </summary>
public class TrussNode
{
    public int Id { get; set; }
    public string Name { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsFixedSupport { get; set; }
    public bool IsRollerSupport { get; set; }

    public TrussNode(int id, string name, double x, double y, bool isFixed = false, bool isRoller = false)
    {
        Id = id;
        Name = name;
        X = x;
        Y = y;
        IsFixedSupport = isFixed;
        IsRollerSupport = isRoller;
    }
}

/// <summary>
/// Stab zwischen zwei Knoten (topologische Verbindung über Knoten-IDs).
/// </summary>
public class TrussBar
{
    public int Id { get; set; }
    public int NodeAId { get; set; }
    public int NodeBId { get; set; }

    public TrussBar(int id, int nodeAId, int nodeBId)
    {
        Id = id;
        NodeAId = nodeAId;
        NodeBId = nodeBId;
    }
}

/// <summary>
/// Äußere Punktlast an einem Knoten in Kilonewton [kN].
/// </summary>
public class TrussLoad
{
    public int NodeId { get; set; }
    public double Fx { get; set; }
    public double Fy { get; set; }

    public TrussLoad(int nodeId, double fx, double fy)
    {
        NodeId = nodeId;
        Fx = fx;
        Fy = fy;
    }
}

/// <summary>
/// Gesamtes 2D-Fachwerkmodell. Rein geometrische Datenhaltung ohne FEM-Statik.
/// </summary>
public class TrussGeometry
{
    public List<TrussNode> Nodes { get; } = new();
    public List<TrussBar> Bars { get; } = new();
    public List<TrussLoad> Loads { get; } = new();

    /// <summary>
    /// Berechnet die minimale achsenparallele Bounding Box aller Knoten [xMin, xMax, yMin, yMax] in Metern.
    /// </summary>
    public (double xMin, double xMax, double yMin, double yMax) GetBounds()
    {
        if (Nodes.Count == 0)
            return (0, 1, 0, 1);

        double xMin = Nodes.Min(n => n.X);
        double xMax = Nodes.Max(n => n.X);
        double yMin = Nodes.Min(n => n.Y);
        double yMax = Nodes.Max(n => n.Y);

        if (Math.Abs(xMax - xMin) < 1e-4) xMax = xMin + 1.0;
        if (Math.Abs(yMax - yMin) < 1e-4) yMax = yMin + 1.0;

        return (xMin, xMax, yMin, yMax);
    }

    /// <summary>
    /// Sprint-Dreieck (Stufe A): 3 Knoten, 3 Stäbe.
    /// </summary>
    public static TrussGeometry CreateSprintTriangle()
    {
        var truss = new TrussGeometry();
        truss.Nodes.Add(new TrussNode(1, "K1", 0.0, 0.0, isFixed: true));
        truss.Nodes.Add(new TrussNode(2, "K2", 4.0, 0.0, isRoller: true));
        truss.Nodes.Add(new TrussNode(3, "K3", 2.0, 2.0));

        truss.Bars.Add(new TrussBar(1, 1, 2));
        truss.Bars.Add(new TrussBar(2, 2, 3));
        truss.Bars.Add(new TrussBar(3, 3, 1));

        truss.Loads.Add(new TrussLoad(3, 0.0, -20.0));
        return truss;
    }

    /// <summary>
    /// Pratt-Träger (Track A): 8 Knoten, 13 Stäbe, 12 m Spannweite, Höhe 2,5 m.
    /// </summary>
    public static TrussGeometry CreateIndustrialPrattTruss()
    {
        var truss = new TrussGeometry();

        // Untergurt (Y = 0 m)
        truss.Nodes.Add(new TrussNode(1, "U1", 0.0, 0.0, isFixed: true));
        truss.Nodes.Add(new TrussNode(2, "U2", 4.0, 0.0));
        truss.Nodes.Add(new TrussNode(3, "U3", 8.0, 0.0));
        truss.Nodes.Add(new TrussNode(4, "U4", 12.0, 0.0, isRoller: true));

        // Obergurt (Y = 2,5 m)
        truss.Nodes.Add(new TrussNode(5, "O1", 0.0, 2.5));
        truss.Nodes.Add(new TrussNode(6, "O2", 4.0, 2.5));
        truss.Nodes.Add(new TrussNode(7, "O3", 8.0, 2.5));
        truss.Nodes.Add(new TrussNode(8, "O4", 12.0, 2.5));

        // Untergurtstäbe
        truss.Bars.Add(new TrussBar(1, 1, 2));
        truss.Bars.Add(new TrussBar(2, 2, 3));
        truss.Bars.Add(new TrussBar(3, 3, 4));

        // Obergurtstäbe
        truss.Bars.Add(new TrussBar(4, 5, 6));
        truss.Bars.Add(new TrussBar(5, 6, 7));
        truss.Bars.Add(new TrussBar(6, 7, 8));

        // Vertikalpfosten
        truss.Bars.Add(new TrussBar(7, 1, 5));
        truss.Bars.Add(new TrussBar(8, 2, 6));
        truss.Bars.Add(new TrussBar(9, 3, 7));
        truss.Bars.Add(new TrussBar(10, 4, 8));

        // Diagonalen
        truss.Bars.Add(new TrussBar(11, 1, 6));
        truss.Bars.Add(new TrussBar(12, 6, 3));
        truss.Bars.Add(new TrussBar(13, 3, 8));

        // Lasten
        truss.Loads.Add(new TrussLoad(6, 0.0, -35.0));
        truss.Loads.Add(new TrussLoad(7, 0.0, -35.0));

        return truss;
    }

    /// <summary>
    /// Auslegerkran-Träger (Track A, 3. Modell): 10 Knoten, 17 Stäbe, Spannweite 12 m.
    /// Asymmetrisch: Einspannung links (Wand), Obergurt fällt zur Spitze hin von 4,0 m auf 1,0 m ab.
    /// Plausibilitätscheck: b = 2*k - 3 = 2*10 - 3 = 17 Stäbe.
    /// </summary>
    public static TrussGeometry CreateCantileverCraneTruss()
    {
        var truss = new TrussGeometry();

        // Untergurt (Y = 0 m), Feldweite 3 m
        truss.Nodes.Add(new TrussNode(1, "U1", 0.0, 0.0, isFixed: true));   // Festlager an der Wand
        truss.Nodes.Add(new TrussNode(2, "U2", 3.0, 0.0));
        truss.Nodes.Add(new TrussNode(3, "U3", 6.0, 0.0));
        truss.Nodes.Add(new TrussNode(4, "U4", 9.0, 0.0));
        truss.Nodes.Add(new TrussNode(5, "U5", 12.0, 0.0));                 // Auslegerspitze unten

        // Obergurt, fällt zur Spitze hin ab
        truss.Nodes.Add(new TrussNode(6, "O1", 0.0, 4.0, isRoller: true)); // Loselager an der Wand
        truss.Nodes.Add(new TrussNode(7, "O2", 3.0, 3.25));
        truss.Nodes.Add(new TrussNode(8, "O3", 6.0, 2.5));
        truss.Nodes.Add(new TrussNode(9, "O4", 9.0, 1.75));
        truss.Nodes.Add(new TrussNode(10, "O5", 12.0, 1.0));                // Auslegerspitze oben

        // Untergurtstäbe (4)
        truss.Bars.Add(new TrussBar(1, 1, 2));
        truss.Bars.Add(new TrussBar(2, 2, 3));
        truss.Bars.Add(new TrussBar(3, 3, 4));
        truss.Bars.Add(new TrussBar(4, 4, 5));

        // Obergurtstäbe (4)
        truss.Bars.Add(new TrussBar(5, 6, 7));
        truss.Bars.Add(new TrussBar(6, 7, 8));
        truss.Bars.Add(new TrussBar(7, 8, 9));
        truss.Bars.Add(new TrussBar(8, 9, 10));

        // Vertikalpfosten (5)
        truss.Bars.Add(new TrussBar(9, 1, 6));
        truss.Bars.Add(new TrussBar(10, 2, 7));
        truss.Bars.Add(new TrussBar(11, 3, 8));
        truss.Bars.Add(new TrussBar(12, 4, 9));
        truss.Bars.Add(new TrussBar(13, 5, 10));

        // Diagonalen (4): vom Obergurt zur Spitze hin nach unten
        truss.Bars.Add(new TrussBar(14, 6, 2));
        truss.Bars.Add(new TrussBar(15, 7, 3));
        truss.Bars.Add(new TrussBar(16, 8, 4));
        truss.Bars.Add(new TrussBar(17, 9, 5));

        // Lasten: Nutzlast an der Spitze und Zwischenlast in Feldmitte
        truss.Loads.Add(new TrussLoad(5, 0.0, -40.0));
        truss.Loads.Add(new TrussLoad(3, 0.0, -20.0));

        return truss;
    }
}