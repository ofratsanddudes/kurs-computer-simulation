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
    // =====================================================================
    // KRAFTPFEIL (mit analytischer Pfeilspitzenberechnung)
    // =====================================================================

    /// <summary>
    /// Zeichnet einen Kraftpfeil: Schaft (Line) + geschlossene Spitze (Polygon) + Beschriftung.
    /// Die Spitze behält bei jeder Last exakt ihre feste Pixelgröße (14 px).
    /// </summary>
    public static void AddForceArrow(
        Canvas canvas,
        Point startPos,
        double fx,
        double fy,
        double lengthPx = 65.0,
        Brush? arrowColor = null)
    {
        arrowColor ??= Brushes.Red;

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

    public static void AddHorizontalDimensionLine(
        Canvas canvas,
        Point p1,
        Point p2,
        double yScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= Brushes.LightGray;

        // 1. Maßhilfslinien (senkrecht von den Knoten mit Überstand)
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
    // VERTIKALE MASSKETTE (DIN 406: Maßzahl gedreht, lesbar von rechts)
    // =====================================================================

    public static void AddVerticalDimensionLine(
        Canvas canvas,
        Point pBottom,
        Point pTop,
        double xScreen,
        string dimensionText,
        Brush? strokeColor = null)
    {
        strokeColor ??= Brushes.LightGray;

        // 1. Waagerechte Maßhilfslinien: vom Bezugspunkt bis 6 px links über die Maßlinie hinaus
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

        // 3. Schlanke Maßpfeile oben und unten (Polygon)
        double arrowLen = 10.0;
        double arrowW = 3.0;

        // Unterer Pfeil: zeigt nach unten zu pBottom
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

        // Oberer Pfeil: zeigt nach oben zu pTop
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

        // 4. Maßzahl nach DIN 406: um -90 Grad gedreht (von rechts lesbar)
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

        double yMid = (pBottom.Y + pTop.Y) / 2.0;

        textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
        textBlock.RenderTransform = new RotateTransform(-90);

        // Exakte Ausrichtung: 6 px links der Maßlinie
        Canvas.SetLeft(textBlock, xScreen - 6.0 - textH / 2.0 - textW / 2.0);
        Canvas.SetTop(textBlock, yMid - textH / 2.0);
        canvas.Children.Add(textBlock);
    }
}