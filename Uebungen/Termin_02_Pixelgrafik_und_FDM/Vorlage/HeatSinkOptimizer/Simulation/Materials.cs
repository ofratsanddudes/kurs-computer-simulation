using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeatSinkOptimizer.Simulation;

/// <summary>Materialzonen im Gitter. Air-Zellen werden nicht simuliert (siehe Schritt 5: Robin-Rand).</summary>
public enum Material : byte
{
    Air,
    Silicon,
    Tim,
    Copper,
    Aluminium
}

/// <summary>Stoffwerte eines Materials: lambda [W/(m*K)], rho [kg/m^3], c_p [J/(kg*K)].</summary>
public readonly record struct MaterialData(float Lambda, float Rho, float Cp)
{
    /// <summary>Volumetrische Wärmekapazität rho * c_p [J/(m^3*K)] (Speicherträgheit).</summary>
    public float RhoCp => Rho * Cp;

    /// <summary>Temperaturleitfähigkeit alpha = lambda / (rho * c_p) [m^2/s].</summary>
    public float Alpha => Lambda / RhoCp;
}

public static class Materials
{
    public static MaterialData Get(Material m) => m switch
    {
        // Werte aus Aufgabenblatt / Folie 30
        Material.Silicon => new MaterialData(148f, 2330f, 712f),
        Material.Copper => new MaterialData(398f, 8960f, 385f),
        Material.Aluminium => new MaterialData(237f, 2700f, 900f),

        // TIM: Das Blatt gibt nur lambda = 4,5 und alpha = 2,5e-6 vor.
        // rho * c_p = lambda / alpha = 1,8e6 J/(m^3*K), z.B. rho = 2500, c_p = 720.
        Material.Tim => new MaterialData(4.5f, 2500f, 720f),

        // Luft (nur der Vollständigkeit halber, Luftzellen werden nicht gerechnet)
        _ => new MaterialData(0.026f, 1.2f, 1005f)
    };
}