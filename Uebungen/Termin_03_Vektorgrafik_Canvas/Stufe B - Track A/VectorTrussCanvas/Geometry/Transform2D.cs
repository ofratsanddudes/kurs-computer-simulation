using System.Windows;

namespace VectorTrussCanvas.Geometry;

/// <summary>
/// Affine Koordinatentransformation zwischen
///   - Weltkoordinaten [m]       : Y zeigt nach OBEN
///   - Bildschirmkoordinaten [px]: Y zeigt nach UNTEN, Ursprung links oben im Canvas
///
/// Zwei Stufen:
///   1. AUTO-FIT  (Update)       : isotroper Maßstab + Zentrierung, damit das Modell ins Fenster passt
///   2. KAMERA    (Zoom, Pan)    : zusätzlicher Zoomfaktor und Verschiebung auf dem Bildschirm
///
/// Warum Zoom/Pan HIER und nicht als MatrixTransform am Canvas?
///   Eine MatrixTransform am Canvas würde auch Schrift, Linienstärken und Pfeilspitzen mitskalieren.
///   Die Aufgabe verlangt aber, dass die Pfeilspitze IMMER ihre feste Pixelgröße behält.
///   Wenn Zoom/Pan in der Welt->Bildschirm-Abbildung stecken, bleiben alle Shapes pixelgenau
///   und ScreenToWorld (Picking, Dragging) ist automatisch korrekt.
///
/// Formeln (b = Auto-Fit-Position, S = Maßstab, z = Zoom, p = Pan):
///   b_x = off_x + (x_w - X_min) * S          b_y = off_y + (Y_max - y_w) * S
///   x_s = p_x + z * b_x                      y_s = p_y + z * b_y
/// </summary>
public class Transform2D
{
    // Bounding Box des Modells [m]
    public double XMin { get; private set; }
    public double XMax { get; private set; }
    public double YMin { get; private set; }
    public double YMax { get; private set; }

    // Canvas-Größe [px]
    public double CanvasWidth { get; private set; }
    public double CanvasHeight { get; private set; }

    // Randabstand [px]
    public double MarginX { get; set; } = 50.0;
    public double MarginY { get; set; } = 50.0;

    // Ergebnis des Auto-Fit
    public double Scale { get; private set; } = 1.0;    // [px/m]
    public double OffsetX { get; private set; } = 0.0;  // [px]
    public double OffsetY { get; private set; } = 0.0;  // [px]

    // Kamera (Track A): Zoomfaktor und Verschiebung
    public const double MinZoom = 0.2;
    public const double MaxZoom = 40.0;
    public double Zoom { get; private set; } = 1.0;     // 1.0 = Auto-Fit-Ansicht
    public double PanX { get; private set; } = 0.0;     // [px]
    public double PanY { get; private set; } = 0.0;     // [px]

    /// <summary>Gesamtmaßstab inklusive Zoom [px/m].</summary>
    public double EffectiveScale => Scale * Zoom;

    /// <summary>
    /// Auto-Fit: berechnet Maßstab und Zentrierung aus Bounding Box und Canvas-Größe.
    ///   s     = min( (w - 2*margin_x) / W_welt , (h - 2*margin_y) / H_welt )
    ///   off_x = (w - W_welt * s) / 2
    ///   off_y = (h - H_welt * s) / 2
    /// Die Kamera (Zoom/Pan) bleibt dabei unverändert.
    /// </summary>
    public void Update(double xMin, double xMax, double yMin, double yMax, double canvasWidth, double canvasHeight)
    {
        XMin = xMin;
        XMax = xMax;
        YMin = yMin;
        YMax = yMax;
        CanvasWidth = Math.Max(canvasWidth, 100.0);
        CanvasHeight = Math.Max(canvasHeight, 100.0);

        double worldW = Math.Max(XMax - XMin, 1e-4);
        double worldH = Math.Max(YMax - YMin, 1e-4);

        double availW = Math.Max(CanvasWidth - 2.0 * MarginX, 10.0);
        double availH = Math.Max(CanvasHeight - 2.0 * MarginY, 10.0);

        // Isotrop: der kleinere Faktor gewinnt, das Seitenverhältnis bleibt 1:1
        double sx = availW / worldW;
        double sy = availH / worldH;
        Scale = Math.Min(sx, sy);

        double drawnW = worldW * Scale;
        double drawnH = worldH * Scale;

        OffsetX = (CanvasWidth - drawnW) / 2.0;
        OffsetY = (CanvasHeight - drawnH) / 2.0;
    }

    /// <summary>
    /// Welt [m] -> Bildschirm [px], inklusive Y-Invertierung und Kamera.
    /// </summary>
    public Point WorldToScreen(double xW, double yW)
    {
        // 1. Auto-Fit-Position
        double bx = OffsetX + (xW - XMin) * Scale;
        double by = OffsetY + (YMax - yW) * Scale;   // Y-Invertierung

        // 2. Kamera: erst zoomen, dann verschieben
        return new Point(PanX + Zoom * bx, PanY + Zoom * by);
    }

    /// <summary>
    /// Bildschirm [px] -> Welt [m]. Exakte Umkehrung von WorldToScreen
    /// (erst Kamera rückgängig machen, dann Auto-Fit invertieren).
    /// </summary>
    public (double Xw, double Yw) ScreenToWorld(double xS, double yS)
    {
        if (Math.Abs(Scale) < 1e-12 || Math.Abs(Zoom) < 1e-12)
            return (0.0, 0.0);

        // 1. Kamera invertieren
        double bx = (xS - PanX) / Zoom;
        double by = (yS - PanY) / Zoom;

        // 2. Auto-Fit invertieren
        double xw = XMin + (bx - OffsetX) / Scale;
        double yw = YMax - (by - OffsetY) / Scale;
        return (xw, yw);
    }

    /// <summary>
    /// Zoom um einen Fixpunkt (Mauszeiger): Der Weltpunkt unter dem Cursor bleibt am selben Pixel.
    ///   p' = c - (c - p) * (z'/z)
    /// </summary>
    /// <param name="cursorX">Mausposition X im Canvas [px]</param>
    /// <param name="cursorY">Mausposition Y im Canvas [px]</param>
    /// <param name="factor">Zoomschritt, z. B. 1.15 (hinein) oder 1/1.15 (heraus)</param>
    public void ZoomAt(double cursorX, double cursorY, double factor)
    {
        double newZoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        double f = newZoom / Zoom;

        PanX = cursorX - (cursorX - PanX) * f;
        PanY = cursorY - (cursorY - PanY) * f;
        Zoom = newZoom;
    }

    /// <summary>Verschiebt die Ansicht um (dx, dy) Pixel.</summary>
    public void PanBy(double dx, double dy)
    {
        PanX += dx;
        PanY += dy;
    }

    /// <summary>Setzt die Kamera zurück (Zoom 1, kein Pan = reine Auto-Fit-Ansicht).</summary>
    public void ResetCamera()
    {
        Zoom = 1.0;
        PanX = 0.0;
        PanY = 0.0;
    }
}