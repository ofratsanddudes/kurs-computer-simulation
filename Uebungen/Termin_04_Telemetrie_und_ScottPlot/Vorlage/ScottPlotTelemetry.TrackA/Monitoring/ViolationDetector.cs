namespace ScottPlotTelemetry.TrackA.Monitoring;

/// <summary>
/// Alarmlogik nach Aufgabenblatt A.1: Alarm erst, wenn ein Signal MEHR als
/// <c>minConsecutiveTicks</c> aufeinanderfolgende Ticks verletzt ist (einzelne Rausch-Ausreißer
/// lösen nichts aus). Danach bleibt der Alarm <c>holdTicks</c> lang sichtbar (Entprellung wie Stufe 1).
/// </summary>
public sealed class ViolationDetector(int minConsecutiveTicks = 3, int holdTicks = 50)
{
    private int _consecutive;   // aktuelle Serie verletzter Ticks
    private int _hold;          // verbleibende Anzeigezeit des Alarms

    public bool IsAlarm => _hold > 0;
    public long EventCount { get; private set; }

    /// <summary>Wird einmal pro Tick aufgerufen. Gibt true zurück, wenn sich der Alarmzustand geändert hat.</summary>
    public bool Update(bool violated)
    {
        bool before = IsAlarm;

        _consecutive = violated ? _consecutive + 1 : 0;

        if (_consecutive > minConsecutiveTicks)
        {
            if (_hold == 0) EventCount++;   // neues Ereignis nur zählen, wenn vorher kein Alarm aktiv war
            _hold = holdTicks;
        }
        else if (_hold > 0)
        {
            _hold--;
        }

        return before != IsAlarm;
    }

    public void Reset()
    {
        _consecutive = 0;
        _hold = 0;
        EventCount = 0;
    }
}
