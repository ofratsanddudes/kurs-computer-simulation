using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using VectorTrussCanvas.Geometry;
using VectorTrussCanvas.Models;
using VectorTrussCanvas.Rendering;

namespace VectorTrussCanvas;

/// <summary>
/// Hauptfenster: nimmt Benutzereingaben entgegen (Maus, Slider, Buttons)
/// und delegiert das Zeichnen an den TrussRenderer.
/// </summary>
public partial class MainWindow : Window
{
    private TrussGeometry _truss;
    private readonly Transform2D _transform = new();

    private TrussNode? _draggedNode = null;
    private TrussNode? _selectedNode = null;

    private bool _isPanning = false;
    private Point _lastMousePos;

    private bool _suppressLoadEvents = false;

    private const double ZoomStep = 1.15;
    private const double HitRadiusPx = 20.0;

    public MainWindow()
    {
        InitializeComponent();
        _truss = TrussGeometry.CreateSprintTriangle();
        UpdateLoadEditor();
    }

    // =====================================================================
    // ZEICHNEN
    // =====================================================================

    private void Redraw()
    {
        if (TrussCanvas == null || TrussCanvas.ActualWidth < 10 || TrussCanvas.ActualHeight < 10)
            return;

        if (_draggedNode == null)
        {
            var (xMin, xMax, yMin, yMax) = _truss.GetBounds();
            _transform.MarginX = 90.0;
            _transform.MarginY = 90.0;
            _transform.Update(xMin, xMax, yMin, yMax, TrussCanvas.ActualWidth, TrussCanvas.ActualHeight);
        }

        TrussRenderer.Render(TrussCanvas, _truss, _transform, _selectedNode);
    }

    // =====================================================================
    // MODELLWAHL (Sprint-Dreieck, Pratt-Träger oder Hallen-Auslegerkran)
    // =====================================================================

    private TrussGeometry CreateSelectedModel() => CmbModelSelection.SelectedIndex switch
    {
        1 => TrussGeometry.CreateIndustrialPrattTruss(),
        2 => TrussGeometry.CreateCantileverCraneTruss(),
        _ => TrussGeometry.CreateSprintTriangle()
    };

    private void ApplyModel(TrussGeometry model)
    {
        _truss = model;
        _selectedNode = null;
        _draggedNode = null;
        _transform.ResetCamera();
        UpdateLoadEditor();
        Redraw();
    }

