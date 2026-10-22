using System.Windows;

namespace VectorTrussCanvas.Geometry;

/// <summary>
/// Affine Koordinatentransformation zwischen
///   - Weltkoordinaten [m]    : kontinuierlich, Y zeigt nach OBEN, Ursprung beliebig
///   - Bildschirmkoordinaten [px]: diskret, Y zeigt nach UNTEN, Ursprung links oben im Canvas
///
/// Eigenschaften:
///   1. Isotrope Skalierung  -> ein einziger Maßstab s für X und Y (keine Verzerrung)
///   2. Zentrierung          -> das Modell liegt immer mittig im Canvas
///   3. Y-Invertierung       -> größeres Y in der Welt = kleineres Y auf dem Bildschirm
///
/// STUFE A, Aufgabe 1: Hier muss nichts geändert werden. Die Klasse wird nur analysiert.
/// </summary>
public class Transform2D
{
    // ---------------------------------------------------------------
    // Bounding Box des Modells in Weltkoordinaten [m]
    // (wird in Update() gesetzt, von außen nur lesbar)
    // ---------------------------------------------------------------
    public double XMin { get; private set; }
    public double XMax { get; private set; }
    public double YMin { get; private set; }
    public double YMax { get; private set; }   // YMax wird für die Y-Invertierung gebraucht!

    // ---------------------------------------------------------------
    // Größe des Canvas in Pixeln
    // ---------------------------------------------------------------
    public double CanvasWidth { get; private set; }
    public double CanvasHeight { get; private set; }

    // ---------------------------------------------------------------
    // Randabstand (Margin) in Pixeln. Wird von außen gesetzt
    // (MainWindow setzt aktuell 70 px).
    // ---------------------------------------------------------------
    public double MarginX { get; set; } = 50.0;
    public double MarginY { get; set; } = 50.0;

    // ---------------------------------------------------------------
    // Ergebnis von Update():
    //   Scale   = Pixel pro Meter [px/m]
    //   OffsetX = Abstand vom linken Canvas-Rand zum Modell [px]
    //   OffsetY = Abstand vom oberen Canvas-Rand zum Modell [px]
    // ---------------------------------------------------------------
    public double Scale { get; private set; } = 1.0;
    public double OffsetX { get; private set; } = 0.0;
    public double OffsetY { get; private set; } = 0.0;

    /// <summary>
    /// Berechnet Maßstab und Zentrierungs-Offsets aus Bounding Box und Canvas-Größe.
    ///
    /// Formeln:
    ///   s   = min( (w_canvas - 2*margin_x) / W_welt , (h_canvas - 2*margin_y) / H_welt )
    ///   off_x = (w_canvas - W_welt * s) / 2
    ///   off_y = (h_canvas - H_welt * s) / 2
    /// </summary>
    public void Update(double xMin, double xMax, double yMin, double yMax, double canvasWidth, double canvasHeight)
    {
        XMin = xMin;
        XMax = xMax;
        YMin = yMin;
        YMax = yMax;

        // Schutz vor winzigen Fenstern (verhindert Division durch 0 und negative Maßstäbe)
        CanvasWidth = Math.Max(canvasWidth, 100.0);
        CanvasHeight = Math.Max(canvasHeight, 100.0);

        // Ausdehnung des Modells in Metern (mindestens 1e-4, damit nie durch 0 geteilt wird)
        double worldW = Math.Max(XMax - XMin, 1e-4);
        double worldH = Math.Max(YMax - YMin, 1e-4);

        // Nutzbare Zeichenfläche = Canvas minus Rand links/rechts bzw. oben/unten
        double availW = Math.Max(CanvasWidth - 2.0 * MarginX, 10.0);
        double availH = Math.Max(CanvasHeight - 2.0 * MarginY, 10.0);

        // ISOTROPES SKALIEREN: Der KLEINERE der beiden Faktoren gewinnt.
        // Dadurch passt das Modell in beiden Richtungen komplett hinein
        // und das Seitenverhältnis bleibt exakt erhalten.
        double sx = availW / worldW;
        double sy = availH / worldH;
        Scale = Math.Min(sx, sy);

        // Tatsächlich gezeichnete Größe des Modells in Pixeln
        double drawnW = worldW * Scale;
        double drawnH = worldH * Scale;

        // Zentrierung: Der übrige Platz wird gleichmäßig links/rechts bzw. oben/unten verteilt.
        // (Der Margin steckt schon im Maßstab, deshalb wird er hier nicht nochmal addiert.)
        OffsetX = (CanvasWidth - drawnW) / 2.0;
        OffsetY = (CanvasHeight - drawnH) / 2.0;
    }

    /// <summary>
    /// Welt [m] -> Bildschirm [px].
    ///   x_s = off_x + (x_w - X_min) * s
    ///   y_s = off_y + (Y_max - y_w) * s     <- Y-INVERTIERUNG: (Y_max - y_w) statt (y_w - Y_min)
    /// </summary>
    public Point WorldToScreen(double xW, double yW)
    {
        double xs = OffsetX + (xW - XMin) * Scale;
        double ys = OffsetY + (YMax - yW) * Scale;
        return new Point(xs, ys);
    }

    /// <summary>
    /// Bildschirm [px] -> Welt [m]. Exakte Umkehrfunktion von WorldToScreen.
    ///   x_w = X_min + (x_s - off_x) / s
    ///   y_w = Y_max - (y_s - off_y) / s
    /// Wird für Maus-Interaktion (Knoten ziehen, Picking) benötigt.
    /// </summary>
    public (double Xw, double Yw) ScreenToWorld(double xS, double yS)
    {
        // Sicherheitsabfrage: bei Scale ~ 0 wäre die Division ungültig
        if (Math.Abs(Scale) < 1e-12)
            return (0.0, 0.0);

        double xw = XMin + (xS - OffsetX) / Scale;
        double yw = YMax - (yS - OffsetY) / Scale;
        return (xw, yw);
    }
}