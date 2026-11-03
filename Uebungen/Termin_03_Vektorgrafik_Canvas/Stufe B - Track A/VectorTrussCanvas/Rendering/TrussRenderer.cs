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
    // Kraftmaßstab: Pfeillänge in Pixeln pro kN (nicht der Geometriemaßstab!)
    // Die Länge wird begrenzt, die Pfeilspitze hat dagegen IMMER feste Pixelgröße (siehe VectorArrow).
    public const double ForceScalePxPerKn = 1.5;
    public const double MinArrowLengthPx = 25.0;
    public const double MaxArrowLengthPx = 130.0;

    // Abstände der Bemaßungsketten in Pixeln (Aufgabenblatt Track A)
    private const double DimInnerOffset = 30.0;     // innere horizontale Kette (Teilmaße)
    private const double DimOuterOffset = 60.0;     // äußere horizontale Kette (Gesamtlänge)
    private const double DimVerticalOffset = 40.0;  // vertikale Kette links
    private const double DimVerticalStep = 35.0;    // zusätzlicher Abstand für die Gesamthöhe bei Zwischenhöhen

    /// <summary>
    /// Leert den Canvas und zeichnet das komplette Modell.
    /// </summary>
    /// <param name="selectedNode">Ausgewählter Knoten (wird farblich hervorgehoben) oder null.</param>
    public static void Render(Canvas canvas, TrussGeometry truss, Transform2D t, TrussNode? selectedNode)
    {
        canvas.Children.Clear();

        // Einmal pro Frame: schneller Knotenzugriff über die ID (statt FirstOrDefault pro Stab)
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
        const double h = 18.0;   // Höhe des Dreiecks [px]
        const double w = 14.0;   // Breite des Dreiecks [px]

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

            // Loselager: zusätzliche Rollenlinie unter dem Dreieck
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
    // KNOTEN
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
            // Ellipse wird über die linke obere Ecke positioniert -> Mittelpunkt minus Radius
            Canvas.SetLeft(ellipse, pos.X - radius);
            Canvas.SetTop(ellipse, pos.Y - radius);
            canvas.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = $"{node.Name} ({node.X:F1}; {node.Y:F1})",
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

            // Pfeillänge = Kraftmaßstab * Betrag, begrenzt auf [Min, Max].
            // Die Richtung (atan2) und die feste Spitzengröße (14 px) übernimmt VectorArrow.
            double magnitude = Math.Sqrt(load.Fx * load.Fx + load.Fy * load.Fy);
            double lengthPx = Math.Clamp(magnitude * ForceScalePxPerKn, MinArrowLengthPx, MaxArrowLengthPx);

            VectorArrow.AddForceArrow(canvas, pos, load.Fx, load.Fy, lengthPx, RenderStyles.Load);
        }
    }

    // =====================================================================
    // DIN-406-BEMASSUNGSKETTEN
    // =====================================================================
    private static void DrawDimensionChains(Canvas canvas, TrussGeometry truss, Transform2D t)
    {
        const double eps = 1e-3;   // Toleranz für "liegt auf derselben Linie" [m]
        var (xMin, _, yMin, yMax) = truss.GetBounds();

        // ---------- HORIZONTAL ----------
        // Untergurt = alle Knoten auf Höhe yMin, von links nach rechts
        var bottomNodes = truss.Nodes
            .Where(n => Math.Abs(n.Y - yMin) < eps)
            .OrderBy(n => n.X)
            .ToList();

        double baseY = t.WorldToScreen(0.0, yMin).Y;

        if (bottomNodes.Count >= 2)
        {
            // Bei nur zwei Knoten wäre das Teilmaß gleich dem Gesamtmaß -> nur eine Kette
            bool hasInnerChain = bottomNodes.Count >= 3;

            // Stufe 1: Teilmaße aller benachbarten Untergurtknoten
            if (hasInnerChain)
            {
                for (int i = 0; i < bottomNodes.Count - 1; i++)
                {
                    var n1 = bottomNodes[i];
                    var n2 = bottomNodes[i + 1];

                    VectorArrow.AddHorizontalDimensionLine(
                        canvas,
                        t.WorldToScreen(n1.X, n1.Y),
                        t.WorldToScreen(n2.X, n2.Y),
                        baseY + DimInnerOffset,
                        $"{n2.X - n1.X:F2} m",
                        RenderStyles.Dimension);
                }
            }

            // Stufe 2: Gesamtlänge
            var first = bottomNodes.First();
            var last = bottomNodes.Last();
            double outerY = hasInnerChain ? baseY + DimOuterOffset : baseY + DimInnerOffset;

            VectorArrow.AddHorizontalDimensionLine(
                canvas,
                t.WorldToScreen(first.X, first.Y),
                t.WorldToScreen(last.X, last.Y),
                outerY,
                $"{last.X - first.X:F2} m",
                RenderStyles.Dimension);
        }

        // ---------- VERTIKAL ----------
        // Knoten am linken Rand von unten nach oben
        var leftEdgeNodes = truss.Nodes
            .Where(n => Math.Abs(n.X - xMin) < eps)
            .OrderBy(n => n.Y)
            .ToList();

        Point pLeftBottom = t.WorldToScreen(xMin, yMin);
        Point pLeftTop = t.WorldToScreen(xMin, yMax);
        double leftX = pLeftBottom.X;

        bool hasIntermediateHeights = leftEdgeNodes.Count > 2;

        // Zwischenhöhen (nur wenn mehr als zwei Knoten übereinander liegen)
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

        // Gesamthöhe (bei Zwischenhöhen eine Stufe weiter außen)
        double totalX = hasIntermediateHeights
            ? leftX - DimVerticalOffset - DimVerticalStep
            : leftX - DimVerticalOffset;

        VectorArrow.AddVerticalDimensionLine(
            canvas, pLeftBottom, pLeftTop, totalX,
            $"{yMax - yMin:F2} m",
            RenderStyles.Dimension);
    }
}