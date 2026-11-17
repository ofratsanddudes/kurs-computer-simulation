using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SceneGraph3D.Camera;
using SceneGraph3D.Scene;
using SharpGL;
using SharpGL.WPF;

namespace SceneGraph3D;

/// <summary>Gelenkkonfiguration des 4-Achs-SCARA inkl. Greiferöffnung.</summary>
public readonly record struct JointPose(float Theta1, float Theta2, float D3, float Theta4, float GWidth)
{
    /// <summary>Lineare Interpolation aller Gelenkwerte, s ∈ [0, 1].</summary>
    public static JointPose Lerp(JointPose a, JointPose b, float s) => new(
        a.Theta1 + s * (b.Theta1 - a.Theta1),
        a.Theta2 + s * (b.Theta2 - a.Theta2),
        a.D3 + s * (b.D3 - a.D3),
        a.Theta4 + s * (b.Theta4 - a.Theta4),
        a.GWidth + s * (b.GWidth - a.GWidth));

    public JointPose WithD3(float d3) => this with { D3 = d3 };
    public JointPose WithGripper(float g) => this with { GWidth = g };
}

public partial class MainWindow : Window
{
    private readonly OrbitCamera _camera = new();
    private Point _lastMousePos;

    private enum DragMode { None, Orbit, Pan }
    private DragMode _dragMode = DragMode.None;

    // ---------------------------------------------------------------------
    // SCARA Geometrie-Konstanten [m]
    // ---------------------------------------------------------------------
    private const float HBase = 0.5f;
    private const float L1 = 1.0f;
    private const float L2 = 0.8f;

    // Handgelenk & Parallelgreifer (TCP = Pinolen-Ursprung, Greifer zentriert um y = 0)
    private const float CrossbarY = 0.06f;           // Unterkante Querbalken
    private const float CrossbarThickness = 0.03f;
    private const float CrossbarLength = 0.22f;
    private const float FingerLength = 0.12f;        // Backen reichen von y = +0.06 bis -0.06
    private const float FingerThickness = 0.015f;
    private const float PinoleShaftStart = 0.15f;    // Unterkante Pinolenschaft

    private const float GWidthMin = 0.02f;
    private const float GWidthMax = 0.16f;

    // ---------------------------------------------------------------------
    // Werkstück & Greiflogik
    // ---------------------------------------------------------------------
    private const float WorkpieceSize = 0.12f;
    private const float RGrab = 0.06f;      // TCP muss innerhalb des Werkstücks liegen (halbe Kantenlänge)
    private const float GGrip = 0.14f;      // Greifer gilt ab g <= 0.14 m als geschlossen
    // Backen-Innenflächen berühren das Werkstück: g/2 - t/2 = a/2  ->  g = a + t
    private const float GContact = WorkpieceSize + FingerThickness; // 0.135 m

    // Stationen (Bodenposition x/z, Höhe der Auflagefläche)
    private static readonly Vector3 StationA = new(1.10f, 0.00f, -0.40f);
    private static readonly Vector3 StationB = new(0.40f, 0.10f, 1.10f);
    private const float StationSize = 0.25f;
    private const float StationSnapRadius = 0.15f;

    private bool _gripped = false;
    private bool _workpieceInReach = false;
    private float _prevGWidth;
    private float _lastDistance;

    // ---------------------------------------------------------------------
    // Gelenkvariablen
    // ---------------------------------------------------------------------
    private float _theta1Deg = 30.0f;
    private float _theta2Deg = -45.0f;
    private float _d3 = 0.15f;
    private float _theta4Deg = 0.0f;
    private float _gWidth = GWidthMax;

    // ---------------------------------------------------------------------
    // Teach-In
    // ---------------------------------------------------------------------
    private const float DTravel = 0.10f; // Hub für Verfahrbewegungen (TCP-Höhe 0.40 m)

    // Standardposen aus inverser Kinematik (Ellenbogen-unten-Lösung).
    // theta4 so gewählt, dass theta1 + theta2 + theta4 ein Vielfaches von 90° ist
    // -> Backen stehen parallel zu den Würfelflächen.
    private JointPose _poseA = new(62.34f, -99.72f, HBase - 0.06f, 37.38f, GWidthMax);           // d3 = 0.44
    private JointPose _poseB = new(-27.66f, -99.72f, HBase - (0.10f + 0.06f), 37.38f, GWidthMax); // d3 = 0.34
    private readonly JointPose _poseHome = new(0.0f, 0.0f, DTravel, 0.0f, GWidthMax);

