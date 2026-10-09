using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace CTTools.Features.Topo;

public enum TopoTool
{
    Shape,
    Smooth,
    Inflate,
    Mesh,
    Point,
    Mid,
    Slope,
    Split,
    Pad,
}

public sealed class TopoSettings
{
    public required TopoTool Tool { get; init; }
    public ShapeAction Shape { get; init; }
    public double OuterRing { get; init; }
    public int Density { get; init; }
    public double Gain { get; init; }
    public int SmoothPasses { get; init; }
    public double InflatePercent { get; init; }
    public double Cell { get; init; }
    public double Rotation { get; init; }
    public bool RelativePoint { get; init; }
    public double Elevation { get; init; }
    public bool SlopeByAngle { get; init; }
    public double SlopeDegrees { get; init; }
    public double SlopeEnd { get; init; }
    public ElementId LevelId { get; init; } = ElementId.InvalidElementId;
    public ElementId TypeId { get; init; } = ElementId.InvalidElementId;

    public bool Live => Tool is TopoTool.Shape or TopoTool.Mesh or TopoTool.Point or TopoTool.Mid or TopoTool.Slope or TopoTool.Split or TopoTool.Pad;
    public bool ShowRings => Tool is TopoTool.Shape or TopoTool.Mesh;
    public bool ShowGrid => Tool is TopoTool.Smooth or TopoTool.Inflate or TopoTool.Mesh;
}

public sealed class TopoSession
{
    private readonly UIApplication _uiApp;
    private readonly TopoWindow _window;
    private readonly ExternalEvent _event;
    private readonly TopoEventHandler _handler;
    private readonly TopoPreviewServer _preview = new();
    private readonly EventHandler<IdlingEventArgs> _idle;
    private readonly Stack<List<(double X, double Y, double Z)>> _undo = new();

    private ElementId? _surfaceId;
    private List<(double X, double Y, double Z)> _baseline = new();
    private List<(double X, double Y, double Z)>? _previewSnap;
    private TopoProbe? _probe;
    private XYZ? _anchor;
    private TopoTool _tool = TopoTool.Shape;
    private bool _down;
    private bool _suspended;
    private bool _busy;
    private bool _started;
    private long _nextIdle;
    private (int X, int Y) _lastCursor;
    private bool _haveCursor;
    private string _previewKey = "";

    public TopoSession(UIApplication uiApp, TopoWindow window, ExternalEvent externalEvent, TopoEventHandler handler)
    {
        _uiApp = uiApp;
        _window = window;
        _event = externalEvent;
        _handler = handler;
        _idle = OnIdle;
    }

    public void Start()
    {
        if (_started)
            return;
        _uiApp.Idling += _idle;
        _started = true;
        var doc = _uiApp.ActiveUIDocument?.Document;
        if (doc is not null)
        {
            try
            {
                _preview.Register(doc);
            }
            catch
            {
                // The brush still edits if the overlay cannot be registered.
            }
        }
    }

    public void Stop()
    {
        if (_started)
        {
            _uiApp.Idling -= _idle;
            _started = false;
        }

        try
        {
            _preview.SetSegments(Array.Empty<PreviewSegment>());
            _preview.Unregister();
        }
        catch
        {
            // Revit may already be closing the document.
        }
    }

    public void PickSurface() => RunApi(app =>
    {
        var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        Adopt(uidoc, TopoEditor.PickSurface(uidoc));
        _window.SetStatus("Click the surface. The blue rings are the area each click will change.", false);
    });

    public void AddLines() => RunApi(app => Stamp(app, edges: false));

    public void AddEdges() => RunApi(app => Stamp(app, edges: true));

    public void CreateFromLines() => RunApi(app => CreateFrom(app, edges: false));

    public void CreateFromEdges() => RunApi(app => CreateFrom(app, edges: true));

