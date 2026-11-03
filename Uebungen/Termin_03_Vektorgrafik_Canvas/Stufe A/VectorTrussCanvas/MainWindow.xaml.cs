using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VectorTrussCanvas.Geometry;
using VectorTrussCanvas.Models;

namespace VectorTrussCanvas;

public partial class MainWindow : Window
{
    private TrussGeometry _truss;
    private readonly Transform2D _transform = new();
    private TrussNode? _draggedNode = null;

    // Statisch eingefrorene Pinsel (Freeze-Doktrin: 0 B GC im Render-Tick)
    private static readonly Brush BarBrush;
    private static readonly Brush NodeBrush;
    private static readonly Brush SupportStroke;
    private static readonly Brush SupportFill;
    private static readonly Brush LengthLabelBrush;

    static MainWindow()
    {
        var bar = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        bar.Freeze();
        BarBrush = bar;

        var node = new SolidColorBrush(Color.FromRgb(0, 210, 255));
        node.Freeze();
        NodeBrush = node;

        var orange = Brushes.Orange.Clone();
        orange.Freeze();
        SupportStroke = orange;

        var orangeTrans = new SolidColorBrush(Color.FromArgb(80, 255, 165, 0));
        orangeTrans.Freeze();
        SupportFill = orangeTrans;

        var yellow = new SolidColorBrush(Color.FromRgb(255, 235, 59));
        yellow.Freeze();
        LengthLabelBrush = yellow;
    }

    public MainWindow()
    {
        InitializeComponent();
        _truss = TrussGeometry.CreateSprintTriangle();
    }

    private void Redraw()
    {
        if (TrussCanvas == null || TrussCanvas.ActualWidth < 10 || TrussCanvas.ActualHeight < 10)
            return;

        TrussCanvas.Children.Clear();

        // -----------------------------------------------------------------
        // 1. TRANSFORMATION (Stufe A, Aufgabe 4: Jitter-Fix)
        // Auto-Fit BoundingBox NUR aktualisieren, wenn gerade KEIN Knoten gezogen wird!
        // -----------------------------------------------------------------
        var (xMin, xMax, yMin, yMax) = _truss.GetBounds();

        if (_draggedNode == null)
        {
            _transform.MarginX = 70.0;
            _transform.MarginY = 70.0; // Bietet oben und unten jeweils 70 px Puffer für Bemaßung & Pfeile
            _transform.Update(xMin, xMax, yMin, yMax, TrussCanvas.ActualWidth, TrussCanvas.ActualHeight);
        }

        // -----------------------------------------------------------------
        // 2. STÄBE ZEICHNEN & EUKLIDISCHE STABLÄNGEN (Stufe A, Aufgabe 2)
        // -----------------------------------------------------------------
        foreach (var bar in _truss.Bars)
        {
            var nodeA = _truss.Nodes.FirstOrDefault(n => n.Id == bar.NodeAId);
            var nodeB = _truss.Nodes.FirstOrDefault(n => n.Id == bar.NodeBId);
            if (nodeA == null || nodeB == null) continue;

            Point pA = _transform.WorldToScreen(nodeA.X, nodeA.Y);
            Point pB = _transform.WorldToScreen(nodeB.X, nodeB.Y);

            var line = new Line
            {
                X1 = pA.X,
                Y1 = pA.Y,
                X2 = pB.X,
                Y2 = pB.Y,
                Stroke = BarBrush,
                StrokeThickness = 3.0
            };
            TrussCanvas.Children.Add(line);

            // Stablänge exakt in Weltkoordinaten [m]
            double dx = nodeB.X - nodeA.X;
            double dy = nodeB.Y - nodeA.Y;
            double lengthM = Math.Sqrt(dx * dx + dy * dy);

            // TextBlock am Bildschirm-Mittelpunkt platzieren
            Point pMid = new Point((pA.X + pB.X) / 2.0, (pA.Y + pB.Y) / 2.0);

            var lengthLabel = new TextBlock
            {
                Text = $"{lengthM:F2} m",
                Foreground = LengthLabelBrush,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 20, 20))
            };
            lengthLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double lblW = lengthLabel.DesiredSize.Width;
            double lblH = lengthLabel.DesiredSize.Height;

