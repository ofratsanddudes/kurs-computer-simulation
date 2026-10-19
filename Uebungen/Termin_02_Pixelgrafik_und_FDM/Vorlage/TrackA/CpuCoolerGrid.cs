using System;

namespace TrackA;

public sealed class CpuCoolerGrid
{
    public const int Width = 200;
    public const int Height = 200;
    public const float Dx = 0.0002f; // 0.2 mm

    // Flache 1D-Arrays für maximale Cache-Lokalität und Performance
    private float[] _tCurrent;
    private float[] _tNext;

    private readonly MaterialType[] _materialMap;
    private readonly float[] _alphaMap;
    private readonly float[] _lambdaMap;
    private readonly float[] _heatSource;
    private readonly bool[] _isSolid;

    public float Dt { get; set; } = 0.000075f; // 75 µs
    public float AmbientTemp { get; set; } = 25.0f;
    public float HeatTransferCoeff { get; set; } = 250.0f;
    public float MaxCpuTemperature { get; private set; } = 25.0f;

    public float[] CurrentTemperatures => _tCurrent;
    public MaterialType[] Materials => _materialMap;

    public CpuCoolerGrid()
    {
        int size = Width * Height;
        _tCurrent = new float[size];
        _tNext = new float[size];
        _materialMap = new MaterialType[size];
        _alphaMap = new float[size];
        _lambdaMap = new float[size];
        _heatSource = new float[size];
        _isSolid = new bool[size];

        InitializeGeometryAndMaterials();
        ResetTemperatures();
    }

    public void ResetTemperatures()
    {
        Array.Fill(_tCurrent, AmbientTemp);
        Array.Fill(_tNext, AmbientTemp);
        MaxCpuTemperature = AmbientTemp;
    }

    private void InitializeGeometryAndMaterials()
    {
        int size = Width * Height;
        for (int i = 0; i < size; i++)
        {
            _materialMap[i] = MaterialType.Air;
            _alphaMap[i] = 0f;
            _lambdaMap[i] = 0.026f;
            _heatSource[i] = 0f;
            _isSolid[i] = false;
        }

        // Zeitskalierungsfaktor für zügiges Einschwingen (< 1 Minute)
        // Erhöht die effektive Aufheizrate, ohne die stationäre Physik zu verzerren
        const float timeBoostSource = 850.0f;

        // 1. CPU-Die (40x10 px): y: 188..197, x: 80..119
        for (int y = 188; y <= 197; y++)
        {
            for (int x = 80; x < 120; x++)
            {
                SetCell(x, y, MaterialType.Silicon, 8.9e-5f, 148.0f, timeBoostSource);
            }
        }

        // 2. TIM Wärmeleitpaste (2 px Dicke): y: 186..187, x: 80..119
        for (int y = 186; y <= 187; y++)
        {
            for (int x = 80; x < 120; x++)
            {
                SetCell(x, y, MaterialType.Tim, 2.5e-6f, 4.5f, 0f);
            }
        }

        // 3. Kupfer Heatspreader (120x20 px): y: 166..185, x: 40..159
        for (int y = 166; y <= 185; y++)
        {
            for (int x = 40; x < 160; x++)
            {
                SetCell(x, y, MaterialType.Copper, 1.15e-4f, 398.0f, 0f);
            }
        }

        // 4. Aluminium Basisplatte (160x20 px): y: 146..165, x: 20..179
        for (int y = 146; y <= 165; y++)
        {
            for (int x = 20; x < 180; x++)
            {
                SetCell(x, y, MaterialType.Aluminum, 9.7e-5f, 237.0f, 0f);
            }
        }

        // 5. 10 Aluminium Kühlrippen (Höhe 126 px, Breite 6 px): y: 20..145
        for (int fin = 0; fin < 10; fin++)
        {
            int startX = 22 + fin * 16;
            for (int y = 20; y <= 145; y++)
            {
                for (int x = startX; x < startX + 6; x++)
                {
                    if (x < Width)
                    {
                        SetCell(x, y, MaterialType.Aluminum, 9.7e-5f, 237.0f, 0f);
                    }
                }
            }
        }
    }

    private void SetCell(int x, int y, MaterialType mat, float alpha, float lambda, float source)
    {
        int idx = y * Width + x;
        _materialMap[idx] = mat;
        _alphaMap[idx] = alpha;
        _lambdaMap[idx] = lambda;
        _heatSource[idx] = source;
        _isSolid[idx] = true;
    }

    public void Step()
    {
        float invDx2 = 1.0f / (Dx * Dx);
        float dt = Dt;
        float h = HeatTransferCoeff;
        float tInf = AmbientTemp;
        float maxDieTemp = -1e9f;

        float hDx = h * Dx;

        for (int y = 0; y < Height; y++)
        {
            int rowIdx = y * Width;
            for (int x = 0; x < Width; x++)
            {
                int idx = rowIdx + x;

                if (!_isSolid[idx])
                {
                    _tNext[idx] = tInf;
                    continue;
                }

                float tCenter = _tCurrent[idx];
                float lambdaCenter = _lambdaMap[idx];
                float alphaCenter = _alphaMap[idx];

                // 1. Nachbar Links
                float tLeft;
                if (x == 0) tLeft = tCenter;
                else if (!_isSolid[idx - 1]) tLeft = (lambdaCenter * tCenter + hDx * tInf) / (lambdaCenter + hDx);
                else tLeft = _tCurrent[idx - 1];

                // 2. Nachbar Rechts
                float tRight;
                if (x == Width - 1) tRight = tCenter;
                else if (!_isSolid[idx + 1]) tRight = (lambdaCenter * tCenter + hDx * tInf) / (lambdaCenter + hDx);
                else tRight = _tCurrent[idx + 1];

                // 3. Nachbar Oben
                float tUp;
                if (y == 0) tUp = tCenter;
                else if (!_isSolid[idx - Width]) tUp = (lambdaCenter * tCenter + hDx * tInf) / (lambdaCenter + hDx);
                else tUp = _tCurrent[idx - Width];

                // 4. Nachbar Unten (PCB: y == Height - 1 ist ideal adiabat isoliert)
                float tDown;
                if (y == Height - 1) tDown = tCenter;
                else if (!_isSolid[idx + Width]) tDown = (lambdaCenter * tCenter + hDx * tInf) / (lambdaCenter + hDx);
                else tDown = _tCurrent[idx + Width];

                // FDM 5-Punkt-Stern
                float laplace = tLeft + tRight + tUp + tDown - 4.0f * tCenter;
                float s = alphaCenter * dt * invDx2;
                float tNew = tCenter + s * laplace + dt * _heatSource[idx];

                _tNext[idx] = tNew;

                if (_materialMap[idx] == MaterialType.Silicon && tNew > maxDieTemp)
                {
                    maxDieTemp = tNew;
                }
            }
        }

        // Puffer tauschen
        (_tCurrent, _tNext) = (_tNext, _tCurrent);
        MaxCpuTemperature = maxDieTemp > -1e8f ? maxDieTemp : AmbientTemp;
    }
}