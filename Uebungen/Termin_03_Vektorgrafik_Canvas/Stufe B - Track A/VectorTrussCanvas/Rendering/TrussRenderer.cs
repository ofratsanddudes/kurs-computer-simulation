using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VectorTrussCanvas.Geometry;
using VectorTrussCanvas.Models;

namespace VectorTrussCanvas.Rendering;

/// <summary>
/// Zeichnet ein Fachwerk auf einen WPF-Canvas.
/// Trennung der Verantwortung:
///   - TrussGeometry  = Datenmodell (Welt, Meter)
///   - Transform2D    = Abbildung Welt -> Bildschirm (inkl. Zoom/Pan)
///   - TrussRenderer  = reine Darstellung (Shapes erzeugen)
///   - MainWindow     = Benutzereingaben
/// </summary>
public static class TrussRenderer
{
    // Kraftmaßstab: Pfeillänge in Pixeln pro kN
    public const double ForceScalePxPerKn = 1.5;
    public const double MinArrowLengthPx = 25.0;
    public const double MaxArrowLengthPx = 130.0;

    // Abstände der Bemaßungsketten in Pixeln (Aufgabenblatt Track A)
    private const double DimInnerOffset = 30.0;     // innere horizontale Kette (Teilmaße)
    private const double DimOuterOffset = 60.0;     // äußere horizontale Kette (Gesamtlänge)
    private const double DimVerticalOffset = 40.0;  // vertikale Kette links
    private const double DimVerticalStep = 35.0;    // zusätzlicher Abstand für Gesamthöhe bei Zwischenhöhen

    /// <summary>
    /// Leert den Canvas und zeichnet das komplette Modell neu.
    /// </summary>
    public static void Render(Canvas canvas, TrussGeometry truss, Transform2D t, TrussNode? selectedNode)
    {
        canvas.Children.Clear();

        var nodesById = truss.Nodes.ToDictionary(n => n.Id);

        DrawBars(canvas, truss, nodesById, t);
        DrawSupports(canvas, truss, t);
        DrawNodes(canvas, truss, t, selectedNode);
        DrawLoads(canvas, truss, nodesById, t);
        DrawDimensionChains(canvas, truss, t);
    }

    // =====================================================================
    // STÄBE + STABLÄNGEN
    // =====================================================================
    private static void DrawBars(Canvas canvas, TrussGeometry truss, Dictionary<int, TrussNode> nodesById, Transform2D t)
    {
        foreach (var bar in truss.Bars)
        {
            if (!nodesById.TryGetValue(bar.NodeAId, out var nodeA) ||
                !nodesById.TryGetValue(bar.NodeBId, out var nodeB))
                continue;

            Point pA = t.WorldToScreen(nodeA.X, nodeA.Y);
            Point pB = t.WorldToScreen(nodeB.X, nodeB.Y);

            canvas.Children.Add(new Line
            {
                X1 = pA.X,
                Y1 = pA.Y,
                X2 = pB.X,
                Y2 = pB.Y,
                Stroke = RenderStyles.Bar,
                StrokeThickness = 3.0
            });

            // Euklidische Länge in Weltkoordinaten [m]: L = sqrt(dx^2 + dy^2)
            double dx = nodeB.X - nodeA.X;
            double dy = nodeB.Y - nodeA.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            // Label in der Stabmitte (Bildschirm): p_mid = (p_A + p_B) / 2
            Point pMid = new Point((pA.X + pB.X) / 2.0, (pA.Y + pB.Y) / 2.0);

            var label = new TextBlock
            {
                Text = $"{length:F2} m",
                Foreground = RenderStyles.LengthLabelText,
                Background = RenderStyles.LengthLabelBackground,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, pMid.X - label.DesiredSize.Width / 2.0);
            Canvas.SetTop(label, pMid.Y - label.DesiredSize.Height / 2.0);
            canvas.Children.Add(label);
        }
    }

    // =====================================================================
    // AUFLAGER
    // =====================================================================
    private static void DrawSupports(Canvas canvas, TrussGeometry truss, Transform2D t)
    {
        const double h = 18.0;
        const double w = 14.0;

        foreach (var node in truss.Nodes)
        {
            if (!node.IsFixedSupport && !node.IsRollerSupport) continue;

            Point pos = t.WorldToScreen(node.X, node.Y);

            canvas.Children.Add(new Polygon
            {
                Points = new PointCollection
                {
                    pos,
                    new Point(pos.X - w / 2, pos.Y + h),
                    new Point(pos.X + w / 2, pos.Y + h)
                },
                Stroke = RenderStyles.SupportStroke,
                Fill = RenderStyles.SupportFill,
                StrokeThickness = 1.5
            });

            if (!node.IsFixedSupport && node.IsRollerSupport)
            {
                canvas.Children.Add(new Line
                {
                    X1 = pos.X - w / 2 - 2,
                    Y1 = pos.Y + h + 3,
                    X2 = pos.X + w / 2 + 2,
                    Y2 = pos.Y + h + 3,
                    Stroke = RenderStyles.SupportStroke,
                    StrokeThickness = 1.5
                });
            }
        }
    }