            Canvas.SetLeft(lengthLabel, pMid.X - lblW / 2.0);
            Canvas.SetTop(lengthLabel, pMid.Y - lblH / 2.0);
            TrussCanvas.Children.Add(lengthLabel);
        }

        // -----------------------------------------------------------------
        // 3. AUFLAGER ZEICHNEN
        // -----------------------------------------------------------------
        foreach (var node in _truss.Nodes)
        {
            Point pos = _transform.WorldToScreen(node.X, node.Y);
            if (node.IsFixedSupport)
                DrawSupportSymbol(pos, isFixed: true);
            else if (node.IsRollerSupport)
                DrawSupportSymbol(pos, isFixed: false);
        }

        // -----------------------------------------------------------------
        // 4. KNOTEN ZEICHNEN & MILLIMETER-LABELS (Stufe A, Aufgabe 4)
        // -----------------------------------------------------------------
        foreach (var node in _truss.Nodes)
        {
            Point pos = _transform.WorldToScreen(node.X, node.Y);
            double radius = 7.0;

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = NodeBrush,
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Tag = node
            };
            Canvas.SetLeft(ellipse, pos.X - radius);
            Canvas.SetTop(ellipse, pos.Y - radius);
            TrussCanvas.Children.Add(ellipse);

            // Formatierung mit Millimeter-Auflösung :F3
            var label = new TextBlock
            {
                Text = $"{node.Name} ({node.X:F3}; {node.Y:F3})",
                Foreground = Brushes.LightGreen,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(label, pos.X + 10);
            Canvas.SetTop(label, pos.Y - 14);
            TrussCanvas.Children.Add(label);
        }

        // -----------------------------------------------------------------
        // 5. EXTERNE LASTPFEILE
        // -----------------------------------------------------------------
        foreach (var load in _truss.Loads)
        {
            var node = _truss.Nodes.FirstOrDefault(n => n.Id == load.NodeId);
            if (node == null) continue;

            Point pos = _transform.WorldToScreen(node.X, node.Y);
            VectorArrow.AddForceArrow(TrussCanvas, pos, load.Fx, load.Fy, lengthPx: 60.0);
        }

        // -----------------------------------------------------------------
        // 6. HORIZONTALE DIN-406 MASSKETTE (Gesamtlänge)
        // -----------------------------------------------------------------
        var leftNode = _truss.Nodes.OrderBy(n => n.X).First();
        var rightNode = _truss.Nodes.OrderByDescending(n => n.X).First();

        // FIX 1: Nicht 0.0, sondern die tatsächliche Unterkante yMin des Modells verwenden:
        Point pLeft = _transform.WorldToScreen(leftNode.X, yMin);
        Point pRight = _transform.WorldToScreen(rightNode.X, yMin);

        // FIX 2: Dynamische Y-Position:
        // Liegt 35 px unter den Auflagern/Unterkante, wird aber begrenzt,
        // damit sie niemals aus dem unteren Canvas-Bereich herausrutscht:
        double dimY = Math.Max(pLeft.Y, pRight.Y) + 35.0;

        // Sicherheits-Clamp gegen Abschneiden am unteren Fensterrand:
        double maxAllowedY = TrussCanvas.ActualHeight - 15.0;
        if (dimY > maxAllowedY)
        {
            dimY = maxAllowedY;
        }

        double totalLength = rightNode.X - leftNode.X;

        VectorArrow.AddHorizontalDimensionLine(
            TrussCanvas, pLeft, pRight, dimY,
            $"{totalLength:F2} m");

        // -----------------------------------------------------------------
        // 7. VERTIKALE DIN-406 MASSKETTE (Stufe A, Aufgabe 3: Gesamthöhe)
        // -----------------------------------------------------------------
        Point pBottomLeft = _transform.WorldToScreen(leftNode.X, yMin);
        Point pTopLeft = _transform.WorldToScreen(leftNode.X, yMax);
        double heightTotal = yMax - yMin;

        VectorArrow.AddVerticalDimensionLine(
            TrussCanvas, pBottomLeft, pTopLeft, pLeft.X - 35.0,
            $"{heightTotal:F2} m");
    }

    private void DrawSupportSymbol(Point pos, bool isFixed)
    {
        double h = 18.0;
        double w = 14.0;

        var triangle = new Polygon
        {
            Points = new PointCollection
            {
                pos,
                new Point(pos.X - w / 2, pos.Y + h),
                new Point(pos.X + w / 2, pos.Y + h)
            },
            Stroke = SupportStroke,
            Fill = SupportFill,
            StrokeThickness = 1.5
        };
        TrussCanvas.Children.Add(triangle);

        if (!isFixed)
        {
            var rollerLine = new Line
            {
                X1 = pos.X - w / 2 - 2,
                Y1 = pos.Y + h + 3,
                X2 = pos.X + w / 2 + 2,
                Y2 = pos.Y + h + 3,
                Stroke = SupportStroke,
                StrokeThickness = 1.5
            };
            TrussCanvas.Children.Add(rollerLine);
        }
    }

    private void TrussCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    private void TrussCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point mousePos = e.GetPosition(TrussCanvas);

        _draggedNode = _truss.Nodes.FirstOrDefault(n =>
        {
            Point pScreen = _transform.WorldToScreen(n.X, n.Y);
            double dist = (mousePos - pScreen).Length;
            return dist <= 20.0;
        });

        if (_draggedNode != null)
        {
            TrussCanvas.CaptureMouse();
            TxtInfo.Text = $"Ziehe Knoten {_draggedNode.Name}...";
        }
    }

    private void TrussCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedNode == null) return;

        Point mousePos = e.GetPosition(TrussCanvas);
        var (worldX, worldY) = _transform.ScreenToWorld(mousePos.X, mousePos.Y);

        // Stufe A, Aufgabe 4: Millimeter-Auflösung (3 Dezimalstellen)
        _draggedNode.X = Math.Round(worldX, 3);
        _draggedNode.Y = Math.Round(worldY, 3);

        TxtInfo.Text = $"Knoten {_draggedNode.Name}: X = {_draggedNode.X:F3} m | Y = {_draggedNode.Y:F3} m";
        Redraw();
    }

    private void TrussCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedNode != null)
        {
            _draggedNode = null;
            TrussCanvas.ReleaseMouseCapture();
            TxtInfo.Text = "Knoten platziert. Auto-Fit aktiv.";
            Redraw(); // Erst jetzt wird Auto-Fit auf die neuen Abmessungen berechnet
        }
    }

    private void CmbModelSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbModelSelection == null) return;

        _truss = CmbModelSelection.SelectedIndex == 1
            ? TrussGeometry.CreateIndustrialPrattTruss()
            : TrussGeometry.CreateSprintTriangle();

        Redraw();
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _truss = CmbModelSelection.SelectedIndex == 1
            ? TrussGeometry.CreateIndustrialPrattTruss()
            : TrussGeometry.CreateSprintTriangle();

        TxtInfo.Text = "Modell zurückgesetzt.";
        Redraw();
    }
}