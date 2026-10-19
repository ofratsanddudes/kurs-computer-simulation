using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TrackA;

public sealed class CpuHeatRenderer
{
    private readonly WriteableBitmap _bitmap;
    private readonly uint[] _colorLut = new uint[256];
    private readonly Int32Rect _rect;

    public WriteableBitmap Bitmap => _bitmap;

    public CpuHeatRenderer(int width, int height)
    {
        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        _rect = new Int32Rect(0, 0, width, height);
        BuildLookupTable();
    }

    private void BuildLookupTable()
    {
        for (int i = 0; i < 256; i++)
        {
            float val = i / 255.0f;
            byte r, g, b;

            if (val < 0.2f)
            {
                float local = val / 0.2f;
                r = 0; g = (byte)(local * 255); b = 255;
            }
            else if (val < 0.4f)
            {
                float local = (val - 0.2f) / 0.2f;
                r = 0; g = 255; b = (byte)((1.0f - local) * 255);
            }
            else if (val < 0.7f)
            {
                float local = (val - 0.4f) / 0.3f;
                r = (byte)(local * 255); g = 255; b = 0;
            }
            else if (val < 0.9f)
            {
                float local = (val - 0.7f) / 0.2f;
                r = 255; g = (byte)((1.0f - local) * 255); b = 0;
            }
            else
            {
                float local = (val - 0.9f) / 0.1f;
                r = 255; g = (byte)(local * 255); b = (byte)(local * 255);
            }

            _colorLut[i] = (0xFFu << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        }
    }

    public unsafe void Render(CpuCoolerGrid grid, float minTemp = 25.0f, float maxTemp = 95.0f)
    {
        _bitmap.Lock();
        try
        {
            uint* pBackBuffer = (uint*)_bitmap.BackBuffer.ToPointer();
            int strideWords = _bitmap.BackBufferStride / 4;
            float tempRange = Math.Max(1.0f, maxTemp - minTemp);

            var temps = grid.CurrentTemperatures;
            var mats = grid.Materials;

            for (int y = 0; y < CpuCoolerGrid.Height; y++)
            {
                uint* row = pBackBuffer + (y * strideWords);
                int gridOffset = y * CpuCoolerGrid.Width;

                for (int x = 0; x < CpuCoolerGrid.Width; x++)
                {
                    int idx = gridOffset + x;
                    float t = temps[idx];

                    if (float.IsNaN(t) || float.IsInfinity(t))
                    {
                        row[x] = 0xFFFF00FFu;
                        continue;
                    }

                    if (mats[idx] == MaterialType.Air)
                    {
                        row[x] = 0xFF12151Fu;
                        continue;
                    }

                    int lutIdx = (int)((t - minTemp) / tempRange * 255.0f);
                    lutIdx = Math.Clamp(lutIdx, 0, 255);

                    row[x] = _colorLut[lutIdx];
                }
            }

            _bitmap.AddDirtyRect(_rect);
        }
        finally
        {
            _bitmap.Unlock();
        }
    }
}