    // =====================================================================
    // KNOTEN (mit Millimeter-Auflösung :F3)
    // =====================================================================
    private static void DrawNodes(Canvas canvas, TrussGeometry truss, Transform2D t, TrussNode? selectedNode)
    {
        const double radius = 7.0;

        foreach (var node in truss.Nodes)
        {
            Point pos = t.WorldToScreen(node.X, node.Y);
            bool isSelected = ReferenceEquals(node, selectedNode);

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = isSelected ? RenderStyles.NodeSelectedFill : RenderStyles.NodeFill,
                Stroke = RenderStyles.NodeStroke,
                StrokeThickness = isSelected ? 2.5 : 1.5,
                Tag = node
            };
            Canvas.SetLeft(ellipse, pos.X - radius);
            Canvas.SetTop(ellipse, pos.Y - radius);
            canvas.Children.Add(ellipse);

            // Korrektur auf Millimeter-Auflösung :F3
            var label = new TextBlock
            {
                Text = $"{node.Name} ({node.X:F3}; {node.Y:F3})",
                Foreground = RenderStyles.NodeLabel,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(label, pos.X + 10);
            Canvas.SetTop(label, pos.Y - 14);
            canvas.Children.Add(label);
        }
    }

    // =====================================================================
    // LASTPFEILE
    // =====================================================================
    private static void DrawLoads(Canvas canvas, TrussGeometry truss, Dictionary<int, TrussNode> nodesById, Transform2D t)
    {
        foreach (var load in truss.Loads)
        {
            if (!nodesById.TryGetValue(load.NodeId, out var node)) continue;

            Point pos = t.WorldToScreen(node.X, node.Y);
            double magnitude = Math.Sqrt(load.Fx * load.Fx + load.Fy * load.Fy);
            double lengthPx = Math.Clamp(magnitude * ForceScalePxPerKn, MinArrowLengthPx, MaxArrowLengthPx);

            VectorArrow.AddForceArrow(canvas, pos, load.Fx, load.Fy, lengthPx, RenderStyles.Load);
        }
    }

    // =====================================================================
    // DIN-406-BEMASSUNGSKETTEN (zweistufig horizontal & vertikal)
    // =====================================================================
    private static void DrawDimensionChains(Canvas canvas, TrussGeometry truss, Transform2D t)
    {
        if (truss.Nodes.Count < 2) return;

        var (xMin, xMax, yMin, yMax) = truss.GetBounds();

        // Basis-Y für die Maßketten: immer unterhalb des tiefsten Punktes aller Knoten
        double bottomScreenY = t.WorldToScreen(0.0, yMin).Y;

        // ---------- HORIZONTAL ----------
        // 1. Untergurt-Knoten ermitteln:
        // Haben wir Untergurt-Knoten (Name fängt mit 'U' an)? Wenn ja, nimm diese (topologisch stabil!),
        // andernfalls nimm Knoten nahe der unteren Hälfte sortiert nach X.
        var namedBottomNodes = truss.Nodes
            .Where(n => n.Name.StartsWith("U", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.X)
            .ToList();

        var bottomNodes = namedBottomNodes.Count >= 2
            ? namedBottomNodes
            : truss.Nodes.OrderBy(n => n.X).ToList();

        bool hasSubDimensions = bottomNodes.Count >= 3;

        // Stufe 1 (Innere Kette bei +30 px): Bemaßung aller Abschnitte (keiner verschwindet mehr!)
        if (hasSubDimensions)
        {
            double innerY = bottomScreenY + DimInnerOffset;
            for (int i = 0; i < bottomNodes.Count - 1; i++)
            {
                var n1 = bottomNodes[i];
                var n2 = bottomNodes[i + 1];

                VectorArrow.AddHorizontalDimensionLine(
                    canvas,
                    t.WorldToScreen(n1.X, n1.Y),
                    t.WorldToScreen(n2.X, n2.Y),
                    innerY,
                    $"{Math.Abs(n2.X - n1.X):F2} m",
                    RenderStyles.Dimension);
            }
        }

        // Stufe 2 (Äußere Kette): Gesamtlänge über das gesamte Bauwerk (xMin bis xMax)
        double outerY = hasSubDimensions ? bottomScreenY + DimOuterOffset : bottomScreenY + DimInnerOffset;
        Point pTotalLeft = t.WorldToScreen(xMin, yMin);
        Point pTotalRight = t.WorldToScreen(xMax, yMin);

        VectorArrow.AddHorizontalDimensionLine(
            canvas,
            pTotalLeft,
            pTotalRight,
            outerY,
            $"{xMax - xMin:F2} m",
            RenderStyles.Dimension);

        // ---------- VERTIKAL ----------
        // Linke Außenkante (Kran-Mast bzw. linke Stütze)
        var leftEdgeNodes = truss.Nodes
            .Where(n => Math.Abs(n.X - xMin) < 1e-2)
            .OrderBy(n => n.Y)
            .ToList();

        Point pLeftBottom = t.WorldToScreen(xMin, yMin);
        Point pLeftTop = t.WorldToScreen(xMin, yMax);
        double leftX = pLeftBottom.X;

        bool hasIntermediateHeights = leftEdgeNodes.Count > 2;

        // Zwischenhöhen am Mast
        if (hasIntermediateHeights)
        {
            for (int i = 0; i < leftEdgeNodes.Count - 1; i++)
            {
                var lower = leftEdgeNodes[i];
                var upper = leftEdgeNodes[i + 1];

                VectorArrow.AddVerticalDimensionLine(
                    canvas,
                    t.WorldToScreen(lower.X, lower.Y),
                    t.WorldToScreen(upper.X, upper.Y),
                    leftX - DimVerticalOffset,
                    $"{upper.Y - lower.Y:F2} m",
                    RenderStyles.Dimension);
            }
        }

        // Gesamthöhe
        double totalX = hasIntermediateHeights
            ? leftX - DimVerticalOffset - DimVerticalStep
            : leftX - DimVerticalOffset;

        VectorArrow.AddVerticalDimensionLine(
            canvas, pLeftBottom, pLeftTop, totalX,
            $"{yMax - yMin:F2} m",
            RenderStyles.Dimension);
    }
}