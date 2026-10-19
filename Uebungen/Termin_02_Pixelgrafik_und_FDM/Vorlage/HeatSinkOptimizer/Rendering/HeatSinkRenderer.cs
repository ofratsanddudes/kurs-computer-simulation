using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HeatSinkOptimizer.Simulation;

namespace HeatSinkOptimizer.Rendering;

/// <summary>Zeichnet das Gitter in eine WriteableBitmap (Bgra32), ohne Allokation pro Frame.</summary>
public class HeatSinkRenderer
{
    private const uint MagentaError = 0xFFFF00FF;   // NaN / Unendlich
    private const uint AirColor = 0xFF101820;

    // Farbe je Material für die Geometrie-Ansicht, Index = (int)Material
    private static readonly uint[] MaterialColors =
    {
        0xFF101820, // Air
        0xFFD03030, // Silicon
        0xFFE8D040, // Tim
        0xFFD07820, // Copper
        0xFF8090A8  // Aluminium
    };

    private readonly WriteableBitmap _bitmap;
    private readonly int _width;
    private readonly int _height;

    // Look-Up-Table: 256 vorberechnete Temperaturfarben, Index 0 = kalt, 255 = heiß
    private readonly uint[] _colorLut = new uint[256];

    public WriteableBitmap Bitmap => _bitmap;

    public HeatSinkRenderer(int width, int height)
    {
        _width = width;
        _height = height;
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);

        for (int i = 0; i < 256; i++)
            _colorLut[i] = NormToColor(i / 255.0f);
    }

    /// <summary>Zeigt die Materialzonen (zur Kontrolle der Geometrie).</summary>
    public unsafe void RenderMaterials(HeatSinkGrid grid)
    {
        _bitmap.Lock();
        try
        {
            uint* back = (uint*)_bitmap.BackBuffer;
            int stride = _bitmap.BackBufferStride / 4;

            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    back[y * stride + x] = MaterialColors[(int)grid.GetMaterial(x, y)];

            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
        }
        finally { _bitmap.Unlock(); }
    }

    /// <summary>Zeigt das Temperaturfeld: minT = blau, maxT = rot. Luft bleibt dunkel.</summary>
    public unsafe void RenderTemperatures(HeatSinkGrid grid, float minT, float maxT)
    {
        float[] t = grid.GetCurrentGrid();
        float scale = 255.0f / Math.Max(maxT - minT, 1e-4f);

        _bitmap.Lock();
        try
        {
            uint* back = (uint*)_bitmap.BackBuffer;
            int stride = _bitmap.BackBufferStride / 4;

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    uint color;
                    if (grid.GetMaterial(x, y) == Material.Air)
                    {
                        color = AirColor;
                    }
                    else
                    {
                        float v = t[y * _width + x];
                        if (float.IsNaN(v) || float.IsInfinity(v))
                            color = MagentaError;   // numerische Explosion sichtbar machen
                        else
                            color = _colorLut[(int)Math.Clamp((v - minT) * scale, 0.0f, 255.0f)];
                    }
                    back[y * stride + x] = color;
                }
            }

            _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
        }
        finally { _bitmap.Unlock(); }
    }

    /// <summary>4-Stufen-Heatmap: Blau -> Cyan -> Grün -> Gelb -> Rot (norm in [0, 1]).</summary>
    private static uint NormToColor(float norm)
    {
        byte r, g, b;

        if (norm < 0.25f) { float f = norm / 0.25f; r = 0; g = (byte)(f * 255); b = 255; }
        else if (norm < 0.5f) { float f = (norm - 0.25f) / 0.25f; r = 0; g = 255; b = (byte)((1.0f - f) * 255); }
        else if (norm < 0.75f) { float f = (norm - 0.5f) / 0.25f; r = (byte)(f * 255); g = 255; b = 0; }
        else { float f = (norm - 0.75f) / 0.25f; r = 255; g = (byte)((1.0f - f) * 255); b = 0; }

        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
    }
}