    private readonly DispatcherTimer _motionTimer = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0) };
    private readonly Stopwatch _segmentClock = new();
    private readonly Queue<(JointPose Target, string Label)> _motionQueue = new();
    private JointPose _segmentStart;
    private JointPose _segmentTarget;
    private double _segmentDuration;
    private string _motionLabel = "";
    private bool _isAnimating = false;
    private bool _suppressSliderEvents = false;
    private bool _initialized = false; // Slider feuern bereits während InitializeComponent()

    // ---------------------------------------------------------------------
    // Szenengraph-Knoten
    // ---------------------------------------------------------------------
    private readonly SceneNode _rootNode = new("Root");
    private readonly SceneNode _baseNode = new("Base");
    private readonly SceneNode _arm1Node = new("UpperArm");
    private readonly SceneNode _arm2Node = new("ForeArm");
    private readonly SceneNode _pinoleNode = new("Pinole");
    private readonly SceneNode _wristNode = new("Wrist");
    private readonly SceneNode _leftFingerNode = new("LeftFinger");
    private readonly SceneNode _rightFingerNode = new("RightFinger");
    private readonly SceneNode _tcpNode = new("TCP");
    private readonly SceneNode _stationANode = new("StationA");
    private readonly SceneNode _stationBNode = new("StationB");
    private readonly SceneNode _workpieceNode = new("Workpiece");

    private JointPose CurrentPose => new(_theta1Deg, _theta2Deg, _d3, _theta4Deg, _gWidth);

    public MainWindow()
    {
        InitializeComponent();
        _prevGWidth = _gWidth;
        _motionTimer.Tick += MotionTimer_Tick;

        BuildSceneGraph();
        _initialized = true;
        SyncSlidersToJoints();
        UpdateAll();
    }

    // =====================================================================
    // Szenengraph
    // =====================================================================
    private void BuildSceneGraph()
    {
        // Root ─┬─ Base ── Arm1 ── Arm2 ── Pinole ── Wrist ─┬─ LeftFinger
        //       │                                           ├─ RightFinger
        //       │                                           └─ TCP (── Workpiece, wenn gegriffen)
        //       ├─ StationA
        //       ├─ StationB
        //       └─ Workpiece (wenn abgelegt)
        _rootNode.AddChild(_baseNode);
        _baseNode.AddChild(_arm1Node);
        _arm1Node.AddChild(_arm2Node);
        _arm2Node.AddChild(_pinoleNode);
        _pinoleNode.AddChild(_wristNode);
        _wristNode.AddChild(_leftFingerNode);
        _wristNode.AddChild(_rightFingerNode);
        _wristNode.AddChild(_tcpNode);

        _rootNode.AddChild(_stationANode);
        _rootNode.AddChild(_stationBNode);
        _rootNode.AddChild(_workpieceNode);

        // --- Roboter-Geometrie (lokal, Skalierung nur innerhalb Push/Pop) ---
        _baseNode.RenderCustomGeometry = gl =>
        {
            gl.Color(0.4f, 0.45f, 0.5f);
            DrawCylinder(gl, 0.22f, HBase, 24);
        };

        _arm1Node.RenderCustomGeometry = gl =>
        {
            gl.Color(0.2f, 0.6f, 0.9f);
            DrawSphere(gl, 0.18f);
            DrawBox(gl, L1 / 2.0f, 0.0f, 0.0f, L1, 0.14f, 0.18f);
        };

        _arm2Node.RenderCustomGeometry = gl =>
        {
            gl.Color(0.2f, 0.8f, 0.4f);
            DrawSphere(gl, 0.15f);
            DrawBox(gl, L2 / 2.0f, 0.0f, 0.0f, L2, 0.12f, 0.15f);
        };

        _pinoleNode.RenderCustomGeometry = gl =>
        {
            // Pinolenschaft (rote Platzhalter-Kugel aus Stufe 1 durch den Greifer ersetzt)
            gl.Color(0.85f, 0.85f, 0.85f);
            gl.PushMatrix();
            gl.Translate(0.0f, PinoleShaftStart, 0.0f);
            DrawCylinder(gl, 0.035f, 0.35f, 16);
            gl.PopMatrix();
        };

        // Achse 4: Flansch (Querbalken -> Pinolenschaft) + Querbalken entlang lokaler Z-Achse
        _wristNode.RenderCustomGeometry = gl =>
        {
            float flangeBottom = CrossbarY + CrossbarThickness;
            gl.Color(0.55f, 0.55f, 0.6f);
            gl.PushMatrix();
            gl.Translate(0.0f, flangeBottom, 0.0f);
            DrawCylinder(gl, 0.03f, PinoleShaftStart - flangeBottom, 16);
            gl.PopMatrix();

            gl.Color(0.3f, 0.3f, 0.35f);
            DrawBox(gl, 0.0f, CrossbarY + CrossbarThickness / 2.0f, 0.0f, 0.05f, CrossbarThickness, CrossbarLength);
        };

        // Greiferbacken (Ursprung = Unterkante Querbalken, Backe hängt nach unten bis y = -0.06)
        Action<OpenGL> drawFinger = gl =>
        {
            gl.Color(0.15f, 0.15f, 0.18f);
            DrawBox(gl, 0.0f, -FingerLength / 2.0f, 0.0f, 0.04f, FingerLength, FingerThickness);
        };
        _leftFingerNode.RenderCustomGeometry = drawFinger;
        _rightFingerNode.RenderCustomGeometry = drawFinger;

        // TCP-Knoten: unsichtbarer Referenzpunkt zwischen den Backen (= Pinolen-Ursprung)
        _tcpNode.Ty = 0.0f;

        // --- Stationen ---
        _stationANode.Tx = StationA.X; _stationANode.Tz = StationA.Z;
        _stationANode.RenderCustomGeometry = gl =>
        {
            gl.Color(0.35f, 0.35f, 0.8f); // flache Bodenmarkierung
            DrawBox(gl, 0.0f, 0.003f, 0.0f, StationSize, 0.006f, StationSize);
        };

        _stationBNode.Tx = StationB.X; _stationBNode.Tz = StationB.Z;
        _stationBNode.RenderCustomGeometry = gl =>
        {
            gl.Color(0.6f, 0.35f, 0.7f); // Ablageplattform
            DrawBox(gl, 0.0f, StationB.Y / 2.0f, 0.0f, StationSize, StationB.Y, StationSize);
        };

        // --- Werkstück ---
        PlaceWorkpieceOnStation(StationA);
        _workpieceNode.RenderCustomGeometry = gl =>
        {
            if (_gripped) gl.Color(0.25f, 0.55f, 1.0f);        // blau: gegriffen
            else if (_workpieceInReach) gl.Color(0.2f, 0.85f, 0.2f); // grün: greifbar
            else gl.Color(1.0f, 0.55f, 0.0f);                  // orange
            DrawBox(gl, 0.0f, 0.0f, 0.0f, WorkpieceSize, WorkpieceSize, WorkpieceSize);
        };

        UpdateJointTransforms();
    }

    private void UpdateJointTransforms()
    {
        _arm1Node.Ty = HBase;
        _arm1Node.Ry = _theta1Deg;

        _arm2Node.Tx = L1;
        _arm2Node.Ry = _theta2Deg;

        _pinoleNode.Tx = L2;
        _pinoleNode.Ty = -_d3;

        // Achse 4: Rotation um die vertikale Pinolenachse
        _wristNode.Ty = 0.0f;
        _wristNode.Ry = _theta4Deg;

        // Backen symmetrisch ±g/2 entlang der lokalen Z-Achse des Handgelenks.
        // Hält der Greifer ein Werkstück, können die Backen nicht weiter schließen als bis zum Kontakt.
        float gEff = _gripped ? MathF.Max(_gWidth, GContact) : _gWidth;
        _leftFingerNode.Tz = -gEff / 2.0f;
        _leftFingerNode.Ty = CrossbarY;
        _rightFingerNode.Tz = gEff / 2.0f;
        _rightFingerNode.Ty = CrossbarY;
    }

    /// <summary>Zentrale Aktualisierung nach jeder Gelenkänderung (Slider oder Animation).</summary>
    private void UpdateAll()
    {
        UpdateJointTransforms();
        UpdateGripLogic();
        UpdateTcpCalculations();
    }

    // =====================================================================
    // Greiflogik (Pick-and-Place durch Reparenting)
    // =====================================================================
    private void UpdateGripLogic()
    {
        Vector3 tcp = _tcpNode.GetWorldPosition();
        Vector3 wp = _workpieceNode.GetWorldPosition();
        _lastDistance = Vector3.Distance(tcp, wp);

        // Greifen nur beim Schließvorgang (Flanke offen -> zu), nicht wenn der
        // bereits geschlossene Greifer in das Werkstück hineinfährt.
        bool closingEdge = _prevGWidth > GGrip && _gWidth <= GGrip;
        bool isOpen = _gWidth > GGrip;

        if (!_gripped && closingEdge && _lastDistance < RGrab)
            GrabWorkpiece();
        else if (_gripped && isOpen)
            ReleaseWorkpiece();

        _prevGWidth = _gWidth;
        _workpieceInReach = !_gripped && _lastDistance < RGrab;
    }

    private float GripperYawDeg => _theta1Deg + _theta2Deg + _theta4Deg;

    private void GrabWorkpiece()
    {
        // Weltorientierung des Werkstücks relativ zum Greifer erhalten
        float localYaw = _workpieceNode.Ry - GripperYawDeg;

        _rootNode.RemoveChild(_workpieceNode);
        _workpieceNode.Tx = 0.0f;
        _workpieceNode.Ty = 0.0f;
        _workpieceNode.Tz = 0.0f;
        _workpieceNode.Ry = NormalizeDeg(localYaw);
        _tcpNode.AddChild(_workpieceNode);

        _gripped = true;
        UpdateJointTransforms(); // Backen auf Kontaktbreite begrenzen
    }

    private void ReleaseWorkpiece()
    {
        Vector3 world = _workpieceNode.GetWorldPosition();
        float worldYaw = GripperYawDeg + _workpieceNode.Ry;

        _tcpNode.RemoveChild(_workpieceNode);
        _rootNode.AddChild(_workpieceNode);
        _workpieceNode.Ry = NormalizeDeg(worldYaw);
        _gripped = false;

        // Ablage: auf einer Station (Snap) oder auf dem Hallenboden
        foreach (var station in new[] { StationA, StationB })
        {
            float dxz = MathF.Sqrt(MathF.Pow(world.X - station.X, 2) + MathF.Pow(world.Z - station.Z, 2));
            if (dxz < StationSnapRadius)
            {
                PlaceWorkpieceOnStation(station);
                return;
            }
        }

        _workpieceNode.Tx = world.X;
        _workpieceNode.Ty = WorkpieceSize / 2.0f;
        _workpieceNode.Tz = world.Z;
    }

    private void PlaceWorkpieceOnStation(Vector3 station)
    {
        _workpieceNode.Tx = station.X;
        _workpieceNode.Ty = station.Y + WorkpieceSize / 2.0f;
        _workpieceNode.Tz = station.Z;
    }

    private static float NormalizeDeg(float a)
    {
        a %= 360.0f;
        if (a > 180.0f) a -= 360.0f;
        if (a < -180.0f) a += 360.0f;
        return a;
    }

    // =====================================================================
    // Teach-In & animierte Bahnsteuerung (DispatcherTimer, ~60 Hz)
    // =====================================================================
    private void StartMotion(IEnumerable<(JointPose Target, string Label)> waypoints)
    {
        StopMotion();
        foreach (var wp in waypoints) _motionQueue.Enqueue(wp);
        if (NextSegment())
        {
            _isAnimating = true;
            _motionTimer.Start();
        }
    }

    private void StopMotion()
    {
        _motionTimer.Stop();
        _motionQueue.Clear();
        _isAnimating = false;
        _motionLabel = "";
    }

    private bool NextSegment()
    {
        if (_motionQueue.Count == 0) return false;

        var (target, label) = _motionQueue.Dequeue();
        _segmentStart = CurrentPose;
        _segmentTarget = target;
        _motionLabel = label;

        // Dauer aus der größten Gelenkbewegung: 90°/s, 0.25 m/s Hub, 0.25 m/s Greifer
        double t = Math.Max(
            Math.Max(Math.Abs(target.Theta1 - _segmentStart.Theta1),
            Math.Max(Math.Abs(target.Theta2 - _segmentStart.Theta2),
                     Math.Abs(target.Theta4 - _segmentStart.Theta4))) / 90.0,
            Math.Max(Math.Abs(target.D3 - _segmentStart.D3) / 0.25,
                     Math.Abs(target.GWidth - _segmentStart.GWidth) / 0.25));
        _segmentDuration = Math.Max(0.3, t);

        _segmentClock.Restart();
        return true;
    }

    private void MotionTimer_Tick(object? sender, EventArgs e)
    {
        double s = Math.Min(1.0, _segmentClock.Elapsed.TotalSeconds / _segmentDuration);

        // Kubische Interpolation (smoothstep): Geschwindigkeit 0 an Start und Ziel
        float sCubic = (float)(s * s * (3.0 - 2.0 * s));
        ApplyPose(JointPose.Lerp(_segmentStart, _segmentTarget, sCubic));

        if (s >= 1.0 && !NextSegment())
        {
            StopMotion();
            UpdateTcpCalculations();
        }
    }

    private void ApplyPose(JointPose p)
    {
        _theta1Deg = p.Theta1;
        _theta2Deg = p.Theta2;
        _d3 = p.D3;
        _theta4Deg = p.Theta4;
        _gWidth = p.GWidth;

        SyncSlidersToJoints();
        UpdateAll();
    }

    private IEnumerable<(JointPose, string)> BuildPickAndPlaceCycle()
    {
        float open = GWidthMax;
        float closed = GContact;

        yield return (_poseHome, "Home");
        yield return (_poseA.WithD3(DTravel).WithGripper(open), "Über A");
        yield return (_poseA.WithGripper(open), "Absenken A");
        yield return (_poseA.WithGripper(closed), "Greifen");
        yield return (_poseA.WithD3(DTravel).WithGripper(closed), "Anheben");
        yield return (_poseB.WithD3(DTravel).WithGripper(closed), "Über B");
        yield return (_poseB.WithGripper(closed), "Absenken B");
        yield return (_poseB.WithGripper(open), "Ablegen");
        yield return (_poseB.WithD3(DTravel).WithGripper(open), "Abheben");
        yield return (_poseHome, "Home");
    }

    // Button-Handler Teach-In
    private void BtnSavePoseA_Click(object sender, RoutedEventArgs e) { _poseA = CurrentPose; UpdateTcpCalculations(); }
    private void BtnSavePoseB_Click(object sender, RoutedEventArgs e) { _poseB = CurrentPose; UpdateTcpCalculations(); }
    private void BtnGoPoseA_Click(object sender, RoutedEventArgs e) => StartMotion([(_poseA, "Pose A")]);
    private void BtnGoPoseB_Click(object sender, RoutedEventArgs e) => StartMotion([(_poseB, "Pose B")]);
    private void BtnGoHome_Click(object sender, RoutedEventArgs e) => StartMotion([(_poseHome, "Home")]);
    private void BtnStartCycle_Click(object sender, RoutedEventArgs e) => StartMotion(BuildPickAndPlaceCycle());
    private void BtnStopMotion_Click(object sender, RoutedEventArgs e) { StopMotion(); UpdateTcpCalculations(); }

    private void BtnResetWorkpiece_Click(object sender, RoutedEventArgs e)
    {
        StopMotion();
        if (_gripped)
        {
            _tcpNode.RemoveChild(_workpieceNode);
            _rootNode.AddChild(_workpieceNode);
            _gripped = false;
        }
        _workpieceNode.Ry = 0.0f;
        PlaceWorkpieceOnStation(StationA);
        UpdateAll();
    }

    // =====================================================================
    // Rendering
    // =====================================================================
    private void OpenGlView_OpenGLInitialized(object sender, OpenGLRoutedEventArgs args)
    {
        OpenGL gl = args.OpenGL;

        gl.Enable(OpenGL.GL_DEPTH_TEST);

        // Phong-Beleuchtungsmodell
        gl.Enable(OpenGL.GL_LIGHTING);
        gl.Enable(OpenGL.GL_LIGHT0);

        float[] lightPos = [10.0f, 20.0f, 15.0f, 1.0f];
        float[] lightAmbient = [0.25f, 0.25f, 0.25f, 1.0f];
        float[] lightDiffuse = [0.85f, 0.85f, 0.85f, 1.0f];
        float[] lightSpecular = [1.0f, 1.0f, 1.0f, 1.0f];

        gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_POSITION, lightPos);
        gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_AMBIENT, lightAmbient);
        gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_DIFFUSE, lightDiffuse);
        gl.Light(OpenGL.GL_LIGHT0, OpenGL.GL_SPECULAR, lightSpecular);

        gl.Enable(OpenGL.GL_COLOR_MATERIAL);
        gl.ColorMaterial(OpenGL.GL_FRONT_AND_BACK, OpenGL.GL_AMBIENT_AND_DIFFUSE);

        // Spekulare Materialanteile -> sichtbare Glanzpunkte (Phong: k_s, alpha).
        // Ohne diese Zeilen ist k_s = 0 (OpenGL-Default) und es gibt keine Glanzpunkte.
        gl.Material(OpenGL.GL_FRONT_AND_BACK, OpenGL.GL_SPECULAR, new[] { 0.6f, 0.6f, 0.6f, 1.0f });
        gl.Material(OpenGL.GL_FRONT_AND_BACK, OpenGL.GL_SHININESS, new[] { 40.0f });

        // Normalen nach (ungleichmäßiger) Skalierung wieder auf Länge 1 bringen
        gl.Enable(OpenGL.GL_NORMALIZE);

        gl.ShadeModel(OpenGL.GL_SMOOTH);
        gl.ClearColor(0.08f, 0.08f, 0.08f, 1.0f);
    }

    private void OpenGlView_Resized(object sender, OpenGLRoutedEventArgs args)
    {
        OpenGL gl = args.OpenGL;
        gl.MatrixMode(OpenGL.GL_PROJECTION);
        gl.LoadIdentity();

        double aspect = OpenGlView.ActualWidth / Math.Max(OpenGlView.ActualHeight, 1.0);
        gl.Perspective(45.0, aspect, 0.1, 100.0);

        gl.MatrixMode(OpenGL.GL_MODELVIEW);
    }

    private void OpenGlView_OpenGLDraw(object sender, OpenGLRoutedEventArgs args)
    {
        OpenGL gl = args.OpenGL;

        gl.Clear(OpenGL.GL_COLOR_BUFFER_BIT | OpenGL.GL_DEPTH_BUFFER_BIT);
        gl.LoadIdentity();

        _camera.Apply(gl);
        DrawAxes(gl, length: 2.0f);
        DrawFloorGrid(gl, size: 4.0f, step: 0.5f);

        // Gesamte Szene (Roboter, Greifer, Stationen, Werkstück) über einen einzigen Aufruf
        _rootNode.Render(gl);

        DrawTcpSphere(gl);
    }

    private void DrawTcpSphere(OpenGL gl)
    {
        var (xAna, yAna, zAna) = CalculateAnalyticTcp();

        gl.Disable(OpenGL.GL_LIGHTING);
        gl.Color(1.0f, 1.0f, 0.0f);

        gl.PushMatrix();
        gl.Translate(xAna, yAna, zAna);
        DrawSphere(gl, 0.02f);
        gl.PopMatrix();

        gl.Enable(OpenGL.GL_LIGHTING);
    }

    // =====================================================================
    // Vorwärtskinematik
    // =====================================================================
    private (float X, float Y, float Z) CalculateAnalyticTcp()
    {
        // x = L1 cos θ1 + L2 cos(θ1+θ2)
        // z = -(L1 sin θ1 + L2 sin(θ1+θ2))
        // y = H_Base - d3   (TCP = Pinolen-Ursprung, Greifer ist darum zentriert)
        // θ4 dreht um die vertikale Achse durch den TCP -> ändert nur die Orientierung
        float t1 = _theta1Deg * MathF.PI / 180.0f;
        float t12 = (_theta1Deg + _theta2Deg) * MathF.PI / 180.0f;

        float x = L1 * MathF.Cos(t1) + L2 * MathF.Cos(t12);
        float z = -(L1 * MathF.Sin(t1) + L2 * MathF.Sin(t12));
        float y = HBase - _d3;

        return (x, y, z);
    }

    private void UpdateTcpCalculations()
    {
        var (xAna, yAna, zAna) = CalculateAnalyticTcp();
        var pMatrix = _tcpNode.GetWorldPosition();

        if (TxtTcpAnalytic != null)
            TxtTcpAnalytic.Text = $"X = {xAna:F3} m | Y = {yAna:F3} m | Z = {zAna:F3} m";

        if (TxtTcpMatrix != null)
            TxtTcpMatrix.Text = $"X = {pMatrix.X:F3} m | Y = {pMatrix.Y:F3} m | Z = {pMatrix.Z:F3} m";

        float diff = Vector3.Distance(new Vector3(xAna, yAna, zAna), pMatrix) * 1000.0f;
        if (TxtTcpDiff != null)
            TxtTcpDiff.Text = $"Deckungsfehler Delta = {diff:F3} mm";

        if (TxtStatus != null)
        {
            string state = _gripped ? "gegriffen" : _workpieceInReach ? "greifbar" : "frei";
            string motion = _isAnimating ? $"  |  Fahre: {_motionLabel}" : "";
            TxtStatus.Text = $"d = {_lastDistance:F3} m  |  Werkstück: {state}  |  Greiferyaw = {NormalizeDeg(GripperYawDeg):F1}°{motion}";
        }
    }

    // =====================================================================
    // Geometrie-Primitive & Normalen
    // =====================================================================
    /// <summary>Quader mit Mittelpunkt (cx, cy, cz) und Abmessungen (sx, sy, sz), lokal in Push/Pop.</summary>
    private static void DrawBox(OpenGL gl, float cx, float cy, float cz, float sx, float sy, float sz)
    {
        gl.PushMatrix();
        gl.Translate(cx, cy, cz);
        gl.Scale(sx, sy, sz);
        DrawCube(gl);
        gl.PopMatrix();
    }

    private static void DrawAxes(OpenGL gl, float length)
    {
        gl.Disable(OpenGL.GL_LIGHTING);
        gl.LineWidth(2.5f);
        gl.Begin(OpenGL.GL_LINES);

        gl.Color(1.0f, 0.1f, 0.1f);
        gl.Vertex(0.0f, 0.0f, 0.0f);
        gl.Vertex(length, 0.0f, 0.0f);

        gl.Color(0.1f, 1.0f, 0.1f);
        gl.Vertex(0.0f, 0.0f, 0.0f);
        gl.Vertex(0.0f, length, 0.0f);

        gl.Color(0.1f, 0.4f, 1.0f);
        gl.Vertex(0.0f, 0.0f, 0.0f);
        gl.Vertex(0.0f, 0.0f, length);

        gl.End();
        gl.Enable(OpenGL.GL_LIGHTING);
    }

    private static void DrawFloorGrid(OpenGL gl, float size, float step)
    {
        gl.Disable(OpenGL.GL_LIGHTING);
        gl.LineWidth(1.0f);
        gl.Color(0.2f, 0.2f, 0.2f);
        gl.Begin(OpenGL.GL_LINES);

        for (float v = -size; v <= size; v += step)
        {
            gl.Vertex(-size, 0.0f, v);
            gl.Vertex(size, 0.0f, v);

            gl.Vertex(v, 0.0f, -size);
            gl.Vertex(v, 0.0f, size);
        }

        gl.End();
        gl.Enable(OpenGL.GL_LIGHTING);
    }

    private static void DrawCube(OpenGL gl)
    {
        float s = 0.5f;
        gl.Begin(OpenGL.GL_QUADS);

        gl.Normal(0.0f, 0.0f, 1.0f);
        gl.Vertex(-s, -s, s); gl.Vertex(s, -s, s); gl.Vertex(s, s, s); gl.Vertex(-s, s, s);

        gl.Normal(0.0f, 0.0f, -1.0f);
        gl.Vertex(-s, -s, -s); gl.Vertex(-s, s, -s); gl.Vertex(s, s, -s); gl.Vertex(s, -s, -s);

        gl.Normal(0.0f, 1.0f, 0.0f);
        gl.Vertex(-s, s, -s); gl.Vertex(-s, s, s); gl.Vertex(s, s, s); gl.Vertex(s, s, -s);

        gl.Normal(0.0f, -1.0f, 0.0f);
        gl.Vertex(-s, -s, -s); gl.Vertex(s, -s, -s); gl.Vertex(s, -s, s); gl.Vertex(-s, -s, s);

        gl.Normal(1.0f, 0.0f, 0.0f);
        gl.Vertex(s, -s, -s); gl.Vertex(s, s, -s); gl.Vertex(s, s, s); gl.Vertex(s, -s, s);

        gl.Normal(-1.0f, 0.0f, 0.0f);
        gl.Vertex(-s, -s, -s); gl.Vertex(-s, -s, s); gl.Vertex(-s, s, s); gl.Vertex(-s, s, -s);

        gl.End();
    }

    private static void DrawSphere(OpenGL gl, float radius, int slices = 16, int stacks = 16)
    {
        for (int i = 0; i < stacks; i++)
        {
            float phi1 = MathF.PI * (-0.5f + (float)i / stacks);
            float phi2 = MathF.PI * (-0.5f + (float)(i + 1) / stacks);

            gl.Begin(OpenGL.GL_QUAD_STRIP);
            for (int j = 0; j <= slices; j++)
            {
                float theta = 2.0f * MathF.PI * (float)j / slices;

                float x1 = MathF.Cos(phi1) * MathF.Cos(theta);
                float y1 = MathF.Sin(phi1);
                float z1 = MathF.Cos(phi1) * MathF.Sin(theta);

                float x2 = MathF.Cos(phi2) * MathF.Cos(theta);
                float y2 = MathF.Sin(phi2);
                float z2 = MathF.Cos(phi2) * MathF.Sin(theta);

                gl.Normal(x1, y1, z1);
                gl.Vertex(radius * x1, radius * y1, radius * z1);

                gl.Normal(x2, y2, z2);
                gl.Vertex(radius * x2, radius * y2, radius * z2);
            }
            gl.End();
        }
    }

    private static void DrawCylinder(OpenGL gl, float radius, float height, int slices = 16)
    {
        gl.Begin(OpenGL.GL_QUAD_STRIP);
        for (int i = 0; i <= slices; i++)
        {
            float theta = 2.0f * MathF.PI * i / slices;
            float nx = MathF.Cos(theta);
            float nz = MathF.Sin(theta);

            gl.Normal(nx, 0.0f, nz);
            gl.Vertex(radius * nx, 0.0f, radius * nz);
            gl.Vertex(radius * nx, height, radius * nz);
        }
        gl.End();
    }

    // =====================================================================
    // Kamera-Maus-Events (Links: Orbit, Rechts: Panning, Rad: Zoom)
    // =====================================================================
    private void OpenGlView_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _dragMode = DragMode.Orbit;
        else if (e.ChangedButton == MouseButton.Right) _dragMode = DragMode.Pan;
        else return;

        _lastMousePos = e.GetPosition(OpenGlView);
        OpenGlView.CaptureMouse();
    }

    private void OpenGlView_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == DragMode.None) return;

        Point currentPos = e.GetPosition(OpenGlView);
        double dx = currentPos.X - _lastMousePos.X;
        double dy = currentPos.Y - _lastMousePos.Y;

        if (_dragMode == DragMode.Orbit)
            _camera.Rotate(-dx * 0.4, dy * 0.4);
        else
            _camera.Pan(dx * 0.01, dy * 0.01);

        _lastMousePos = currentPos;
    }

    private void OpenGlView_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.None) return;

        _dragMode = DragMode.None;
        OpenGlView.ReleaseMouseCapture();
    }

    private void OpenGlView_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double deltaZoom = e.Delta > 0 ? -0.5 : 0.5;
        _camera.Zoom(deltaZoom);
    }

    // =====================================================================
    // Slider
    // =====================================================================
    private void SliderJoint_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || _suppressSliderEvents) return;

        // Manueller Eingriff bricht eine laufende Bahnfahrt ab
        if (_isAnimating) StopMotion();

        _theta1Deg = (float)SliderTheta1.Value;
        _theta2Deg = (float)SliderTheta2.Value;
        _d3 = (float)SliderD3.Value;
        _theta4Deg = (float)SliderTheta4.Value;
        _gWidth = (float)SliderGWidth.Value;

        UpdateJointLabels();
        UpdateAll();
    }

    private void SyncSlidersToJoints()
    {
        if (!_initialized) return;

        _suppressSliderEvents = true;
        SliderTheta1.Value = _theta1Deg;
        SliderTheta2.Value = _theta2Deg;
        SliderD3.Value = _d3;
        SliderTheta4.Value = _theta4Deg;
        SliderGWidth.Value = _gWidth;
        _suppressSliderEvents = false;

        UpdateJointLabels();
    }

    private void UpdateJointLabels()
    {
        TxtTheta1.Text = $"{_theta1Deg:F0}°";
        TxtTheta2.Text = $"{_theta2Deg:F0}°";
        TxtD3.Text = $"{_d3:F2} m";
        TxtTheta4.Text = $"{_theta4Deg:F0}°";
        TxtGWidth.Text = $"{_gWidth * 1000.0f:F0} mm";
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        _camera.AzimuthDeg = 45.0;
        _camera.ElevationDeg = 30.0;
        _camera.Distance = 8.0;
        _camera.LookAtX = 0.0;
        _camera.LookAtY = 0.5;
        _camera.LookAtZ = 0.0;
    }

    private void BtnResetJoints_Click(object sender, RoutedEventArgs e)
    {
        StopMotion();
        ApplyPose(new JointPose(0.0f, 0.0f, 0.0f, 0.0f, GWidthMax));
    }
}
