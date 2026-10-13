using System.Globalization;
using System.IO;
using KinematikEuler.Simulation;

namespace KinematikEuler.Output;

public static class CSVExporter
{
    public static void Export(string filePath, List<TrajectoryPoint> points)
    {
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("t_s;x_m;v_ms;a_ms2;f_daempf_N");

        foreach (var p in points)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F6};{1:F6};{2:F6};{3:F6};{4:F6}",
                p.Time, p.X, p.V, p.A, p.FDaempf));
        }
    }
}