    public void PreviewEffect()
    {
        if (!_window.TryRead(out var settings))
            return;
        var uidoc = _uiApp.ActiveUIDocument;
        var topo = Surface(uidoc);
        if (uidoc is null || topo is null)
        {
            _window.SetStatus("Pick a surface first.", true);
            return;
        }

        _previewSnap ??= TopoEditor.Capture(topo);
        TopoEditor.Transact(uidoc.Document, settings.Tool == TopoTool.Inflate ? "Inflate topography" : "Smooth topography", () =>
        {
            TopoEditor.Restore(topo, _previewSnap);
            if (settings.Tool == TopoTool.Inflate)
                TopoEditor.Inflate(topo, settings.InflatePercent, settings.Cell);
            else
                TopoEditor.SmoothSurface(topo, settings.SmoothPasses);
        });
        _probe = null;
        _previewKey = "";
        _window.SetStatus(settings.Tool == TopoTool.Inflate
            ? $"Previewing inflation at {settings.InflatePercent:0}%. Apply keeps it, or move the slider and preview again."
            : "Previewing the smooth. Apply keeps it.", false);
    }

    public void ApplyEffect()
    {
        var topo = Surface(_uiApp.ActiveUIDocument);
        if (_previewSnap is null || topo is null)
        {
            _window.SetStatus("Nothing to apply. Use Preview first.", false);
            return;
        }

        _undo.Push(_previewSnap);
        _previewSnap = null;
        _window.SetStatus("Applied. Undo or Revert can still put the surface back.", false);
    }

    public void Undo()
    {
        var uidoc = _uiApp.ActiveUIDocument;
        var topo = Surface(uidoc);
        if (uidoc is null || topo is null)
            return;
        if (_previewSnap is not null)
        {
            var snap = _previewSnap;
            _previewSnap = null;
            TopoEditor.Transact(uidoc.Document, "Undo topography preview", () => TopoEditor.Restore(topo, snap));
            _probe = null;
            _window.SetStatus("Preview discarded.", false);
            return;
        }

        if (_undo.Count == 0)
        {
            _window.SetStatus("Nothing to undo.", false);
            return;
        }

        var previous = _undo.Pop();
        TopoEditor.Transact(uidoc.Document, "Undo topography edit", () => TopoEditor.Restore(topo, previous));
        _probe = null;
        _previewKey = "";
        _window.SetStatus("Undid the last edit.", false);
    }

    public void Revert()
    {
        var uidoc = _uiApp.ActiveUIDocument;
        var topo = Surface(uidoc);
        if (uidoc is null || topo is null)
            return;
        TopoEditor.Transact(uidoc.Document, "Revert topography", () => TopoEditor.Restore(topo, _baseline));
        _undo.Clear();
        _previewSnap = null;
        _probe = null;
        _anchor = null;
        _previewKey = "";
        _window.SetStatus("Reverted to the surface as it was when you picked it.", false);
    }

