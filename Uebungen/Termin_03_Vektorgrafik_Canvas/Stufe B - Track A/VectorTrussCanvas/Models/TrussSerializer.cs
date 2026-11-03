using System.IO;
using System.Text.Json;

namespace VectorTrussCanvas.Models;

// ---------------------------------------------------------------------
// DTOs (Data Transfer Objects): schlanke Datensätze nur für die JSON-Datei.
// Sie entkoppeln das Dateiformat von den Modellklassen (TrussNode, ...).
// Positionale Records werden von System.Text.Json direkt unterstützt.
// ---------------------------------------------------------------------
public sealed record NodeDto(int Id, string? Name, double X, double Y, bool IsFixedSupport, bool IsRollerSupport);
public sealed record BarDto(int Id, int NodeAId, int NodeBId);
public sealed record LoadDto(int NodeId, double Fx, double Fy);
public sealed record TrussDto(List<NodeDto>? Nodes, List<BarDto>? Bars, List<LoadDto>? Loads);

/// <summary>
/// Speichert und lädt ein Fachwerk (Knoten, Stäbe, Lager, Lasten) als JSON.
/// Die Datei kann in Einheit 07 direkt als Eingabe für den FEM-Löser dienen.
/// Rein geometrische Datenhaltung, keine Statik.
/// </summary>
public static class TrussSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true   // menschenlesbar
    };

    /// <summary>Modell -> JSON-Text.</summary>
    public static string ToJson(TrussGeometry truss)
    {
        var dto = new TrussDto(
            truss.Nodes.Select(n => new NodeDto(n.Id, n.Name, n.X, n.Y, n.IsFixedSupport, n.IsRollerSupport)).ToList(),
            truss.Bars.Select(b => new BarDto(b.Id, b.NodeAId, b.NodeBId)).ToList(),
            truss.Loads.Select(l => new LoadDto(l.NodeId, l.Fx, l.Fy)).ToList());

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// JSON-Text -> Modell. Prüft Konsistenz (eindeutige Knoten-IDs, gültige Verweise)
    /// und wirft bei Fehlern eine InvalidDataException mit verständlicher Meldung.
    /// </summary>
    public static TrussGeometry FromJson(string json)
    {
        TrussDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<TrussDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Die Datei ist kein gültiges JSON: " + ex.Message, ex);
        }

        if (dto == null)
            throw new InvalidDataException("Die Datei enthält kein Tragwerk.");

        var truss = new TrussGeometry();
        var ids = new HashSet<int>();

        foreach (var n in dto.Nodes ?? new List<NodeDto>())
        {
            if (!ids.Add(n.Id))
                throw new InvalidDataException($"Knoten-ID {n.Id} kommt mehrfach vor.");

            truss.Nodes.Add(new TrussNode(n.Id, n.Name ?? $"K{n.Id}", n.X, n.Y, n.IsFixedSupport, n.IsRollerSupport));
        }

        foreach (var b in dto.Bars ?? new List<BarDto>())
        {
            if (!ids.Contains(b.NodeAId) || !ids.Contains(b.NodeBId))
                throw new InvalidDataException($"Stab {b.Id} verweist auf einen nicht vorhandenen Knoten.");

            truss.Bars.Add(new TrussBar(b.Id, b.NodeAId, b.NodeBId));
        }

        foreach (var l in dto.Loads ?? new List<LoadDto>())
        {
            if (!ids.Contains(l.NodeId))
                throw new InvalidDataException($"Eine Last verweist auf den nicht vorhandenen Knoten {l.NodeId}.");

            truss.Loads.Add(new TrussLoad(l.NodeId, l.Fx, l.Fy));
        }

        if (truss.Nodes.Count == 0)
            throw new InvalidDataException("Das Tragwerk enthält keine Knoten.");

        return truss;
    }

    /// <summary>Speichert das Modell als .json-Datei.</summary>
    public static void Save(TrussGeometry truss, string path)
        => File.WriteAllText(path, ToJson(truss));

    /// <summary>Lädt ein Modell aus einer .json-Datei.</summary>
    public static TrussGeometry Load(string path)
        => FromJson(File.ReadAllText(path));
}