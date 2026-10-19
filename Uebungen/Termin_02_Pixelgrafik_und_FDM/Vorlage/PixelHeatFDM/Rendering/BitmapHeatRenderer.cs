using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelHeatFDM.Simulation;

namespace PixelHeatFDM.Rendering;

/// <summary>
/// Performanter Bitmap-Renderer für 2D-Temperaturfelder auf Basis von WriteableBitmap.
/// Ablauf pro Frame: Temperatur -> Farbindex -> LUT-Farbe -> Zwischenpuffer -> BackBuffer.
/// </summary>
public class BitmapHeatRenderer
{
    // Signalfarbe für numerische Instabilität (NaN oder ±Unendlich), Format 0xAARRGGBB
    private const uint MagentaError = 0xFFFF00FF;
    private const uint WallColor = 0xFF404040;   // dunkelgrau für Isolator-Wände
    private readonly WriteableBitmap _bitmap;
    private readonly int _width;
    private readonly int _height;

    // Zwischenpuffer: ein uint pro Pixel (Bgra32), einmalig angelegt und pro Frame wiederverwendet
    // -> keine Allokation im Render-Loop (0 B GC)
    private readonly uint[] _pixelBuffer;

    // Look-Up-Table (LUT): 256 vorberechnete Farben, Index 0 = kälteste, 255 = heißeste Stufe
    private readonly uint[] _colorLut = new uint[256];

    public WriteableBitmap Bitmap => _bitmap;

    public BitmapHeatRenderer(int width, int height)
    {
        _width = width;
        _height = height;

        // 32 Bit pro Pixel (Blau, Grün, Rot, Alpha), 96 DPI = 1 Pixel pro Einheit
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _pixelBuffer = new uint[width * height];

        // LUT einmalig füllen: Stufe i entspricht dem normierten Wert u = i / 255 im Bereich [0, 1].
        // Wir nutzen dafür die bekannte Farbfunktion, daher sieht das Ergebnis exakt aus wie vorher.
        for (int i = 0; i < 256; i++)
            _colorLut[i] = MapTemperatureToColor(i / 255.0f, 0.0f, 1.0f);
    }

    /// <summary>
    /// Überträgt das 2D-Temperaturgitter in den Bitmap-BackBuffer.
    /// </summary>
    /// <param name="grid">Simulationsgitter, aus dem die aktuellen Temperaturen gelesen werden.</param>
    /// <param name="minTemp">Temperatur, die der ersten LUT-Stufe (Blau) entspricht.</param>
    /// <param name="maxTemp">Temperatur, die der letzten LUT-Stufe (Rot) entspricht.</param>
    public unsafe void Render(HeatGrid grid, float minTemp = 20.0f, float maxTemp = 100.0f)
    {
        float[,] tData = grid.GetCurrentGrid();

        // Wertebereich der Skala; Untergrenze verhindert Division durch 0
        float range = Math.Max(maxTemp - minTemp, 1e-4f);

        // Schritt 1: Temperaturen in Farben umrechnen und im Zwischenpuffer ablegen.
        // y außen, x innen: der Zwischenpuffer wird dadurch strikt sequentiell beschrieben (cache-freundlich).
        int index = 0;
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                float t = tData[x, y];

                // Wände zuerst: sie haben keine sinnvolle Temperaturfarbe
                if (grid.IsInsulator(x, y))
                {
                    _pixelBuffer[index++] = WallColor;
                }
                else if (float.IsNaN(t) || float.IsInfinity(t))
                {
                    _pixelBuffer[index++] = MagentaError;
                }
                else
                {
                    int lutIndex = (int)Math.Clamp((t - minTemp) / range * 255.0f, 0.0f, 255.0f);
                    _pixelBuffer[index++] = _colorLut[lutIndex];
                }
            }
        }

        // Schritt 2: Zwischenpuffer in den unmanaged BackBuffer der Bitmap kopieren.
        // Lock() sperrt den Puffer, solange wir hineinschreiben.
        _bitmap.Lock();
        try
        {
            uint* backBuffer = (uint*)_bitmap.BackBuffer;

            // Stride = Bytes pro Zeile (kann wegen Padding größer als Breite * 4 sein).
            // Geteilt durch 4 ergibt die Zeilenlänge in uint-Einheiten, passend zum uint*-Zeiger.
            int stride = _bitmap.BackBufferStride / 4;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    // Pixeladresse im BackBuffer: Zeile * Stride + Spalte
                    backBuffer[y * stride + x] = _pixelBuffer[y * _width + x];
                }
            }

            // Ohne AddDirtyRect meldet WPF keine Änderung und zeigt kein neues Bild an
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
        }
        finally
        {
            // Unlock im finally: Der Puffer wird auch bei einer Exception wieder freigegeben
            _bitmap.Unlock();
        }
    }

    /// <summary>
    /// Berechnet die BGRA32-Farbe für einen Temperaturwert über eine 4-Stufen-Heatmap:
    /// Blau (kalt) -> Cyan -> Grün -> Gelb -> Rot (heiß).
    /// Wird nur noch einmalig zum Befüllen der LUT aufgerufen, nicht mehr pro Pixel und Frame.
    /// </summary>
    public static uint MapTemperatureToColor(float temp, float minTemp, float range)
    {
        if (float.IsNaN(temp) || float.IsInfinity(temp))
            return MagentaError;

        // Normierung auf das Einheitsintervall [0, 1]
        float norm = Math.Clamp((temp - minTemp) / range, 0.0f, 1.0f);

        byte r, g, b;

        // Jedes Viertel des Bereichs ist ein linearer Übergang zwischen zwei Eckfarben;
        // f läuft darin von 0 bis 1.
        if (norm < 0.25f)
        {
            // Blau -> Cyan: Grünanteil steigt
            float f = norm / 0.25f;
            r = 0;
            g = (byte)(f * 255);
            b = 255;
        }
        else if (norm < 0.5f)
        {
            // Cyan -> Grün: Blauanteil sinkt
            float f = (norm - 0.25f) / 0.25f;
            r = 0;
            g = 255;
            b = (byte)((1.0f - f) * 255);
        }
        else if (norm < 0.75f)
        {
            // Grün -> Gelb: Rotanteil steigt
            float f = (norm - 0.5f) / 0.25f;
            r = (byte)(f * 255);
            g = 255;
            b = 0;
        }
        else
        {
            // Gelb -> Rot: Grünanteil sinkt
            float f = (norm - 0.75f) / 0.25f;
            r = 255;
            g = (byte)((1.0f - f) * 255);
            b = 0;
        }

        // Kanäle zu einem 32-Bit-Pixel packen: 0xAARRGGBB, Alpha = 0xFF (voll deckend)
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }
}