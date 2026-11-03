using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VectorTrussCanvas.Geometry;

/// <summary>
/// Hilfsklasse zum Zeichnen von Kraftpfeilen und DIN-406-Bemaßungsketten auf dem Canvas.
/// Alles wird mit nativen WPF-Shapes (Line, Polygon, TextBlock) erzeugt.
/// </summary>
public static class VectorArrow
{
    // =====================================================================
    // KRAFTPFEIL (aus der Vorlage, unverändert)
    // =====================================================================

    /// <summary>
    /// Zeichnet einen Kraftpfeil: Schaft (Line) + geschlossene Spitze (Polygon) + Beschriftung.
    ///
    /// Wichtig: Der Pfeil hat eine FESTE Länge in Pixeln (lengthPx) und eine FESTE Spitzengröße (14 px).
    /// Der Kraftbetrag wird nur als Text angezeigt. So bleibt die Spitze bei jeder Last gleich groß.
    /// </summary>
    /// <param name="startPos">Angriffspunkt der Kraft (Screen-Koordinaten)</param>
    /// <param name="fx">Kraftkomponente in X [kN] (Welt, positiv nach rechts)</param>
    /// <param name="fy">Kraftkomponente in Y [kN] (Welt, positiv nach OBEN)</param>
    public static void AddForceArrow(
        Canvas canvas,
        Point startPos,
        double fx,
        double fy,
        double lengthPx = 65.0,
        Brush? arrowColor = null)
    {
        arrowColor ??= Brushes.Red;

        // Betrag der Kraft; bei (fast) Null wird nichts gezeichnet
        double norm = Math.Sqrt(fx * fx + fy * fy);
        if (norm < 1e-6)
            return;

        // Einheitsvektor der Richtung auf dem BILDSCHIRM.
        // Y-Invertierung: Fy nach oben (Welt) entspricht -y auf dem Canvas.
        double ux = fx / norm;
        double uy = -fy / norm;

        // Spitze liegt lengthPx Pixel vom Angriffspunkt entfernt
        Point tipPos = new Point(startPos.X + ux * lengthPx, startPos.Y + uy * lengthPx);

        // 1) Schaft als Linie
        var shaft = new Line
        {
            X1 = startPos.X,
            Y1 = startPos.Y,
            X2 = tipPos.X,
            Y2 = tipPos.Y,
            Stroke = arrowColor,
            StrokeThickness = 2.5
        };
        canvas.Children.Add(shaft);

        // 2) Pfeilspitze über Drehmatrix:
        //    phi     = Richtungswinkel des Pfeils (im Bildschirmsystem)
        //    beta    = Halbwinkel der Spitze (15 Grad)
        //    p_left  = tip - L * (cos(phi - beta), sin(phi - beta))
        //    p_right = tip - L * (cos(phi + beta), sin(phi + beta))
        double lTip = 14.0;
        double beta = 15.0 * Math.PI / 180.0;
        double phi = Math.Atan2(uy, ux);

        Point pLeft = new Point(
            tipPos.X - lTip * Math.Cos(phi - beta),
            tipPos.Y - lTip * Math.Sin(phi - beta));

        Point pRight = new Point(
            tipPos.X - lTip * Math.Cos(phi + beta),
            tipPos.Y - lTip * Math.Sin(phi + beta));

        var head = new Polygon
        {
            Points = new PointCollection { tipPos, pLeft, pRight },
            Fill = arrowColor,
            Stroke = arrowColor,
            StrokeThickness = 1.0
        };
        canvas.Children.Add(head);

        // 3) Beschriftung (Kraftbetrag) hinter der Spitze
        var label = new TextBlock
        {
            Text = $"{norm:F1} kN",
            Foreground = arrowColor,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(label, tipPos.X + ux * 8 - 15);
        Canvas.SetTop(label, tipPos.Y + uy * 8 - 8);
        canvas.Children.Add(label);
    }

    // =====================================================================
    // HORIZONTALE MASSKETTE (aus der Vorlage, unverändert)
    // =====================================================================

    /// <summary>
    /// Horizontale Bemaßung nach DIN 406:
    ///   1. zwei Maßhilfslinien (gestrichelt, senkrecht von den Knoten nach unten)
    ///   2. eine waagerechte Maßlinie
    ///   3. zwei Maßpfeile an den Enden der Maßlinie
    ///   4. Maßzahl mittig über der Maßlinie
    /// </summary>
    /// <param name="p1">Linker Messpunkt (Screen)</param>
    /// <param name="p2">Rechter Messpunkt (Screen)</param>
    /// <param name="yScreen">Y-Position der Maßlinie (Screen)</param>
    public static void AddHorizontalDimensionLine(
        Canvas canvas,
        Point p1,
        Point p2,
        double yScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= Brushes.LightGray;

        // 1. Maßhilfslinien: starten 4 px unter dem Knoten (Sicherheitsabstand)
        //    und ragen 6 px über die Maßlinie hinaus (Überstand)
        canvas.Children.Add(new Line
        {
            X1 = p1.X,
            Y1 = p1.Y + 4,
            X2 = p1.X,
            Y2 = yScreen + 6,
            Stroke = strokeColor,
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });

        canvas.Children.Add(new Line
        {
            X1 = p2.X,
            Y1 = p2.Y + 4,
            X2 = p2.X,
            Y2 = yScreen + 6,
            Stroke = strokeColor,
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });

        // 2. Maßlinie (durchgezogen, etwas dicker)
        canvas.Children.Add(new Line
        {
            X1 = p1.X,
            Y1 = yScreen,
            X2 = p2.X,
            Y2 = yScreen,
            Stroke = strokeColor,
            StrokeThickness = 1.2
        });

        // 3. Schlanke Maßpfeile (Länge 10 px, Breite 2 * 3 px, Verhältnis ca. 1:3)
        double arrowLen = 10.0;
        double arrowH = 3.0;

        // Linker Pfeil: Spitze bei p1, Basis 10 px weiter rechts
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(p1.X, yScreen),
                new Point(p1.X + arrowLen, yScreen - arrowH),
                new Point(p1.X + arrowLen, yScreen + arrowH)
            },
            Fill = strokeColor
        });

        // Rechter Pfeil: Spitze bei p2, Basis 10 px weiter links
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(p2.X, yScreen),
                new Point(p2.X - arrowLen, yScreen - arrowH),
                new Point(p2.X - arrowLen, yScreen + arrowH)
            },
            Fill = strokeColor
        });

        // 4. Maßzahl mittig über der Maßlinie.
        //    Measure() liefert die tatsächliche Textbreite, damit exakt zentriert werden kann.
        var textBlock = new TextBlock
        {
            Text = dimensionText,
            Foreground = strokeColor,
            FontSize = 11,
            FontWeight = FontWeights.Normal
        };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textW = textBlock.DesiredSize.Width;

        Canvas.SetLeft(textBlock, (p1.X + p2.X) / 2.0 - textW / 2.0);
        Canvas.SetTop(textBlock, yScreen - 16.0);
        canvas.Children.Add(textBlock);
    }

    // =====================================================================
    // VERTIKALE MASSKETTE (NEU: Stufe A, Aufgabe 3)
    // =====================================================================

    /// <summary>
    /// Vertikale Bemaßung nach DIN 406. Entspricht der horizontalen Methode mit vertauschten Achsen:
    ///   1. zwei WAAGERECHTE, gestrichelte Maßhilfslinien (vom Knoten nach links)
    ///   2. eine SENKRECHTE Maßlinie
    ///   3. Maßpfeile oben und unten (Polygon)
    ///   4. Maßzahl mittig neben der Maßlinie, um 90 Grad gedreht
    /// </summary>
    /// <param name="pBottom">Unterer Messpunkt (Screen, größerer Y-Wert)</param>
    /// <param name="pTop">Oberer Messpunkt (Screen, kleinerer Y-Wert)</param>
    /// <param name="xScreen">X-Position der senkrechten Maßlinie (Screen)</param>
    public static void AddVerticalDimensionLine(
        Canvas canvas,
        Point pBottom,
        Point pTop,
        double xScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= Brushes.LightGray;

        // 1. Maßhilfslinien: starten 4 px links vom Knoten (Sicherheitsabstand)
        //    und ragen 6 px über die Maßlinie nach links hinaus (Überstand)
        canvas.Children.Add(new Line
        {
            X1 = pBottom.X - 4,
            Y1 = pBottom.Y,
            X2 = xScreen - 6,
            Y2 = pBottom.Y,
            Stroke = strokeColor,
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });

        canvas.Children.Add(new Line
        {
            X1 = pTop.X - 4,
            Y1 = pTop.Y,
            X2 = xScreen - 6,
            Y2 = pTop.Y,
            Stroke = strokeColor,
            StrokeThickness = 1.0,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });

        // 2. Senkrechte Maßlinie zwischen den beiden Hilfslinien
        canvas.Children.Add(new Line
        {
            X1 = xScreen,
            Y1 = pBottom.Y,
            X2 = xScreen,
            Y2 = pTop.Y,
            Stroke = strokeColor,
            StrokeThickness = 1.2
        });

        // 3. Maßpfeile (Länge 10 px, Breite 2 * 3 px)
        double arrowLen = 10.0;
        double arrowH = 3.0;

        // Unterer Pfeil: Spitze bei pBottom, Basis 10 px weiter OBEN
        // (Y wächst nach unten, also "weiter oben" = kleinerer Y-Wert)
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(xScreen, pBottom.Y),
                new Point(xScreen - arrowH, pBottom.Y - arrowLen),
                new Point(xScreen + arrowH, pBottom.Y - arrowLen)
            },
            Fill = strokeColor
        });

        // Oberer Pfeil: Spitze bei pTop, Basis 10 px weiter UNTEN
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(xScreen, pTop.Y),
                new Point(xScreen - arrowH, pTop.Y + arrowLen),
                new Point(xScreen + arrowH, pTop.Y + arrowLen)
            },
            Fill = strokeColor
        });

        // 4. Maßzahl: mittig, um -90 Grad gedreht (lesbar von links) links neben der Maßlinie.
        var textBlock = new TextBlock
        {
            Text = dimensionText,
            Foreground = strokeColor,
            FontSize = 11,
            FontWeight = FontWeights.Normal
        };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textW = textBlock.DesiredSize.Width;   // Breite VOR der Drehung
        double textH = textBlock.DesiredSize.Height;  // Höhe VOR der Drehung

        double yMid = (pBottom.Y + pTop.Y) / 2.0;

        // Drehung um die Mitte des Textblocks (0.5 / 0.5 = Mittelpunkt)
        textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
        textBlock.RenderTransform = new RotateTransform(-90);

        // Nach der Drehung ist der Text optisch textH breit und textW hoch.
        // Canvas.SetLeft/Top beziehen sich auf den UNGEDREHTEN Block, daher die Korrektur:
        //   rechter Rand des gedrehten Textes = xScreen - 8
        //   Mitte des gedrehten Textes        = xScreen - 8 - textH/2
        //   linke Kante des ungedrehten Blocks = Mitte - textW/2
        Canvas.SetLeft(textBlock, xScreen - 8 - textH / 2.0 - textW / 2.0);
        Canvas.SetTop(textBlock, yMid - textH / 2.0);
        canvas.Children.Add(textBlock);
    }
}