    private void CmbModelSelection_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        ApplyModel(CreateSelectedModel());
        TxtInfo.Text = "Modell gewechselt.";
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        ApplyModel(CreateSelectedModel());
        TxtInfo.Text = "Modell zurückgesetzt.";
    }

    // =====================================================================
    // MAUS: AUSWAHL, DRAG, PAN, ZOOM
    // =====================================================================

    private void TrussCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        Redraw();
    }

    private void TrussCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Point mousePos = e.GetPosition(TrussCanvas);

        // Rechte oder mittlere Taste: Kamera verschieben (Pan)
        if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _lastMousePos = mousePos;
            TrussCanvas.CaptureMouse();
            TrussCanvas.Cursor = Cursors.SizeAll;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        // Nächstgelegenen Knoten im Greifradius finden
        TrussNode? hit = _truss.Nodes
            .Select(n => new { Node = n, Dist = (mousePos - _transform.WorldToScreen(n.X, n.Y)).Length })
            .Where(x => x.Dist <= HitRadiusPx)
            .OrderBy(x => x.Dist)
            .Select(x => x.Node)
            .FirstOrDefault();

        _selectedNode = hit;
        UpdateLoadEditor();

        _draggedNode = hit;
        if (_draggedNode != null)
        {
            TrussCanvas.CaptureMouse();
            TxtInfo.Text = $"Ziehe Knoten {_draggedNode.Name}...";
        }

        Redraw();
    }

    private void TrussCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point mousePos = e.GetPosition(TrussCanvas);

        // Kamera verschieben
        if (_isPanning)
        {
            Vector delta = mousePos - _lastMousePos;
            _lastMousePos = mousePos;
            _transform.PanBy(delta.X, delta.Y);
            Redraw();
            return;
        }

        // Knoten ziehen
        if (_draggedNode == null) return;

        var (worldX, worldY) = _transform.ScreenToWorld(mousePos.X, mousePos.Y);

        _draggedNode.X = Math.Round(worldX, 3);
        _draggedNode.Y = Math.Round(worldY, 3);

        TxtInfo.Text = $"Knoten {_draggedNode.Name}: X = {_draggedNode.X:F3} m | Y = {_draggedNode.Y:F3} m";
        UpdateSelectedNodeText();
        Redraw();
    }

    private void TrussCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        // Panning beenden
        if (_isPanning && (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle))
        {
            _isPanning = false;
            TrussCanvas.ReleaseMouseCapture();
            TrussCanvas.Cursor = Cursors.Arrow;
            TxtInfo.Text = $"Ansicht verschoben. Zoom: {_transform.Zoom * 100:F0} %";
            return;
        }

        // Dragging beenden
        if (_draggedNode != null && e.ChangedButton == MouseButton.Left)
        {
            _draggedNode = null;
            TrussCanvas.ReleaseMouseCapture();
            TxtInfo.Text = "Knoten platziert. Auto-Fit aktiv.";
            Redraw();
        }
    }

    private void TrussCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        Point mousePos = e.GetPosition(TrussCanvas);
        double factor = e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;

        _transform.ZoomAt(mousePos.X, mousePos.Y, factor);

        TxtInfo.Text = $"Zoom: {_transform.Zoom * 100:F0} %";
        Redraw();
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        _transform.ResetCamera();
        TxtInfo.Text = "Ansicht zurückgesetzt.";
        Redraw();
    }

    // =====================================================================
    // LASTVEKTOR-EDITOR
    // =====================================================================

    private TrussLoad? FindLoad(TrussNode node) => _truss.Loads.FirstOrDefault(l => l.NodeId == node.Id);

    private void UpdateLoadEditor()
    {
        _suppressLoadEvents = true;

        if (_selectedNode == null)
        {
            TxtSelectedNode.Text = "Kein Knoten ausgewählt";
            SldFx.Value = 0;
            SldFy.Value = 0;
            SldFx.IsEnabled = false;
            SldFy.IsEnabled = false;
            BtnRemoveLoad.IsEnabled = false;
        }
        else
        {
            var load = FindLoad(_selectedNode);
            SldFx.Value = load?.Fx ?? 0.0;
            SldFy.Value = load?.Fy ?? 0.0;
            SldFx.IsEnabled = true;
            SldFy.IsEnabled = true;
            BtnRemoveLoad.IsEnabled = load != null;
            UpdateSelectedNodeText();
        }

        _suppressLoadEvents = false;
        UpdateLoadInfoText();
    }

    private void UpdateSelectedNodeText()
    {
        if (_selectedNode == null) return;
        TxtSelectedNode.Text = $"Knoten {_selectedNode.Name}  ({_selectedNode.X:F3} ; {_selectedNode.Y:F3}) m";
    }

    private void UpdateLoadInfoText()
    {
        double fx = _selectedNode != null ? SldFx.Value : 0.0;
        double fy = _selectedNode != null ? SldFy.Value : 0.0;

        TxtFx.Text = $"Fx = {fx:F1} kN";
        TxtFy.Text = $"Fy = {fy:F1} kN";

        double magnitude = Math.Sqrt(fx * fx + fy * fy);
        if (_selectedNode == null || magnitude < 1e-9)
        {
            TxtLoadInfo.Text = _selectedNode == null ? "" : "Keine Last an diesem Knoten.";
            return;
        }

        double phiDeg = Math.Atan2(fy, fx) * 180.0 / Math.PI;
        if (phiDeg < 0) phiDeg += 360.0;

        TxtLoadInfo.Text = $"|F| = {magnitude:F1} kN   φ = {phiDeg:F1}°";
    }

    private void LoadSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _suppressLoadEvents || _selectedNode == null) return;

        double fx = Math.Round(SldFx.Value, 1);
        double fy = Math.Round(SldFy.Value, 1);

        var load = FindLoad(_selectedNode);
        bool isZero = Math.Abs(fx) < 1e-9 && Math.Abs(fy) < 1e-9;

        if (isZero)
        {
            if (load != null) _truss.Loads.Remove(load);
        }
        else if (load == null)
        {
            _truss.Loads.Add(new TrussLoad(_selectedNode.Id, fx, fy));
        }
        else
        {
            load.Fx = fx;
            load.Fy = fy;
        }

        BtnRemoveLoad.IsEnabled = !isZero;
        UpdateLoadInfoText();
        Redraw();
    }

    private void BtnRemoveLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedNode == null) return;

        var load = FindLoad(_selectedNode);
        if (load != null) _truss.Loads.Remove(load);

        UpdateLoadEditor();
        Redraw();
    }

    // =====================================================================
    // JSON SPEICHERN / LADEN
    // =====================================================================

    private void BtnSaveJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON-Dateien (*.json)|*.json",
            FileName = "fachwerk.json",
            Title = "Tragwerk als JSON speichern"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            TrussSerializer.Save(_truss, dialog.FileName);
            TxtInfo.Text = $"Gespeichert: {Path.GetFileName(dialog.FileName)} " +
                           $"({_truss.Nodes.Count} Knoten, {_truss.Bars.Count} Stäbe, {_truss.Loads.Count} Lasten)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Speichern fehlgeschlagen:\n" + ex.Message, "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnLoadJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON-Dateien (*.json)|*.json",
            Title = "Tragwerk aus JSON laden"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            TrussGeometry loaded = TrussSerializer.Load(dialog.FileName);
            ApplyModel(loaded);
            TxtInfo.Text = $"Geladen: {Path.GetFileName(dialog.FileName)} " +
                           $"({loaded.Nodes.Count} Knoten, {loaded.Bars.Count} Stäbe, {loaded.Loads.Count} Lasten)";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Laden fehlgeschlagen:\n" + ex.Message, "Fehler",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}