    private void OnIdle(object? sender, IdlingEventArgs e)
    {
        if (_suspended || !_window.IsLoaded || _busy)
            return;

        var now = Environment.TickCount64;
        if (now < _nextIdle)
        {
            e.SetRaiseWithoutDelay();
            return;
        }

        _busy = true;
        try
        {
            Tick();
            _nextIdle = Environment.TickCount64 + 40;
            e.SetRaiseWithoutDelay();
        }
        catch (Exception ex)
        {
            _window.SetStatus(ex.Message, true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Tick()
    {
        if (!_window.TryRead(out var settings))
            return;
        if (settings.Tool != _tool)
        {
            _tool = settings.Tool;
            _anchor = null;
            _previewKey = "";
        }

        var cursor = TopoPointer.Cursor();
        var down = TopoPointer.LeftDown();
        var pressed = down && !_down;
        _down = down;
        if (_window.ContainsScreenPoint(cursor.X, cursor.Y))
            return;

        var moved = !_haveCursor || cursor != _lastCursor;
        _lastCursor = cursor;
        _haveCursor = true;
        if (!moved && !pressed)
            return;

        var uidoc = _uiApp.ActiveUIDocument;
        EnsureSurface(uidoc);
        if (uidoc is null || !TopoPointer.TryAim(uidoc, cursor.X, cursor.Y, out var aim))
            return;

        XYZ? hit = null;
        if (settings.Tool == TopoTool.Pad)
        {
            if (aim.Vertical)
                hit = new XYZ(aim.X, aim.Y, settings.Elevation);
        }
        else if (Locate(uidoc, aim, out var point))
        {
            hit = point;
        }

        UpdatePreview(uidoc, settings, hit, aim);
        if (!pressed || hit is null)
            return;
        if (settings.Tool == TopoTool.Pad)
            ClickPad(uidoc, settings, hit);
        else
            ClickSurface(uidoc, settings, hit);
    }

    private void ClickSurface(UIDocument uidoc, TopoSettings settings, XYZ hit)
    {
        var topo = Surface(uidoc);
        if (topo is null)
        {
            _window.SetStatus("Pick a surface first.", true);
            return;
        }

        if (_previewSnap is not null)
        {
            _undo.Push(_previewSnap);
            _previewSnap = null;
        }

        var doc = uidoc.Document;
        switch (settings.Tool)
        {
            case TopoTool.Shape:
                Edit(doc, topo, "Shape by point", () => TopoEditor.Sculpt(topo, hit.X, hit.Y, settings.OuterRing, settings.Density, settings.Gain, settings.Shape));
                break;
            case TopoTool.Mesh:
                var delta = settings.Shape == ShapeAction.Lower ? -settings.Gain : settings.Gain;
                Edit(doc, topo, "Move mesh point", () => TopoEditor.NudgeNearest(topo, hit.X, hit.Y, delta, Math.Max(settings.Cell * 0.45, 1)));
                break;
            case TopoTool.Point:
                Edit(doc, topo, "Place topography point", () => settings.RelativePoint
                    ? TopoEditor.PlaceRelative(topo, hit.X, hit.Y, settings.Elevation)
                    : TopoEditor.PlaceAbsolute(topo, hit.X, hit.Y, settings.Elevation));
                break;
            case TopoTool.Mid:
                SecondClick(doc, topo, hit, "Insert midpoint", (a, b) => TopoEditor.InsertMidpoint(topo, a, b));
                break;
            case TopoTool.Split:
                SecondClick(doc, topo, hit, "Split topography", (a, b) => TopoEditor.SplitLine(topo, a, b));
                break;
            case TopoTool.Slope:
                SecondClick(doc, topo, hit, "Slope topography", (a, b) =>
                {
                    var run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                    var end = settings.SlopeByAngle ? a.Z + run * Math.Tan(settings.SlopeDegrees * Math.PI / 180) : settings.SlopeEnd;
                    TopoEditor.SlopeBetween(topo, a, b, end);
                    return 1;
                });
                break;
        }
    }

    private void SecondClick(Document doc, Toposolid topo, XYZ hit, string name, Func<XYZ, XYZ, int> apply)
    {
        if (_anchor is null)
        {
            _anchor = hit;
            _window.SetStatus("Click the second point.", false);
            return;
        }

        var start = _anchor;
        _anchor = null;
        Edit(doc, topo, name, () => apply(start, hit));
    }

    private void ClickPad(UIDocument uidoc, TopoSettings settings, XYZ hit)
    {
        if (settings.LevelId == ElementId.InvalidElementId || settings.TypeId == ElementId.InvalidElementId)
        {
            _window.SetStatus("Choose a level and a toposolid type on the Create page.", true);
            return;
        }

        if (_anchor is null)
        {
            _anchor = hit;
            _window.SetStatus("Click the opposite corner.", false);
            return;
        }

        var start = _anchor;
        _anchor = null;
        if (Math.Abs(hit.X - start.X) < 1 || Math.Abs(hit.Y - start.Y) < 1)
        {
            _window.SetStatus("The pad is too small. Click two corners farther apart.", true);
            return;
        }

        var created = TopoEditor.CreatePad(uidoc.Document, start.X, start.Y, hit.X, hit.Y, settings.Elevation, settings.TypeId, settings.LevelId);
        Adopt(uidoc, created);
        _window.SetStatus("Pad created. Shape by point now sculpts it.", false);
    }

    private void Edit(Document doc, Toposolid topo, string name, Func<int> action)
    {
        var before = TopoEditor.Capture(topo);
        var changed = 0;
        TopoEditor.Transact(doc, name, () => changed = action());
        if (changed > 0)
            _undo.Push(before);
        _probe = null;
        _previewKey = "";
        if (changed == 0)
            _window.SetStatus("Nothing changed. Click inside the surface.", false);
    }

    private void UpdatePreview(UIDocument uidoc, TopoSettings settings, XYZ? hit, TopoPointer.Aim aim)
    {
        var ringKey = hit is null || !settings.ShowRings
            ? ""
            : $"{hit.X:0.0}:{hit.Y:0.0}:{hit.Z:0.0}:{settings.OuterRing:0.0}:{settings.Density}";
        var gridKey = settings.ShowGrid ? $"{settings.Cell:0.0}:{settings.Rotation:0}" : "";
        var anchorKey = _anchor is null || hit is null ? "" : $"{_anchor.X:0.0}:{_anchor.Y:0.0}:{hit.X:0.0}:{hit.Y:0.0}";
        var key = $"{settings.Tool}|{ringKey}|{gridKey}|{anchorKey}";
        if (key == _previewKey)
            return;
        _previewKey = key;

        var segments = new List<PreviewSegment>();
        if (hit is not null && settings.ShowRings)
            AddRings(segments, hit, settings.OuterRing, settings.Density);
        if (_anchor is not null && hit is not null)
            segments.Add(new PreviewSegment(_anchor + new XYZ(0, 0, 0.8), hit + new XYZ(0, 0, 0.8), 230, 81, 0));
        if (settings.ShowGrid)
            AddGrid(segments, settings);

        _preview.SetSegments(segments);
        uidoc.RefreshActiveView();
    }

    private void AddRings(List<PreviewSegment> segments, XYZ center, double outer, int rings)
    {
        var z = center.Z + 0.8;
        AddCircle(segments, center.X, center.Y, z, Math.Max(outer, 0.5), 48, 21, 101, 192);
        for (var ring = 1; ring < rings; ring++)
            AddCircle(segments, center.X, center.Y, z, outer * ring / rings, 24, 66, 165, 245);
        for (var spoke = 0; spoke < 8; spoke++)
        {
            var angle = spoke * Math.PI / 4;
            segments.Add(new PreviewSegment(
                new XYZ(center.X, center.Y, z),
                new XYZ(center.X + outer * Math.Cos(angle), center.Y + outer * Math.Sin(angle), z),
                21, 101, 192));
        }
    }

    private static void AddCircle(List<PreviewSegment> segments, double x, double y, double z, double radius, int count, byte r, byte g, byte b)
    {
        XYZ? previous = null;
        for (var i = 0; i <= count; i++)
        {
            var angle = 2 * Math.PI * i / count;
            var point = new XYZ(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle), z);
            if (previous is not null)
                segments.Add(new PreviewSegment(previous, point, r, g, b));
            previous = point;
        }
    }

    private void AddGrid(List<PreviewSegment> segments, TopoSettings settings)
    {
        var topo = Surface(_uiApp.ActiveUIDocument);
        if (topo is null)
            return;
        _probe ??= TopoProbe.Build(topo);
        if (_probe is null)
            return;
        var cell = Math.Max(settings.Cell, 1);
        var angle = settings.Rotation * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var cx = (_probe.MinX + _probe.MaxX) / 2;
        var cy = (_probe.MinY + _probe.MaxY) / 2;
        var reach = Math.Sqrt(Math.Pow(_probe.MaxX - _probe.MinX, 2) + Math.Pow(_probe.MaxY - _probe.MinY, 2));
        var count = Math.Min(24, (int)Math.Ceiling(reach / cell));
        var z = _probe.MaxZ + 0.6;
        for (var i = -count; i <= count; i++)
        {
            XYZ? rowStart = null;
            XYZ? rowEnd = null;
            XYZ? colStart = null;
            XYZ? colEnd = null;
            for (var j = -count; j <= count; j++)
            {
                var row = ToGrid(cx, cy, i * cell, j * cell, cosine, sine, z);
                var col = ToGrid(cx, cy, j * cell, i * cell, cosine, sine, z);
                if (_probe.TryVertical(row.X, row.Y, out _))
                {
                    rowStart ??= row;
                    rowEnd = row;
                }

                if (_probe.TryVertical(col.X, col.Y, out _))
                {
                    colStart ??= col;
                    colEnd = col;
                }
            }

            if (rowStart is not null && rowEnd is not null && rowStart.DistanceTo(rowEnd) > cell)
                segments.Add(new PreviewSegment(rowStart, rowEnd, 144, 202, 249));
            if (colStart is not null && colEnd is not null && colStart.DistanceTo(colEnd) > cell)
                segments.Add(new PreviewSegment(colStart, colEnd, 144, 202, 249));
        }
    }

    private static XYZ ToGrid(double cx, double cy, double localX, double localY, double cosine, double sine, double z) =>
        new(cx + localX * cosine - localY * sine, cy + localX * sine + localY * cosine, z);

    private bool Locate(UIDocument uidoc, TopoPointer.Aim aim, out XYZ point)
    {
        point = XYZ.Zero;
        var topo = Surface(uidoc);
        if (topo is null)
            return false;
        _probe ??= TopoProbe.Build(topo);
        if (_probe is null)
            return false;
        if (aim.Vertical)
        {
            if (!_probe.TryVertical(aim.X, aim.Y, out var z))
                return false;
            point = new XYZ(aim.X, aim.Y, z);
            return true;
        }

        return _probe.TryRay(aim.Origin, aim.Direction, out point);
    }

    private void Stamp(UIApplication app, bool edges)
    {
        var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var topo = Surface(uidoc) ?? throw new InvalidOperationException("Pick a surface first.");
        var refs = edges ? TopoEditor.PickEdgeSources(uidoc) : TopoEditor.PickLines(uidoc);
        var elements = refs.Select(uidoc.Document.GetElement).ToList();
        var before = TopoEditor.Capture(topo);
        var count = 0;
        TopoEditor.Transact(uidoc.Document, edges ? "Topography from edges" : "Add lines to topography",
            () => count = TopoEditor.StampCurves(topo, elements, edges));
        if (count > 0)
            _undo.Push(before);
        _probe = null;
        _window.SetStatus(count == 0 ? "No points fell inside the surface." : $"Added {count} point(s).", count == 0);
    }

    private void CreateFrom(UIApplication app, bool edges)
    {
        if (!_window.TryRead(out var settings) || settings.LevelId == ElementId.InvalidElementId || settings.TypeId == ElementId.InvalidElementId)
            throw new InvalidOperationException("Choose a level and a toposolid type.");
        var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var refs = edges ? TopoEditor.PickEdgeSources(uidoc) : TopoEditor.PickLines(uidoc);
        var elements = refs.Select(uidoc.Document.GetElement).ToList();
        var created = TopoEditor.CreateFromElements(uidoc.Document, elements, edges, settings.TypeId, settings.LevelId);
        Adopt(uidoc, created);
        _window.SetStatus("Surface created.", false);
    }

    private void Adopt(UIDocument uidoc, Toposolid topo)
    {
        TopoEditor.Transact(uidoc.Document, "Prepare topography", () => TopoEditor.Prepare(topo));
        _surfaceId = topo.Id;
        _baseline = TopoEditor.Capture(topo);
        _undo.Clear();
        _previewSnap = null;
        _probe = null;
        _anchor = null;
        _previewKey = "";
        uidoc.Selection.SetElementIds(new List<ElementId> { topo.Id });
        var typeName = (uidoc.Document.GetElement(topo.GetTypeId()) as ElementType)?.Name;
        _window.SetSurface(string.IsNullOrWhiteSpace(typeName) ? "Toposolid" : typeName);
    }

    private void EnsureSurface(UIDocument? uidoc)
    {
        if (uidoc is null)
            return;
        var ids = uidoc.Selection.GetElementIds();
        if (ids.Count == 1 && uidoc.Document.GetElement(ids.First()) is Toposolid selected && selected.Id != _surfaceId)
        {
            Adopt(uidoc, selected);
            _window.SetStatus("Click the surface. The blue rings are the area each click will change.", false);
            return;
        }

        if (Surface(uidoc) is null)
            _surfaceId = null;
    }

    private Toposolid? Surface(UIDocument? uidoc)
    {
        if (uidoc is null || _surfaceId is null)
            return null;
        return uidoc.Document.GetElement(_surfaceId) as Toposolid;
    }

    private void RunApi(Action<UIApplication> work)
    {
        _suspended = true;
        _handler.Work = work;
        if (_event.Raise() != ExternalEventRequest.Accepted)
        {
            _suspended = false;
            _handler.Work = null;
            _window.SetStatus("Revit is busy. Try again in a moment.", true);
        }
    }

    public void OnApiFinished(string? message)
    {
        _suspended = false;
        if (!string.IsNullOrEmpty(message))
            _window.SetStatus(message, message != "Cancelled.");
    }
}
