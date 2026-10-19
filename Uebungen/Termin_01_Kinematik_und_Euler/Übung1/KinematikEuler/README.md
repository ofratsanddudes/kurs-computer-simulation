# Termin 01 – Track A: Hydraulikzylinder-Endlagendämpfung

Systemsimulation / Digitaler Zwilling, FH OÖ Campus Wels

## 1. Aufbau

| Klasse | Aufgabe |
|---|---|
| `HydraulicCylinderModel` | Physik: Kräfte und Beschleunigung a(x, v) |
| `EulerSolver`, `HeunSolver` | ein Integrationsschritt, über `IIntegrator` austauschbar |
| `CylinderSimulation` | Zeitschleife, Stabilitätsprüfung, Kontakt-Interpolation |
| `StabilityStudy` | Vergleich der Schrittweiten, Stabilitätsgrenzen |
| `ConsoleReport`, `CSVExporter` | Ausgabe, getrennt von der Rechnung |

Modell, Solver und Ausgabe kennen sich nicht gegenseitig. Es gibt keine globalen Zustände.

## 2. Modell und Annahmen

- Zustand x = [x, v], Bewegungsgleichung m·a = F_vor − F_dämpf − F_anschlag.
- Startwerte x₀ = 0, v₀ = 0 (im Blatt nicht angegeben, so angenommen).
- F_vor wirkt nur für x < 0,80 m (wörtlich nach Blatt).
- **Simulationsdauer t_end = 0,30 s** statt 0,10 s: Der Kolben erreicht den Anschlag erst nach etwa 0,112 s, bei 0,10 s gäbe es keinen Kontakt.
- Ein Lauf gilt als instabil, wenn |x| > 5 m oder |v| > 10⁵ m/s oder ein Wert NaN ist. Da der Gesamthub nur 0,80 m beträgt und die maximale Geschwindigkeit ca. 30 m/s nicht überschreitet, fangen diese Schwellen eine numerische Explosion zuverlässig ab.

## 3. Ergebnisse (t_end = 0,30 s)

| Solver | dt | Status | a_min [m/s²] | s_Anschlag [mm] | t_Kontakt [ms] |
|---|---|---|---|---|---|
| Euler | 10 µs | stabil | −166919 | 1,417 | 112,234 |
| Euler | 100 µs | stabil | −167921 | 1,526 | 111,743 |
| Euler | 500 µs | instabil | – | – | – |
| Euler | 2 ms | instabil | – | – | – |
| Heun | 10 µs | stabil | −157582 | 1,405 | 112,325 |
| Heun | 100 µs | stabil | −86191 | 1,455 | 112,501 |
| Heun | 500 µs | stabil | −1645 | 1,565 | 121,052 |
| Heun | 2 ms | instabil | – | – | – |

Referenz ist Heun mit dt = 10 µs: Kontakt bei 112,325 ms mit 1,988 m/s, maximaler Anschlagweg 1,405 mm.

**Beobachtungen**
- Heun mit 500 µs ist stabil, aber ungenau: Der Kontakt kommt 8,7 ms zu spät (stabil ≠ genau).
- a_min hängt stark vom Zeitgitter ab, weil die Reibungs- und Federkennlinien (bei x = 0,65 m bzw. x = 0,80 m) unstetig schalten. Je nachdem, wie tief ein Schritt über die Kante greift, schwankt die berechnete Spitzenkraft.

## 4. Stabilitätsgrenze

Gemessen mit einem dt-Raster in 5-µs-Schritten:

| Solver | stabil bis | immer instabil ab |
|---|---|---|
| Euler | 350 µs | 355 µs |
| Heun | 570 µs | 660 µs (dazwischen Grauzone) |

**Erklärung für Euler:** Beim Eintritt in die Dämpfungszone (x = 0,65 m) hat der Kolben v ≈ 30 m/s. Ein Euler-Schritt ergibt dort

    v_neu = v · (1 − h·v),   h = c_blend · dt / m

Der Betrag von v klingt nur ab, wenn |1 − h·v| < 1, also h·v < 2:

    dt < 2·m / (c_blend · v) = 2·80 / (15000 · 29,9) ≈ 0,36 ms

Das passt exakt zur gemessenen Grenze von 355 µs.

**Erklärung für Heun:** Die Stabilitätsfunktion lautet R(z) = 1 + z + z²/2. Da das System am Anschlag durch die Kontaktfeder und Dämpfung ein stark oszillierendes, gedämpftes System mit komplexen Eigenwerten darstellt, profitiert Heun von seinem in die komplexe Ebene ausgebuchteten Stabilitätsbereich, wodurch Schrittweiten bis 570 µs stabil abgefedert werden.

## 5. Ereignisbehandlung

Der Anschlag bei x = 0,80 m liegt fast nie genau auf einem Zeitgitterpunkt.
Wenn x in einem Schritt die Marke überschreitet, wird der Zeitpunkt linear interpoliert:

    tau = (0,80 − x_k) / (x_{k+1} − x_k),   t* = t_k + tau · dt

Damit ist die Kontaktzeit nicht an die Schrittweite gebunden.

## 6. Ausgabe

- Konsole: Tabelle aus Abschnitt 3 und Stabilitätsgrenzen.
- `simulation_cylinder.csv`: Trajektorie (Heun, dt = 10 µs) mit t, x, v, a, F_dämpf.