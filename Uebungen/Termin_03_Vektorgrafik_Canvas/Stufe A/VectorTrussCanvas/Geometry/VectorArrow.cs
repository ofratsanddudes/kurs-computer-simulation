using System;
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
    private static readonly Brush DefaultArrowBrush;
    private static readonly Brush DefaultDimBrush;

    static VectorArrow()
    {
        // Freeze-Doktrin: Pinsel einfrieren zur Vermeidung von GC-Allokationen
        var red = new SolidColorBrush(Colors.Red);
        red.Freeze();
        DefaultArrowBrush = red;

        var gray = new SolidColorBrush(Colors.LightGray);
        gray.Freeze();
        DefaultDimBrush = gray;
    }

    // =====================================================================
    // KRAFTPFEIL (mit analytischer Pfeilspitzenberechnung)
    // =====================================================================

    /// <summary>
    /// Zeichnet einen Kraftpfeil: Schaft (Line) + geschlossene Spitze (Polygon) + Beschriftung.
    /// Der Pfeil hat eine feste Länge in Pixeln (lengthPx) und eine feste Spitzengröße (14 px).
    /// </summary>
    public static void AddForceArrow(
        Canvas canvas,
        Point startPos,
        double fx,
        double fy,
        double lengthPx = 65.0,
        Brush? arrowColor = null)
    {
        arrowColor ??= DefaultArrowBrush;

        double norm = Math.Sqrt(fx * fx + fy * fy);
        if (norm < 1e-6)
            return;

        // Einheitsvektor im Bildschirmsystem (Y-Invertierung: Fy nach oben entspricht -y auf Canvas)
        double ux = fx / norm;
        double uy = -fy / norm;

        Point tipPos = new Point(startPos.X + ux * lengthPx, startPos.Y + uy * lengthPx);

        // 1. Schaft als Linie
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

        // 2. Pfeilspitze über Drehmatrix (Länge 14 px, Halbwinkel 15°)
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

        // 3. Beschriftung (Kraftbetrag) hinter der Spitze
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
    // HORIZONTALE MASSKETTE (DIN 406)
    // =====================================================================

    /// <summary>
    /// Horizontale Bemaßung nach DIN 406:
    /// 1. zwei Maßhilfslinien (senkrecht von den Knoten nach unten mit Überstand)
    /// 2. eine waagerechte Maßlinie
    /// 3. zwei schlanke Maßpfeile
    /// 4. Maßzahl mittig über der Maßlinie
    /// </summary>
    public static void AddHorizontalDimensionLine(
        Canvas canvas,
        Point p1,
        Point p2,
        double yScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= DefaultDimBrush;

        // 1. Maßhilfslinien (senkrecht)
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

        // 2. Waagerechte Maßlinie
        canvas.Children.Add(new Line
        {
            X1 = p1.X,
            Y1 = yScreen,
            X2 = p2.X,
            Y2 = yScreen,
            Stroke = strokeColor,
            StrokeThickness = 1.2
        });

        // 3. Schlanke Maßpfeile links und rechts
        double arrowLen = 10.0;
        double arrowH = 3.0;

        // Pfeil links (zeigt nach links zu p1)
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

        // Pfeil rechts (zeigt nach rechts zu p2)
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

        // 4. Maßzahl mittig über der Maßlinie
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
    // VERTIKALE MASSKETTE (DIN 406 - Stufe A, Aufgabe 3)
    // =====================================================================

    /// <summary>
    /// Vertikale Bemaßung nach DIN 406:
    /// 1. zwei waagerechte Maßhilfslinien (mit Überstand nach links)
    /// 2. eine senkrechte Maßlinie bei xScreen
    /// 3. Maßpfeile oben und unten (Polygon)
    /// 4. Maßzahl zentriert links neben der Maßlinie
    /// </summary>
    public static void AddVerticalDimensionLine(
        Canvas canvas,
        Point pBottom,
        Point pTop,
        double xScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= DefaultDimBrush;

        // 1. Waagerechte Maßhilfslinien: Start am Knoten, Ende 6 px links über der Maßlinie
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

        // 2. Senkrechte Maßlinie
        canvas.Children.Add(new Line
        {
            X1 = xScreen,
            Y1 = pBottom.Y,
            X2 = xScreen,
            Y2 = pTop.Y,
            Stroke = strokeColor,
            StrokeThickness = 1.2
        });

        // 3. Maßpfeile oben und unten
        double arrowLen = 10.0;
        double arrowW = 3.0;

        // Unterer Pfeil: Spitze unten bei pBottom.Y, Basis weiter oben (kleineres Y)
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(xScreen, pBottom.Y),
                new Point(xScreen - arrowW, pBottom.Y - arrowLen),
                new Point(xScreen + arrowW, pBottom.Y - arrowLen)
            },
            Fill = strokeColor
        });

        // Oberer Pfeil: Spitze oben bei pTop.Y, Basis weiter unten (größeres Y)
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection
            {
                new Point(xScreen, pTop.Y),
                new Point(xScreen - arrowW, pTop.Y + arrowLen),
                new Point(xScreen + arrowW, pTop.Y + arrowLen)
            },
            Fill = strokeColor
        });

        // 4. Maßzahl mittig links neben der senkrechten Maßlinie
        var textBlock = new TextBlock
        {
            Text = dimensionText,
            Foreground = strokeColor,
            FontSize = 11,
            FontWeight = FontWeights.Normal
        };
        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textW = textBlock.DesiredSize.Width;
        double textH = textBlock.DesiredSize.Height;

        Canvas.SetLeft(textBlock, xScreen - textW - 5.0);
        Canvas.SetTop(textBlock, (pBottom.Y + pTop.Y) / 2.0 - textH / 2.0);
        canvas.Children.Add(textBlock);
    }
}