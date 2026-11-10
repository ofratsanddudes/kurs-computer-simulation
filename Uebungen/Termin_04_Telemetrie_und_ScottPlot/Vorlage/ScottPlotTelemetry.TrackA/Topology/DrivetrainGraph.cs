using System.Windows.Controls;
using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.WpfGraphControl;

namespace ScottPlotTelemetry.TrackA.Topology;

public enum SensorId { Speed = 0, Torque = 1, Vibration = 2 }

/// <summary>
/// A.4: Antriebsstrang als gerichteter Graph (MSAGL, Sugiyama-Layout von links nach rechts).
/// Statt AutomaticGraphLayoutControl wird direkt der GraphViewer verwendet: so kann ein einzelner
/// Knoten umgefärbt und invalidiert werden, OHNE dass das komplette Layout neu berechnet wird.
/// </summary>
public sealed class DrivetrainGraph
{
    // MSAGL-Farben (Structs) für Normal- und Alarmzustand
    private static readonly Color OkFill = new(46, 125, 50);
    private static readonly Color AlarmFill = new(198, 40, 40);
    private static readonly Color ComponentFill = new(45, 45, 48);
    private static readonly Color LineColor = new(170, 170, 170);
    private static readonly Color TextColor = new(230, 230, 230);

    private readonly GraphViewer _viewer = new();
    private readonly Graph _graph = new("Antriebsstrang");
    private readonly Node[] _sensorNodes = new Node[3];
    private readonly IViewerNode?[] _sensorViewerNodes = new IViewerNode?[3];

    public DrivetrainGraph(Panel host)
    {
        _graph.Attr.LayerDirection = LayerDirection.LR;   // Energiefluss von links nach rechts
        _graph.Attr.BackgroundColor = new Color(30, 30, 30);

        // Hauptkette: Leistungsfluss
        string[] chain = ["Umrichter", "E-Motor", "Kupplung", "Getriebe", "Lastbremse"];
        foreach (string id in chain) AddComponent(id);
        for (int i = 0; i < chain.Length - 1; i++)
        {
            Edge e = _graph.AddEdge(chain[i], chain[i + 1]);
            e.Attr.Color = LineColor;
            e.Attr.LineWidth = 2;
        }

        // Sensor-Knoten: Messkette Komponente -> Sensor (gestrichelt)
        AddSensor(SensorId.Speed, "Drehzahlsensor", "n(t) / P", "E-Motor");
        AddSensor(SensorId.Torque, "Momentenmesswelle", "M(t)", "Kupplung");
        AddSensor(SensorId.Vibration, "Vibrationssensor", "a_vib(t)", "Getriebe");

        // Viewer an das WPF-Panel hängen und Layout berechnen (synchron im UI-Thread)
        _viewer.BindToPanel(host);
        _viewer.Graph = _graph;

        // Die Viewer-Objekte der Sensoren einmalig suchen, damit SetAlarm sie direkt invalidieren kann
        foreach (IViewerObject obj in _viewer.Entities)
        {
            if (obj is not IViewerNode vn) continue;
            for (int i = 0; i < _sensorNodes.Length; i++)
                if (vn.Node == _sensorNodes[i]) _sensorViewerNodes[i] = vn;
        }
    }

    /// <summary>Färbt einen Sensor-Knoten grün/rot. Wird nur bei Zustandswechsel aufgerufen.</summary>
    public void SetAlarm(SensorId sensor, bool alarm)
    {
        int i = (int)sensor;
        _sensorNodes[i].Attr.FillColor = alarm ? AlarmFill : OkFill;

        if (_sensorViewerNodes[i] is { } vn)
            _viewer.Invalidate(vn);       // nur diesen Knoten neu zeichnen
        else
            _viewer.Graph = _graph;       // Fallback: kompletter Neuaufbau
    }

    private void AddComponent(string id)
    {
        Node node = _graph.AddNode(id);
        node.Attr.Shape = Shape.Box;
        node.Attr.FillColor = ComponentFill;
        node.Attr.Color = LineColor;
        node.Label.FontColor = TextColor;
    }

    private void AddSensor(SensorId sensor, string id, string signal, string measuredComponent)
    {
        Node node = _graph.AddNode(id);
        node.LabelText = $"{id}\n{signal}";
        node.Attr.Shape = Shape.Ellipse;
        node.Attr.FillColor = OkFill;
        node.Attr.Color = LineColor;
        node.Label.FontColor = TextColor;
        _sensorNodes[(int)sensor] = node;

        Edge e = _graph.AddEdge(measuredComponent, id);
        e.Attr.Color = LineColor;
        e.Attr.AddStyle(Style.Dashed);
    }
}
