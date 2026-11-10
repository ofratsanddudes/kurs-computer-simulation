# Termin 04 – Track A: Antriebsprüfstand & Schwingungs-Leitstand

**Lehrveranstaltung:** Systemsimulation / Digitaler Zwilling  
**Studiengang:** B.Sc. Automatisierungstechnik, 5. Semester, FH OÖ Campus Wels  
**Technologie:** C# 12 / .NET 8, WPF, ScottPlot.WPF 5.1.59, AutomaticGraphLayout.WpfGraphControl 1.1.12, single-threaded (`DispatcherTimer`)

Echtzeit-Leitstand für einen Motorprüfstand: Drehzahl, Drehmoment und Lagervibration werden mit 50 Hz simuliert, über Welford-Statistik mit dynamischen $\pm 3\sigma$-Bändern überwacht und in drei ScottPlot-Panels sowie einem MSAGL-Antriebsstranggraphen dargestellt.

---

## 1. Bedienung

| Element | Funktion |
| :--- | :--- |
| Pause / Reset | hält die Telemetrie an bzw. setzt Statistik, Histogramm und Zähler zurück |
| Schlupf | Kupplung rutscht: $M - 2{,}5$ Nm, $n + 80$ U/min für 15 Ticks |
| Lagerschaden | Lagerstöße: $a_{vib} \pm 0{,}9\,g$ mit wechselndem Vorzeichen für 100 Ticks |
| Überlast | Überdrehzahl: $n + 900$ U/min für 150 Ticks, damit $P > P_{max}$ |
| Badges | Alarmzustand je Kanal (Moment, Vibration, Leistung) mit Ereigniszähler |
| Statuszeile | aktuelle Werte, $\bar M \pm 3\sigma_M$, $\sigma_a$, Ereignisse, Anzahl Messwerte $k$ |

---

## 2. Aufbau

| Klasse | Aufgabe |
|---|---|
| `TestBenchModel` | Signalmodell $n(t)$, $M(t)$, $a_{vib}(t)$ und Fehlerinjektion |
| `ViolationDetector` | Alarm bei mehr als 3 aufeinanderfolgenden Verletzungen, Haltezeit 1 s |
| `LiveHistogram` | 20 feste Klassen, Zählen in $O(1)$ |
| `DrivetrainGraph` | MSAGL-Antriebsstrang, Umfärben einzelner Sensor-Knoten |
| `MainWindow` | Timer, Plots, Verknüpfung von Modell, Statistik und Anzeige |
| `WelfordStatistics` | aus dem Stufe-1-Projekt, per Projektreferenz eingebunden |

Track A ist ein eigenes Projekt in der Projektmappe. Stufe 1 bleibt unverändert im Projekt `ScottPlotTelemetry`, die Unit-Tests decken `WelfordStatistics` weiterhin ab.

---

## 3. Stufe 1 – Basisaufgabe

| Aufgabe | Umsetzung |
|---|---|
| 1.1 GC-Leak-Fix | Badge-Brushes als `private static readonly`, im statischen Konstruktor `Freeze()`, im Tick nur Referenzen zuweisen und nur bei Zustandswechsel |
| 1.2 Fenster-Statistik | vorallokiertes `_windowCopy`, `CopyTo` + `for`-Schleife für Mittelwert/Min/Max der letzten 500 Werte (10 s). Kein `foreach`, weil der `yield`-Enumerator ein Heap-Objekt wäre |
| 1.3 Entprellung | Haltezeit 25 Ticks (0,5 s), Zähler nur bei neuem Ereignis, zweiter `DataStreamer` für $y_{ref}(t) = 100 + 15\sin(2\pi \cdot 0{,}5\,t)$ |

`dotnet test`: alle 4 Unit-Tests bestanden, inkl. `Test_Welford_CatastrophicCancellation_Resistance` mit Offset $10^9$.

**Schwäche der Vorlage:** Das globale $\sigma$ enthält die Sinusschwankung selbst:

$$\sigma \approx \sqrt{\tfrac{15^2}{2} + \tfrac{6^2}{12}} \approx 10{,}75 \quad\Rightarrow\quad 3\sigma \approx 32{,}2$$

Ein Peak von +45 nahe dem Sinus-Minimum bleibt unter der oberen Grenze. Im Screenshot liegt der mittlere Peak mit ca. 142 nur knapp über der Grenze von 134,8. Eine Prüfung auf dem Residuum $r = y - y_{ref}$ würde das beheben.

---

## 4. Signalmodell

$$n(t) = 1500 + 250\sin(2\pi\cdot 0{,}05\,t) + \varepsilon_n,\quad \varepsilon_n \sim \mathcal N(0,\,5^2)\;[\text{U/min}]$$
$$M(t) = 6 + \varepsilon_M,\quad \varepsilon_M \sim \mathcal N(0,\,0{,}15^2)\;[\text{Nm}]$$
$$a_{vib}(t) = \varepsilon_a,\quad \varepsilon_a \sim \mathcal N(0,\,0{,}2^2)\;[g]$$


