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
    // Aktuelles Fachwerkmodell (Weltkoordinaten in Metern)
    private TrussGeometry _truss;

    // Welt <-> Bildschirm-Transformation (siehe Transform2D.cs)
    private readonly Transform2D _transform = new();

    // Knoten, der gerade mit der Maus gezogen wird (null = kein Drag aktiv)
    // Dient auch als "Schalter" für den Jitter-Fix (Aufgabe 4).
    private TrussNode? _draggedNode = null;

    public MainWindow()
    {
        InitializeComponent();
        _truss = TrussGeometry.CreateSprintTriangle();
    }

    /// <summary>
    /// Zeichnet das komplette Modell neu (Retained Mode: Canvas leeren, alle Shapes neu erzeugen).
    /// </summary>
    private void Redraw()
    {
        if (TrussCanvas == null || TrussCanvas.ActualWidth < 10 || TrussCanvas.ActualHeight < 10)
            return;

        TrussCanvas.Children.Clear();

        // -----------------------------------------------------------------
        // 1. TRANSFORMATION AKTUALISIEREN
        //    Die Bounding Box wird IMMER berechnet (wird weiter unten für die
        //    Bemaßung gebraucht). Die Transformation selbst wird aber nur
        //    aktualisiert, wenn gerade KEIN Knoten gezogen wird.
        //
        //    AUFGABE 4 (Jitter-Fix):
        //    Würde Update() während des Drags laufen, wüchse die Bounding Box,
        //    der Maßstab schrumpfte und der Knoten "liefe" unter der Maus weg.
        //    Mit eingefrorener Transformation bleibt der Knoten unter dem Cursor.
        //    Auto-Fit erfolgt erst nach dem Loslassen (MouseUp -> Redraw).
        // -----------------------------------------------------------------
        var (xMin, xMax, yMin, yMax) = _truss.GetBounds();

        if (_draggedNode == null)
        {
            _transform.MarginX = 70.0;
            _transform.MarginY = 70.0;
            _transform.Update(xMin, xMax, yMin, yMax, TrussCanvas.ActualWidth, TrussCanvas.ActualHeight);
        }

        // -----------------------------------------------------------------
        // 2. STÄBE ZEICHNEN (Line) + STABLÄNGEN-LABELS (AUFGABE 2)
        // -----------------------------------------------------------------
        foreach (var bar in _truss.Bars)
        {
            var nodeA = _truss.Nodes.FirstOrDefault(n => n.Id == bar.NodeAId);
            var nodeB = _truss.Nodes.FirstOrDefault(n => n.Id == bar.NodeBId);
            if (nodeA == null || nodeB == null) continue;

            // Endpunkte in Bildschirmkoordinaten [px]
            Point pA = _transform.WorldToScreen(nodeA.X, nodeA.Y);
            Point pB = _transform.WorldToScreen(nodeB.X, nodeB.Y);

            var line = new Line
            {
                X1 = pA.X,
                Y1 = pA.Y,
                X2 = pB.X,
                Y2 = pB.Y,
                Stroke = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                StrokeThickness = 3.0
            };
            TrussCanvas.Children.Add(line);

            // --- AUFGABE 2: Euklidische Stablänge ---
            //   L = sqrt( (X_B - X_A)^2 + (Y_B - Y_A)^2 )
            // Die Länge wird in WELTKOORDINATEN [m] berechnet (nicht in Pixeln!),
            // sonst wäre sie vom Zoom abhängig.
            double dx = nodeB.X - nodeA.X;
            double dy = nodeB.Y - nodeA.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            // Mittelpunkt des Stabes in BILDSCHIRMKOORDINATEN: p_mid = (p_A + p_B) / 2
            // (dort wird das Label platziert)
            Point pMid = new Point((pA.X + pB.X) / 2.0, (pA.Y + pB.Y) / 2.0);

            var lengthLabel = new TextBlock
            {
                Text = $"{length:F2} m",
                Foreground = Brushes.Yellow,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                // halbtransparenter dunkler Hintergrund, damit der Text über dem Stab lesbar bleibt
                Background = new SolidColorBrush(Color.FromArgb(160, 20, 20, 20))
            };

            // Textgröße messen, um das Label exakt auf dem Mittelpunkt zu zentrieren
            lengthLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(lengthLabel, pMid.X - lengthLabel.DesiredSize.Width / 2.0);
            Canvas.SetTop(lengthLabel, pMid.Y - lengthLabel.DesiredSize.Height / 2.0);
            TrussCanvas.Children.Add(lengthLabel);
        }

        // -----------------------------------------------------------------
        // 3. AUFLAGER ZEICHNEN
        // -----------------------------------------------------------------
        foreach (var node in _truss.Nodes)
        {
            Point pos = _transform.WorldToScreen(node.X, node.Y);

            if (node.IsFixedSupport)
            {
                // Festlager: Dreieckssymbol unter dem Knoten
                DrawSupportSymbol(pos, isFixed: true);
            }
            else if (node.IsRollerSupport)
            {
                // Loselager: Dreieck + Rollenlinie
                DrawSupportSymbol(pos, isFixed: false);
            }
        }

        // -----------------------------------------------------------------
        // 4. KNOTEN ZEICHNEN (Ellipse + Text-Label)
        // -----------------------------------------------------------------
        foreach (var node in _truss.Nodes)
        {
            Point pos = _transform.WorldToScreen(node.X, node.Y);
            double radius = 7.0;

            var ellipse = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
                Tag = node // Knoten am Shape merken (für Hit-Testing)
            };
            // Ellipse positioniert sich über die linke obere Ecke der Hüllbox,
            // daher Mittelpunkt minus Radius, damit der Kreis exakt auf dem Knoten sitzt
            Canvas.SetLeft(ellipse, pos.X - radius);
            Canvas.SetTop(ellipse, pos.Y - radius);
            TrussCanvas.Children.Add(ellipse);

            // Knotenlabel mit Name und Koordinaten
            var label = new TextBlock
            {
                Text = $"{node.Name} ({node.X:F1}; {node.Y:F1})",
                Foreground = Brushes.LightGreen,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(label, pos.X + 10);
            Canvas.SetTop(label, pos.Y - 14);
            TrussCanvas.Children.Add(label);
        }

        // -----------------------------------------------------------------
        // 5. LASTPFEILE ZEICHNEN (feste Pfeillänge 60 px, Betrag als Text)
        // -----------------------------------------------------------------
        foreach (var load in _truss.Loads)
        {
            var node = _truss.Nodes.FirstOrDefault(n => n.Id == load.NodeId);
            if (node == null) continue;

            Point pos = _transform.WorldToScreen(node.X, node.Y);
            VectorArrow.AddForceArrow(TrussCanvas, pos, load.Fx, load.Fy, lengthPx: 60.0);
        }

        // -----------------------------------------------------------------
        // 6. HORIZONTALE DIN-406-MASSKETTE (Gesamtlänge, aus der Vorlage)
        // -----------------------------------------------------------------
        var leftNode = _truss.Nodes.OrderBy(n => n.X).First();
        var rightNode = _truss.Nodes.OrderByDescending(n => n.X).First();
        Point pLeft = _transform.WorldToScreen(leftNode.X, 0.0);
        Point pRight = _transform.WorldToScreen(rightNode.X, 0.0);
        double dimY = Math.Max(pLeft.Y, pRight.Y) + 35.0;   // 35 px unterhalb des Tragwerks
        double totalLength = rightNode.X - leftNode.X;

        VectorArrow.AddHorizontalDimensionLine(
            TrussCanvas, pLeft, pRight, dimY,
            $"{totalLength:F2} m");

        // -----------------------------------------------------------------
        // 7. VERTIKALE DIN-406-MASSKETTE (AUFGABE 3): Gesamthöhe links neben dem Tragwerk
        //    Messpunkte: unterster und oberster Punkt der Bounding Box, x-Position des linken Knotens.
        //    Die Maßlinie liegt 35 px links davon.
        // -----------------------------------------------------------------
        Point pBottomLeft = _transform.WorldToScreen(leftNode.X, yMin);
        Point pTopLeft = _transform.WorldToScreen(leftNode.X, yMax);
        double heightTotal = yMax - yMin;

        VectorArrow.AddVerticalDimensionLine(
            TrussCanvas, pBottomLeft, pTopLeft, pLeft.X - 35.0,
            $"{heightTotal:F2} m");
    }

    /// <summary>
    /// Zeichnet ein Auflagersymbol (Dreieck) unter dem Knoten.
    /// Loselager bekommt zusätzlich eine Rollenlinie.
    /// </summary>
    private void DrawSupportSymbol(Point pos, bool isFixed)
    {
        double h = 18.0;   // Höhe des Dreiecks [px]
        double w = 14.0;   // Breite des Dreiecks [px]

        // Spitze des Dreiecks liegt auf dem Knoten
        var triangle = new Polygon
        {
            Points = new PointCollection
            {
                pos,
                new Point(pos.X - w / 2, pos.Y + h),
                new Point(pos.X + w / 2, pos.Y + h)
            },
            Stroke = Brushes.Orange,
            Fill = new SolidColorBrush(Color.FromArgb(80, 255, 165, 0)),
            StrokeThickness = 1.5
        };
        TrussCanvas.Children.Add(triangle);

        if (!isFixed)
        {
            // Loselager: Linie unter dem Dreieck als Rollensymbol
            var rollerLine = new Line
            {
                X1 = pos.X - w / 2 - 2,
                Y1 = pos.Y + h + 3,
                X2 = pos.X + w / 2 + 2,
                Y2 = pos.Y + h + 3,
                Stroke = Brushes.Orange,
                StrokeThickness = 1.5
            };
            TrussCanvas.Children.Add(rollerLine);
        }
    }

    // =====================================================================
    // EVENT-HANDLER
    // =====================================================================

    /// <summary>
    /// Fenstergröße geändert -> neu zeichnen (Auto-Fit passt Maßstab und Zentrierung an).
    /// </summary>
    private void TrussCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    /// <summary>
    /// Linke Maustaste gedrückt: Knoten in der Nähe des Cursors greifen (Hit-Test, 20 px Radius).
    /// </summary>
    private void TrussCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        Point mousePos = e.GetPosition(TrussCanvas);

        // Ersten Knoten innerhalb von 20 Pixeln um den Mauszeiger finden
        _draggedNode = _truss.Nodes.FirstOrDefault(n =>
        {
            Point pScreen = _transform.WorldToScreen(n.X, n.Y);
            double dist = (mousePos - pScreen).Length;
            return dist <= 20.0;
        });

        if (_draggedNode != null)
        {
            // Maus "fangen": MouseMove/MouseUp kommen auch dann an,
            // wenn der Cursor den Canvas verlässt
            TrussCanvas.CaptureMouse();
            TxtInfo.Text = $"Ziehe Knoten {_draggedNode.Name}...";
        }
    }

    /// <summary>
    /// Maus bewegt: Wenn ein Knoten gegriffen ist, Mausposition in Weltkoordinaten umrechnen
    /// und den Knoten dorthin setzen.
    /// </summary>
    private void TrussCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedNode == null) return;

        Point mousePos = e.GetPosition(TrussCanvas);

        // Bildschirm [px] -> Welt [m] (mit der EINGEFRORENEN Transformation)
        var (worldX, worldY) = _transform.ScreenToWorld(mousePos.X, mousePos.Y);

        // AUFGABE 4: Millimeter-Auflösung (3 Nachkommastellen in Metern)
        _draggedNode.X = Math.Round(worldX, 3);
        _draggedNode.Y = Math.Round(worldY, 3);

        TxtInfo.Text = $"Knoten {_draggedNode.Name}: X = {_draggedNode.X:F3} m | Y = {_draggedNode.Y:F3} m";
        Redraw();
    }

    /// <summary>
    /// Maustaste losgelassen: Drag beenden. Da _draggedNode jetzt null ist, führt Redraw()
    /// wieder Transform2D.Update() aus -> Auto-Fit auf die neue Bounding Box.
    /// </summary>
    private void TrussCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedNode != null)
        {
            _draggedNode = null;
            TrussCanvas.ReleaseMouseCapture();
            TxtInfo.Text = "Knoten platziert. Auto-Fit aktiv.";
            Redraw();
        }
    }

    /// <summary>
    /// Modellwahl in der ComboBox geändert.
    /// </summary>
    private void CmbModelSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbModelSelection == null) return;

        _truss = CmbModelSelection.SelectedIndex == 1
            ? TrussGeometry.CreateIndustrialPrattTruss()
            : TrussGeometry.CreateSprintTriangle();

        Redraw();
    }

    /// <summary>
    /// Reset: aktuelles Modell in den Ausgangszustand zurücksetzen.
    /// </summary>
    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _truss = CmbModelSelection.SelectedIndex == 1
            ? TrussGeometry.CreateIndustrialPrattTruss()
            : TrussGeometry.CreateSprintTriangle();

        TxtInfo.Text = "Modell zurückgesetzt.";
        Redraw();
    }
}