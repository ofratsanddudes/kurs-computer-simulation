using System.Windows.Media;

namespace VectorTrussCanvas.Rendering;

/// <summary>
/// Zentrale, statische und EINGEFRORENE Brushes (Freeze-Doktrin aus Kapitel 3.5).
///
/// Warum?
///   - Ein eingefrorener Brush schaltet das Change-Tracking ab und ist thread-sicher.
///   - Er wird einmal angelegt und bei jedem Redraw wiederverwendet, statt pro Frame
///     und pro Shape neue SolidColorBrush-Objekte auf dem Heap zu erzeugen (weniger GC-Last beim Dragging).
///
/// Vordefinierte Brushes wie Brushes.White oder Brushes.Orange sind bereits eingefroren.
/// </summary>
public static class RenderStyles
{
    // Stäbe
    public static readonly Brush Bar = CreateFrozen(Color.FromRgb(220, 220, 220));

    // Knoten
    public static readonly Brush NodeFill = CreateFrozen(Color.FromRgb(0, 210, 255));
    public static readonly Brush NodeSelectedFill = CreateFrozen(Color.FromRgb(255, 200, 0));
    public static readonly Brush NodeStroke = Brushes.White;
    public static readonly Brush NodeLabel = Brushes.LightGreen;

    // Stablängen-Labels
    public static readonly Brush LengthLabelText = Brushes.Yellow;
    public static readonly Brush LengthLabelBackground = CreateFrozen(Color.FromArgb(160, 20, 20, 20));

    // Auflager
    public static readonly Brush SupportStroke = Brushes.Orange;
    public static readonly Brush SupportFill = CreateFrozen(Color.FromArgb(80, 255, 165, 0));

    // Lasten und Bemaßung
    public static readonly Brush Load = Brushes.Red;
    public static readonly Brush Dimension = Brushes.LightGray;

    /// <summary>Erzeugt einen SolidColorBrush und friert ihn ein.</summary>
    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();   // ab jetzt unveränderlich
        return brush;
    }
}