Das Rauschen wird per Box-Muller **normalverteilt** erzeugt. Die Vorlage nutzt gleichverteiltes Rauschen, dazu würde die Gauß-Kurve im Histogramm nicht passen.

Mechanische Leistung ($n$ in U/min, daher durch 60):

$$P = 2\pi\,\frac{n}{60}\,M \;\le\; P_{max} = 1200\,\text{W}$$

**Plausibilitätscheck:** Im Normalbetrieb erreicht der Arbeitspunkt maximal ca. 1750 U/min bei 6 Nm, also $P \approx 1100$ W unter der Grenze. Bei Überlast ($n \ge 2150$ U/min) gilt $P \ge 1350$ W, der Alarm muss also sicher auslösen.

---

## 5. Dashboard

* **A.1 Zeitbereich:** $n(t)$ links, $M(t)$ rechts über zwei `DataStreamer` mit je 500 Punkten. Das Band $\bar M \pm 3\sigma_M$ kommt aus Welford. Die zweite Y-Achse muss auch für die Bandlinien gesetzt werden:

```csharp
  _torqueStreamer.Axes.YAxis = p.Axes.Right;
  _mUpper.Axes.YAxis = p.Axes.Right;
```

  Bei Schlupf werden die Bandlinien rot und eine `Annotation` blendet einen Warnhinweis ein.

* **A.2 Betriebskennfeld:** Arbeitspunkt-`Marker` und Spur der letzten 3 s im $M$-$n$-Diagramm. Die Grenzhyperbel wird einmalig berechnet:

  $$M_{max}(n) = \frac{P_{max}}{2\pi\,n/60}$$

  Die Spur-Arrays werden wie ein Ringpuffer beschrieben, `Scatter` hält nur eine Referenz darauf. Bei Überlast wird der Marker rot.

* **A.3 Vibrations-Histogramm:** 20 Klassen in $[-1{,}2;\,1{,}2]\,g$, also $h = 0{,}12\,g$. Die Gauß-Kurve nutzt Welford-$\mu$ und -$\sigma$ und wird auf die Balkenfläche skaliert:

  $$y(x) = f_{\mathcal N}(x;\,\mu,\sigma)\cdot n \cdot h$$

  Die Dichte hat die Fläche 1, die Balken haben die Fläche $n\cdot h$. Beim Lagerschaden entstehen Seitenkeulen bei $\pm 0{,}9\,g$.

* **A.4 Anlagentopologie (MSAGL):** Kette `Umrichter → E-Motor → Kupplung → Getriebe → Lastbremse`, Sugiyama-Layout von links nach rechts. Drei Sensor-Knoten, gestrichelt angebunden:

  | Sensor | Komponente | rot bei |
  |---|---|---|
  | Drehzahlsensor | E-Motor | Überlast $P > P_{max}$ |
  | Momentenmesswelle | Kupplung | Schlupf |
  | Vibrationssensor | Getriebe | Lagerschwingung |

  Statt `AutomaticGraphLayoutControl` wird direkt der `GraphViewer` verwendet. Beim Umfärben wird nur der betroffene Knoten invalidiert, statt das gesamte Layout neu zu berechnen.

---

## 6. Alarmlogik & Welford-Statistik

**Welford-Rekursion** pro Messwert in $O(1)$, gespeichert werden nur `Count`, `Mean`, `M2`:

$$\bar x_k = \bar x_{k-1} + \frac{x_k - \bar x_{k-1}}{k}, \qquad S_k = S_{k-1} + (x_k - \bar x_{k-1})(x_k - \bar x_k), \qquad s_k^2 = \frac{S_k}{k-1}$$

Die Lehrbuchformel $\frac{1}{k-1}\left(\sum x_i^2 - \frac{1}{k}(\sum x_i)^2\right)$ subtrahiert zwei fast gleich große Zahlen. Bei einem Offset von $10^9$ löscht das Stellen aus, die Varianz kann sogar negativ werden. Welford rechnet nur mit Abweichungen vom laufenden Mittelwert (Test `Test_Welford_CatastrophicCancellation_Resistance`).

**Mindestdauer:** Ein Alarm entsteht erst bei **mehr als 3 aufeinanderfolgenden** Ticks außerhalb der Grenze. Bei $3\sigma$ liegen statistisch 0,27 % aller Werte außerhalb, einzelne Rausch-Ausreißer lösen also nichts aus.

**Ausreißer gehen nicht in die Statistik ein:**

```csharp
bool mViolated = _welfordM.IsAnomaly(m, 3.0);
if (!mViolated) _welfordM.Add(m);
```

