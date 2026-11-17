# Termin 05 – Track A: 4-Achs-SCARA mit Parallelgreifer, Teach-In & Pick-and-Place

LV Systemsimulation / Digitaler Zwilling · C# / .NET 8 · WPF · SharpGL (Fixed-Function OpenGL)

![Screenshot](docs/screenshot.png)
<!-- Screenshot der laufenden Anwendung unter docs/screenshot.png ablegen -->

## Bedienung

| Eingabe | Wirkung |
|---|---|
| Linke Maustaste ziehen | Orbit (Azimut θ, Elevation φ, geklemmt auf ±85°) |
| Rechte Maustaste ziehen | Panning des Zielpunkts |
| Mausrad | Zoom (Distanz r) |
| Slider θ1, θ2, d3, θ4, g | Gelenke direkt verstellen (bricht laufende Fahrt ab) |
| Pose A/B speichern | Aktuelle Gelenkstellung als Teach-Pose übernehmen |
| → Pose A / → Pose B / → Home | Animierte Fahrt zur Pose |
| Pick & Place Zyklus starten | Vollautomatischer Transport Station A → Station B |

## Szenengraph

```
Root
├── Base                         Zylinder, H = 0.5 m
│   └── UpperArm   T(0, H, 0)·Ry(θ1)         Schulter + Arm 1 (L1 = 1.0 m)
│       └── ForeArm   T(L1, 0, 0)·Ry(θ2)     Ellenbogen + Arm 2 (L2 = 0.8 m)
│           └── Pinole   T(L2, −d3, 0)       Pinolenschaft
│               └── Wrist   Ry(θ4)          Flansch + Querbalken (y = +0.06 m)
│                   ├── LeftFinger    T(0, 0.06, −g/2)
│                   ├── RightFinger   T(0, 0.06, +g/2)
│                   └── TCP           T(0, 0, 0)   (── Workpiece, wenn gegriffen)
├── StationA   (1.10, 0, −0.40)  Bodenmarkierung
├── StationB   (0.40, 0, 1.10)   Plattform, Höhe 0.10 m
└── Workpiece  Würfel 0.12 m     (hier, wenn abgelegt)
```

Gerendert wird ausschließlich über `_rootNode.Render(gl)`. Skalierungen der Geometrie stehen nur lokal in
`RenderCustomGeometry` (innerhalb `PushMatrix`/`PopMatrix`), damit Kindknoten nicht mitverzerrt werden.

## Kinematik

Vorwärtskinematik des TCP (Pinolen-Ursprung, die Backen sind um diesen Punkt zentriert):

$$x = L_1\cos\theta_1 + L_2\cos(\theta_1+\theta_2)$$
$$z = -\big(L_1\sin\theta_1 + L_2\sin(\theta_1+\theta_2)\big)$$
$$y = H_{Base} - d_3$$

Greiferorientierung um die Hochachse: $\psi = \theta_1 + \theta_2 + \theta_4$.
θ4 ändert nur die Orientierung, nicht die TCP-Position. Die gelbe Kontrollkugel (analytisch) und der
`TCP`-Knoten (Szenengraph-Matrix) decken sich mit Δ = 0,000 mm.

Die Standardposen A und B stammen aus der inversen Kinematik (Ellenbogen-unten-Lösung, θ2 = −99,72°).
θ4 = 37,38° ist so gewählt, dass ψ ein Vielfaches von 90° ist und die Backen parallel zu den Würfelflächen stehen.

## Teach-In & Bahnsteuerung

- `DispatcherTimer` mit ~60 Hz, Gelenkraum-Interpolation aller fünf Werte (θ1, θ2, d3, θ4, g)
- Kubisches Profil $s_c = 3s^2 - 2s^3$: Geschwindigkeit null an Start und Ziel, kein Ruck beim Anfahren
- Segmentdauer aus der größten Gelenkbewegung (90°/s, 0,25 m/s Hub und Greifer)
- Zyklus: Home → über A → absenken → greifen → anheben → über B → absenken → ablegen → abheben → Home

## Greiflogik

- Greifen, wenn der Greifer **schließt** (Flanke g > 0,14 m → g ≤ 0,14 m) und $d = \lVert p_{TCP} - p_{WP}\rVert < r_{grab} = 0{,}06$ m.
  $r_{grab}$ entspricht der halben Kantenlänge: Der TCP muss im Werkstück liegen.
- Reparenting: Das Werkstück wird von `Root` unter den `TCP`-Knoten umgehängt und folgt danach automatisch der Kette.
  Seine Weltorientierung bleibt erhalten (lokales $R_y = \psi_{WP} - \psi$).
- Beim Greifen schließen die Backen nur bis zur Kontaktbreite $g = a + t = 0{,}135$ m.
- Ablegen beim Öffnen: Zurückhängen unter `Root`. Über einer Station (Radius 0,15 m) rastet das Werkstück auf deren
  Auflagefläche ein, sonst landet es auf dem Hallenboden.

## Beleuchtung

Phong über `GL_LIGHT0` mit ambientem, diffusem und spekularem Anteil, `GL_COLOR_MATERIAL` für die Grundfarbe,
spekulares Material mit $k_s = 0{,}6$ und Shininess $\alpha = 40$ für Glanzpunkte.
`GL_NORMALIZE`, weil die Quader über ungleichmäßige Skalierung entstehen.

## Tests

`Tests/SceneGraphTests.cs`: Kugelkoordinaten, Clamping, SCARA-Vorwärtskinematik, Matrix-Hierarchie, Panning,
symmetrische Backen bei rotiertem Handgelenk, Reparenting.
