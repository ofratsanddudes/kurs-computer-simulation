using KinematikEuler.Simulation;

namespace KinematikEuler.Output;

public static class ConsoleReport
{
    public static void PrintStudy(StudyResult study, double tEnd)
    {
        var reference = study.Reference;

        Console.WriteLine($"\nSimulationsdauer t_end = {tEnd:F2} s");
        Console.WriteLine($"Referenz: {reference.SolverName}, dt = {FormatDt(reference.Dt)}");

        if (reference.IsStable && reference.ContactTime is double tRef)
        {
            Console.WriteLine($"  erster Kontakt bei t = {tRef * 1e3:F3} ms mit v = {reference.ContactVelocity:F3} m/s");
            Console.WriteLine($"  maximaler Anschlagweg = {reference.MaxPenetration * 1e3:F3} mm");
        }
        else
        {
            Console.WriteLine("  Achtung: Die Referenz hat den Anschlag nicht erreicht (t_end zu kurz?).");
        }
        Console.WriteLine();

        string header = Line("Solver", "dt", "Status", "a_min [m/s²]", "s_Anschlag [mm]", "t_Kontakt [ms]", "Δt_K [ms]");
        string divider = new string('-', header.Length);

        Console.WriteLine(divider);
        Console.WriteLine(header);
        Console.WriteLine(divider);

        foreach (var row in study.Rows)
        {
            var r = row.Run;

            if (r.IsStable)
            {
                string tK = r.ContactTime is double tc ? $"{tc * 1e3:F3}" : "-";
                string dK = row.ContactTimeError is double e ? $"{e * 1e3:+0.000;-0.000;0.000}" : "-";

                Console.WriteLine(Line(
                    r.SolverName, FormatDt(r.Dt), "STABIL",
                    $"{r.AMin:F0}", $"{r.MaxPenetration * 1e3:F3}", tK, dK));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(Line(r.SolverName, FormatDt(r.Dt), "INSTABIL", "NaN", "NaN", "-", "-"));
                Console.ResetColor();
            }
        }

        Console.WriteLine(divider);
        Console.WriteLine("Δt_K = Abweichung des Kontaktzeitpunkts von der Referenz (stabil ≠ genau).\n");

        PrintLimits(study);
    }

    private static void PrintLimits(StudyResult study)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== STABILITÄTSGRENZEN (Raster {study.SweepStep * 1e6:F0} µs) ===");
        Console.ResetColor();

        foreach (var l in study.Limits)
        {
            string safeText = double.IsNaN(l.DtSafe)
                ? "kein stabiler Wert gefunden"
                : $"stabil bis {FormatDt(l.DtSafe)}";

            string unstableText = l.DtUnsafe is double u
                ? $"ab {FormatDt(u)} immer instabil"
                : "im getesteten Bereich nie instabil";

            Console.WriteLine($"  • {l.SolverName,-12}: {safeText} | {unstableText}");

            if (l.GrayZoneRuns > 0)
            {
                Console.WriteLine($"      dazwischen {l.GrayZoneRuns} getestete Werte mit unregelmäßigem Verhalten (hängt von der Lage des Zeitgitters ab)");
            }
        }

        Console.WriteLine();
    }

    private static string Line(string solver, string dt, string status, string aMin, string s, string tK, string dK) =>
        $"| {solver,-11} | {dt,-8} | {status,-8} | {aMin,12} | {s,15} | {tK,14} | {dK,9} |";

    private static string FormatDt(double dt) =>
        dt >= 1e-3 ? $"{dt * 1e3:F2} ms" : $"{dt * 1e6:F0} µs";
}