Sonst würde jeder Fehler $\sigma$ vergrößern und das Band aufblähen, und spätere Fehler würden maskiert. Eine dauerhafte Verschiebung des Arbeitspunkts bleibt dadurch als Alarm stehen, was beim Condition Monitoring gewollt ist.

---

## 7. Didaktische Vorgreif-Sperre

Gemäß den Vorgaben für Termin 04:
* Ausschließlich **ScottPlot 5** (`Plot.Add.*`, `Plot.Axes.*`, `plottable.Axes.YAxis`), keine ScottPlot-4-Aufrufe wie `AddSignal` oder `XAxis`.
* **Kein** SharpGL, **kein** Multithreading (`Parallel.For`), **keine** S-Functions. Die Datenerzeugung läuft timergesteuert im UI-Thread über vorallokierte Puffer.

---

## 8. Performance

**Keine Allokationen im eigenen Tick-Code:** Streamer, Linien, Marker, `Bar[20]`, Histogramm-Zähler, Spur- und Gauß-Arrays werden einmalig im Konstruktor angelegt. Im Tick werden nur Werte überschrieben. `Coordinates` und das Toleranzband-Tupel sind Structs. Strings für die Statuszeile entstehen nur jeden 5. Tick, Badge-Texte und Graph-Umfärbung nur bei einem Zustandswechsel.

**Profiler-Messung** (Visual Studio, CPU-Auslastung + .NET-Indikatoren, Release, 1:51 min, ca. 3000 Ticks, alle drei Fehler injiziert):

| Messgröße | Min | Max | Durchschnitt |
|---|---|---|---|
| GC Heap Size | 0,4 MiB | 11,2 MiB | 6,7 MiB |
| Allokationsrate | 1,7 MiB | 30,9 MiB | 21 MiB |
| Gen-0-GCs (pro Intervall) | 0 | 7 | 2,7 |
| Gen-1 / Gen-2-GCs (pro Intervall) | 0 / 0 | 6 / 6 | 0,2 / 0,07 |
| Zeit im GC | 0 % | 10 % | 0,09 % |
| CPU-Auslastung | 0 % | 8,8 % | 2,6 % |
| Working Set | 77 MiB | 260 MiB | 239 MiB |

* Der GC-Heap pendelt als Sägezahn ohne Aufwärtstrend, es gibt **kein Speicherleck**.
* Gen-1/Gen-2-Sammlungen sind selten und treten überwiegend in der Startphase auf. Im Betrieb dominieren Gen-0-Sammlungen mit im Mittel 0,09 % GC-Zeit.
* Die Allokationsrate stammt überwiegend aus dem Rendering von ScottPlot/SkiaSharp (3 Plots pro Tick).
* **Effektive Tickrate:** 3000 Ticks in 111 s ergeben **≈ 27 Hz statt 50 Hz**. Ursache ist die Windows-Timer-Auflösung von ca. 15,6 ms (aus 20 ms werden ca. 31 ms) plus die Renderzeit.

![Profiler](docs/profiler.png)

---

## 9. Grafische Nachweise

### A. Stufe 1 – drei Anomalien, alle erkannt
![Stufe 1](docs/stufe1.png)

### B. Track A – Normalbetrieb
Alle Kanäle grün, $\sigma_M = 0{,}145$ Nm und $\sigma_a = 0{,}195\,g$ entsprechen dem Modell, die Gauß-Kurve deckt sich mit dem Histogramm.

![Track A – Normalbetrieb](docs/trackA_normal.png)

### C. Track A – Lagerschaden
Vibrations-Badge und Vibrationssensor rot, Seitenkeulen im Histogramm. Moment und Leistung bleiben grün, die Kanäle werden unabhängig überwacht.

![Track A – Lagerschaden](docs/trackA_lagerschaden.png)

---

## 10. Modellannahmen und Grenzen

* **Signale sind synthetisch:** Arbeitspunkt, Rauschpegel und Fehlerbilder sind eigene Annahmen, das Aufgabenblatt macht dazu keine Vorgaben.
* **Simulationszeit ≠ Echtzeit:** `_time` steigt pro Tick fest um 20 ms, der Timer erreicht aber nur ca. 27 Hz. Abhilfe wäre eine `Stopwatch` oder die Entkopplung von Simulation und Darstellung (Folie 48), ein Simulations-Thread war für Termin 04 jedoch nicht erlaubt.
* **Histogramm und Toleranzband sind global:** Mit der Zeit fallen neue Ereignisse im Histogramm weniger auf, und bei geplanten Lastwechseln müsste das Band pro Betriebspunkt geführt werden.
* **Lagerschaden und $\sigma_a$:** Einzelne Stoßwerte liegen durch das Rauschen knapp innerhalb von $\pm 3\sigma$ und gehen doch in die Statistik ein. $\sigma_a$ steigt dadurch leicht an (im Screenshot 0,267 statt 0,2), besonders bei wenigen gesammelten Werten.