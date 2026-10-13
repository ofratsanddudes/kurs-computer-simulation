using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KinematikEuler.Simulation;

namespace KinematikEuler.Output;

public static class ConsoleReport
{
    public static void PrintResults(List<SimulationResult> results)
    {
        // Spaltenbreiten: Solver (13), dt (9), a_min (14), x_max (12), Status (10)
        const string divider = "+---------------+-----------+----------------+--------------+------------+";

        Console.WriteLine("\n" + divider);
        Console.WriteLine("| Solver        | dt        | a_min [m/s²]   | x_max [m]    | Status     |");
        Console.WriteLine(divider);

        foreach (var r in results)
        {
            string dtStr = r.Dt switch
            {
                >= 1e-3 => $"{r.Dt * 1e3:F1} ms",
                _ => $"{r.Dt * 1e6:F0} µs"
            };

            string aMinStr = r.IsStable
                ? string.Format(CultureInfo.InvariantCulture, "{0,14:F2}", r.AMin)
                : "           NaN";

            string xMaxStr = r.IsStable
                ? string.Format(CultureInfo.InvariantCulture, "{0,12:F4}", r.XMax)
                : "         NaN";

            string statusStr = r.IsStable ? "STABIL" : "INSTABIL";

            // Die gesamte Tabellenzeile wird als ein String gedruckt
            string row = $"| {r.SolverName,-13} | {dtStr,-9} | {aMinStr} | {xMaxStr} | {statusStr,-10} |";

            if (r.IsStable)
            {
                Console.WriteLine(row);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(row);
                Console.ResetColor();
            }
        }

        Console.WriteLine(divider + "\n");

        PrintStabilityLimits(results);
    }

    private static void PrintStabilityLimits(List<SimulationResult> results)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== STABILITÄTSGRENZEN (dt_krit) ===");
        Console.ResetColor();

        var grouped = results.GroupBy(r => r.SolverName);

        foreach (var group in grouped)
        {
            string solverName = group.Key;
            var runs = group.OrderBy(r => r.Dt).ToList();

            var maxStable = runs.Where(r => r.IsStable).LastOrDefault();
            var minUnstable = runs.Where(r => !r.IsStable).FirstOrDefault();

            Console.Write($"  • {solverName,-13}: ");

            if (maxStable != null && minUnstable != null)
            {
                string dtStableStr = FormatDt(maxStable.Dt);
                string dtUnstableStr = FormatDt(minUnstable.Dt);

                Console.WriteLine($"Stabil bis {dtStableStr} | Instabil ab {dtUnstableStr}");
            }
            else if (minUnstable == null)
            {
                Console.WriteLine("Stabil für alle getesteten Zeitschritte");
            }
            else
            {
                Console.WriteLine("Instabil für alle getesteten Zeitschritte");
            }
        }

        Console.WriteLine();
    }

    private static string FormatDt(double dt)
    {
        return dt >= 1e-3 ? $"{dt * 1e3:F1} ms" : $"{dt * 1e6:F0} µs";
    }
}