using System;
using System.Collections.Generic;
using System.Linq;

namespace VectorTrussCanvas.Models;

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

public class TrussGeometry
{
    public List<TrussNode> Nodes { get; } = new();
    public List<TrussBar> Bars { get; } = new();
    public List<TrussLoad> Loads { get; } = new();

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

    public static TrussGeometry CreateIndustrialPrattTruss()
    {
        var truss = new TrussGeometry();

        // Untergurt
        truss.Nodes.Add(new TrussNode(1, "U1", 0.0, 0.0, isFixed: true));
        truss.Nodes.Add(new TrussNode(2, "U2", 4.0, 0.0));
        truss.Nodes.Add(new TrussNode(3, "U3", 8.0, 0.0));
        truss.Nodes.Add(new TrussNode(4, "U4", 12.0, 0.0, isRoller: true));

        // Obergurt
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

        // Pfosten
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
}