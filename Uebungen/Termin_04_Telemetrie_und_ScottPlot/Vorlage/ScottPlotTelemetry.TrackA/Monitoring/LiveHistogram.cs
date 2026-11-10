namespace ScottPlotTelemetry.TrackA.Monitoring;

/// <summary>
/// Fest dimensioniertes Live-Histogramm: Bereich und Klassenanzahl werden einmal festgelegt,
/// Add() erhöht nur einen Zähler -> O(1) und 0 Byte Heap pro Messwert.
/// </summary>
public sealed class LiveHistogram
{
    public int BinCount { get; }
    public double Min { get; }
    public double Max { get; }
    public double BinWidth { get; }             // Klassenbreite h
    public long[] Counts { get; }               // einmalig angelegt
    public long Total { get; private set; }     // Anzahl aller Werte n
    public long MaxCount { get; private set; }  // höchster Balken (für die Y-Achse)

    public LiveHistogram(int binCount, double min, double max)
    {
        BinCount = binCount;
        Min = min;
        Max = max;
        BinWidth = (max - min) / binCount;
        Counts = new long[binCount];
    }

    public void Add(double x)
    {
        // Klassenindex berechnen; Werte außerhalb landen im Randbin (kein Wert geht verloren)
        int i = (int)Math.Floor((x - Min) / BinWidth);
        if (i < 0) i = 0;
        if (i >= BinCount) i = BinCount - 1;

        Counts[i]++;
        Total++;
        if (Counts[i] > MaxCount) MaxCount = Counts[i];
    }

    public double BinCenter(int i) => Min + (i + 0.5) * BinWidth;

    public void Reset()
    {
        Array.Clear(Counts);
        Total = 0;
        MaxCount = 0;
    }
}
