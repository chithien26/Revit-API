using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CTTools.Features.Topo;

/// <summary>
/// Creates and sculpts Revit 2025 toposolids with the public API:
/// pads and contour lines, local raise/lower, smoothing, slope paths, and edges of existing elements.
/// </summary>
public static class TopoEditor
{
    private const double PointGap = 0.15; // feet, about 45 mm

    public static void Run(UIApplication app, TopoRequest request)
    {
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc?.Document;
        if (uidoc is null || doc is null)
        {
            request.Result = "Open a project first.";
            return;
        }

        switch (request.Mode)
        {
            case TopoMode.NewPad:
                CreatePad(uidoc, request);
                break;
            case TopoMode.NewFromLines:
                CreateFromCurves(uidoc, request, edges: false);
                break;
            case TopoMode.FromEdges:
                CreateFromCurves(uidoc, request, edges: true);
                break;
            case TopoMode.AddPoints:
                AddPoints(uidoc, request);
                break;
            case TopoMode.AddLines:
                StampLines(uidoc, request);
                break;
            case TopoMode.ShapeByPoint:
                ShapeByPoint(uidoc, request);
                break;
            case TopoMode.Smooth:
                Smooth(uidoc, request);
                break;
            case TopoMode.InsertMidpoint:
                InsertMidpoints(uidoc, request);
                break;
            case TopoMode.SlopePath:
                SlopePaths(uidoc, request);
                break;
        }
    }

    private static void CreatePad(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var p1 = uidoc.Selection.PickPoint("Pick the first corner of the pad. Use a plan view.");
        var p2 = uidoc.Selection.PickPoint("Pick the opposite corner.");
        if (Math.Abs(p2.X - p1.X) < 1 || Math.Abs(p2.Y - p1.Y) < 1)
        {
            request.Result = "The pad is too small. Pick two corners farther apart.";
            return;
        }

        double z = request.Elevation;
        var points = new List<XYZ>
        {
            new(p1.X, p1.Y, z),
            new(p2.X, p1.Y, z),
            new(p2.X, p2.Y, z),
            new(p1.X, p2.Y, z),
        };
        var topo = CreateSurface(doc, points, request, "Create topography pad");
        uidoc.Selection.SetElementIds(new List<ElementId> { topo.Id });
        request.Result = "Created a flat pad. Use Shape by point to sculpt it.";
    }

    private static void CreateFromCurves(UIDocument uidoc, TopoRequest request, bool edges)
    {
        var doc = uidoc.Document;
        IList<Reference> refs = edges
            ? uidoc.Selection.PickObjects(ObjectType.Element, new EdgeSourceFilter(),
                "Select walls, floors, roofs, or model lines. Click Finish on the ribbon.")
            : uidoc.Selection.PickObjects(ObjectType.Element, new ModelCurveFilter(),
                "Select model lines drawn at the contour elevation. Detail lines are ignored. Click Finish.");

        var points = new List<XYZ>();
        foreach (var reference in refs)
        {
            var element = doc.GetElement(reference);
            foreach (var curve in edges ? TopEdges(element) : ModelCurves(element))
                points.AddRange(Sample(curve));
        }

        points = Dedupe(points);
        if (points.Count < 3)
        {
            request.Result = edges
                ? "Need at least 3 points. Pick elements that have a top edge."
                : "Need at least 3 points. Pick model lines, not detail lines.";
            return;
        }

        var topo = CreateSurface(doc, points, request, edges ? "Topography from edges" : "Topography from lines");
        uidoc.Selection.SetElementIds(new List<ElementId> { topo.Id });
        request.Result = $"Created a surface from {points.Count} points.";
    }

    private static void AddPoints(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface, then click where each point goes. Press Esc when finished.");
        var placed = 0;
        while (true)
        {
            Reference hit;
            try
            {
                hit = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(),
                    "Click inside the surface to place a point. Press Esc when finished.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            if (hit.ElementId != topo.Id)
                continue;

            var click = hit.GlobalPoint;
            if (!TryElevationAt(topo, click.X, click.Y, out var surfaceZ))
                surfaceZ = click.Z;
            var z = request.ElevationIsOffset ? surfaceZ + request.Elevation : request.Elevation;
            placed += InTransaction(doc, "Add topography point", () =>
                Place(topo, new[] { (click.X, click.Y, z) }));
        }

        request.Result = placed == 0 ? "No points added." : $"Added {placed} point(s).";
    }

    private static void StampLines(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface that will receive the lines.");
        var refs = uidoc.Selection.PickObjects(ObjectType.Element, new ModelCurveFilter(),
            "Select model lines to stamp onto the surface. Click Finish.");
        var spots = new List<(double X, double Y, double Z)>();
        foreach (var reference in refs)
        {
            foreach (var curve in ModelCurves(doc.GetElement(reference)))
            {
                foreach (var point in Sample(curve))
                {
                    if (TryElevationAt(topo, point.X, point.Y, out _))
                        spots.Add((point.X, point.Y, point.Z));
                }
            }
        }

        var placed = InTransaction(doc, "Add lines to topography", () => Place(topo, spots));
        request.Result = $"Stamped {placed} point(s) onto the surface. Lines outside the boundary were skipped.";
    }

    private static void ShapeByPoint(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface to sculpt.");
        if (request.Radius <= PointGap)
        {
            request.Result = "Radius is too small.";
            return;
        }

        var clicks = 0;
        var moved = 0;
        while (true)
        {
            Reference hit;
            try
            {
                hit = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(),
                    "Click the surface to sculpt. Press Esc when finished.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            if (hit.ElementId != topo.Id)
                continue;

            var click = hit.GlobalPoint;
            var samples = new List<(double X, double Y, double Z, double W)>();
            foreach (var spot in Brush(click.X, click.Y, request.Radius, request.Rings))
            {
                if (TryElevationAt(topo, spot.X, spot.Y, out var z))
                    samples.Add((spot.X, spot.Y, z, spot.W));
            }

            if (samples.Count == 0)
                continue;

            var average = samples.Average(p => p.Z);
            var targets = new List<(double X, double Y, double Z)>();
            foreach (var spot in samples.OrderByDescending(p => p.W))
            {
                if (targets.Any(kept => FlatDistance(new XYZ(kept.X, kept.Y, 0), spot.X, spot.Y) < PointGap))
                    continue;
                var z = request.Shape switch
                {
                    ShapeAction.Lower => spot.Z - request.Gain * spot.W,
                    ShapeAction.SmoothOut => spot.Z + (average - spot.Z) * spot.W,
                    _ => spot.Z + request.Gain * spot.W,
                };
                targets.Add((spot.X, spot.Y, z));
            }

            moved += InTransaction(doc, "Shape topography", () => Place(topo, targets));
            clicks++;
        }

        request.Result = clicks == 0
            ? "No sculpt applied."
            : $"Sculpted {clicks} click(s), {moved} point(s). Ctrl+Z undoes one click.";
    }

    private static void Smooth(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface to smooth.");
        var changed = InTransaction(doc, "Smooth topography", () => SmoothOnce(topo, request.SmoothPasses));
        request.Result = changed == 0
            ? "No interior points to smooth. Sculpt or add points first."
            : $"Smoothed {changed} point(s) over {request.SmoothPasses} pass(es).";
    }

    private static void InsertMidpoints(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface.");
        var added = 0;
        while (true)
        {
            Reference a;
            Reference b;
            try
            {
                a = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(), "Pick the first point. Esc to stop.");
                b = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(), "Pick the second point.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            if (a.ElementId != topo.Id || b.ElementId != topo.Id)
                continue;

            var midX = (a.GlobalPoint.X + b.GlobalPoint.X) / 2;
            var midY = (a.GlobalPoint.Y + b.GlobalPoint.Y) / 2;
            var midZ = (a.GlobalPoint.Z + b.GlobalPoint.Z) / 2;
            added += InTransaction(doc, "Insert topography midpoint", () => Place(topo, new[] { (midX, midY, midZ) }));
        }

        request.Result = added == 0 ? "No midpoint added." : $"Inserted {added} midpoint(s).";
    }

    private static void SlopePaths(UIDocument uidoc, TopoRequest request)
    {
        var doc = uidoc.Document;
        var topo = PickTopo(uidoc, "Pick the surface.");
        var paths = 0;
        while (true)
        {
            Reference start;
            Reference end;
            try
            {
                start = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(),
                    "Pick the start of the slope. Esc to stop.");
                end = uidoc.Selection.PickObject(ObjectType.PointOnElement, new TopoFilter(), "Pick the end of the slope.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }

            if (start.ElementId != topo.Id || end.ElementId != topo.Id)
                continue;

            var a = start.GlobalPoint;
            var b = end.GlobalPoint;
            var run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (run < PointGap)
                continue;

            var endZ = request.SlopeByAngle
                ? a.Z + run * Math.Tan(request.SlopeDegrees * Math.PI / 180)
                : request.SlopeEndElevation;

            var spacing = Math.Max(run / 20, 0.5);
            var spots = new List<(double X, double Y, double Z)>();
            var count = Math.Max(2, (int)Math.Ceiling(run / spacing));
            for (var i = 0; i <= count; i++)
            {
                var t = i / (double)count;
                var z = a.Z + t * (endZ - a.Z);
                spots.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, z));
            }

            InTransaction(doc, "Slope topography", () =>
            {
                var editor = Open(topo);
                var verts = Vertices(editor);
                var basis = BaseElevation(topo);
                foreach (var vertex in verts)
                {
                    if (!ProjectsOnto(a, b, vertex.Position, out var t, out var distance))
                        continue;
                    if (distance > spacing * 0.75)
                        continue;
                    var z = a.Z + t * (endZ - a.Z);
                    editor.ModifySubElement(vertex, z - basis);
                }

                Place(topo, spots);
                return 0;
            });
            paths++;
        }

        request.Result = paths == 0 ? "No slope applied." : $"Applied {paths} slope path(s).";
    }

    private static Toposolid CreateSurface(Document doc, IList<XYZ> points, TopoRequest request, string name)
    {
        Toposolid? created = null;
        InTransaction(doc, name, () =>
        {
            var unique = Dedupe(points);
            try
            {
                created = Toposolid.Create(doc, unique, request.TypeId, request.LevelId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                var level = (Level)doc.GetElement(request.LevelId);
                var loop = BoxLoop(unique, 2, level.Elevation);
                created = Toposolid.Create(doc, new List<CurveLoop> { loop }, unique, request.TypeId, request.LevelId);
            }

            return 0;
        });
        return created ?? throw new InvalidOperationException("Revit could not create the surface.");
    }

    private static int Place(Toposolid topo, IEnumerable<(double X, double Y, double Z)> spots)
    {
        var editor = Open(topo);
        var existing = Vertices(editor);
        var basis = BaseElevation(topo);
        var placed = 0;
        foreach (var spot in spots)
        {
            var near = existing.FirstOrDefault(v => FlatDistance(v.Position, spot.X, spot.Y) < PointGap);
            if (near is not null)
            {
                editor.ModifySubElement(near, spot.Z - basis);
                placed++;
                continue;
            }

            if (!TryElevationAt(topo, spot.X, spot.Y, out _))
                continue;

            try
            {
                var added = editor.AddPoint(new XYZ(spot.X, spot.Y, spot.Z));
                editor.ModifySubElement(added, spot.Z - basis);
                existing.Add(added);
                placed++;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Outside the boundary, or on top of a point Revit refuses.
            }
        }

        return placed;
    }

    private static int SmoothOnce(Toposolid topo, int passes)
    {
        var editor = Open(topo);
        var verts = Vertices(editor);
        var neighbors = new Dictionary<(long X, long Y), List<SlabShapeVertex>>();
        foreach (var vertex in verts)
            neighbors[Key(vertex.Position)] = new List<SlabShapeVertex>();

        foreach (SlabShapeCrease crease in editor.SlabShapeCreases)
        {
            var ends = new List<SlabShapeVertex>();
            foreach (SlabShapeVertex end in crease.EndPoints)
                ends.Add(end);
            for (var i = 0; i < ends.Count; i++)
            {
                for (var j = i + 1; j < ends.Count; j++)
                {
                    if (!neighbors.ContainsKey(Key(ends[i].Position)) || !neighbors.ContainsKey(Key(ends[j].Position)))
                        continue;
                    neighbors[Key(ends[i].Position)].Add(ends[j]);
                    neighbors[Key(ends[j].Position)].Add(ends[i]);
                }
            }
        }

        var height = verts.ToDictionary(v => Key(v.Position), v => v.Position.Z);
        var movable = verts.Where(v => v.VertexType != SlabShapeVertexType.Corner && neighbors[Key(v.Position)].Count > 0).ToList();
        if (movable.Count == 0)
            return 0;

        for (var pass = 0; pass < passes; pass++)
        {
            var next = new Dictionary<(long X, long Y), double>(height);
            foreach (var vertex in movable)
            {
                var key = Key(vertex.Position);
                next[key] = neighbors[key].Average(n => height[Key(n.Position)]);
            }

            height = next;
        }

        var basis = BaseElevation(topo);
        foreach (var vertex in movable)
            editor.ModifySubElement(vertex, height[Key(vertex.Position)] - basis);
        return movable.Count;
    }

    private static (long X, long Y) Key(XYZ point) =>
        ((long)Math.Round(point.X * 10000), (long)Math.Round(point.Y * 10000));

    private static SlabShapeEditor Open(Toposolid topo)
    {
        var editor = topo.GetSlabShapeEditor();
        if (!editor.IsEnabled)
            editor.Enable();
        return editor;
    }

    private static List<SlabShapeVertex> Vertices(SlabShapeEditor editor)
    {
        var list = new List<SlabShapeVertex>();
        foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            list.Add(vertex);
        return list;
    }

    private static double BaseElevation(Toposolid topo)
    {
        var level = (Level)topo.Document.GetElement(topo.LevelId);
        var offset = topo.get_Parameter(BuiltInParameter.TOPOSOLID_HEIGHTABOVELEVEL_PARAM)?.AsDouble() ?? 0;
        return level.Elevation + offset;
    }

    private static bool TryElevationAt(Toposolid topo, double x, double y, out double z)
    {
        z = 0;
        var box = topo.get_BoundingBox(null);
        if (box is null)
            return false;

        var line = Line.CreateBound(new XYZ(x, y, box.Max.Z + 10), new XYZ(x, y, box.Min.Z - 10));
        double? best = null;
        foreach (var solid in Solids(topo.get_Geometry(FineGeometry)))
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace planar && planar.FaceNormal.Z <= 0.05)
                    continue;
                var compared = face.Intersect(line, out IntersectionResultArray hits);
                if (compared != SetComparisonResult.Overlap || hits is null)
                    continue;
                foreach (IntersectionResult hit in hits)
                {
                    if (best is null || hit.XYZPoint.Z > best)
                        best = hit.XYZPoint.Z;
                }
            }
        }

        if (best is null)
            return false;
        z = best.Value;
        return true;
    }

    private static readonly Options FineGeometry = new()
    {
        ComputeReferences = false,
        IncludeNonVisibleObjects = false,
        DetailLevel = ViewDetailLevel.Fine,
    };

    private static IEnumerable<Solid> Solids(GeometryElement? geometry)
    {
        if (geometry is null)
            yield break;
        foreach (var item in geometry)
        {
            if (item is Solid solid && solid.Faces.Size > 0)
                yield return solid;
            else if (item is GeometryInstance instance)
            {
                foreach (var inner in Solids(instance.GetInstanceGeometry()))
                    yield return inner;
            }
        }
    }

    private static IEnumerable<Curve> ModelCurves(Element element)
    {
        if (element is ModelCurve model && model.GeometryCurve is Curve curve)
            yield return curve;
    }

    private static IEnumerable<Curve> TopEdges(Element element)
    {
        if (element is ModelCurve)
        {
            foreach (var curve in ModelCurves(element))
                yield return curve;
            yield break;
        }

        var geometry = element.get_Geometry(FineGeometry);
        foreach (var solid in Solids(geometry))
        {
            foreach (Edge edge in solid.Edges)
            {
                if (FacesUp(edge))
                    yield return edge.AsCurve();
            }
        }
    }

    private static bool FacesUp(Edge edge)
    {
        for (var i = 0; i < 2; i++)
        {
            var face = edge.GetFace(i);
            if (face is PlanarFace planar && planar.FaceNormal.Z > 0.3)
                return true;
        }

        return false;
    }

    private static List<XYZ> Sample(Curve curve)
    {
        IList<XYZ> raw;
        try
        {
            raw = curve.Tessellate();
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            raw = new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) };
        }

        return raw.ToList();
    }

    private static List<XYZ> Dedupe(IEnumerable<XYZ> points)
    {
        var kept = new List<XYZ>();
        foreach (var point in points)
        {
            if (kept.Any(other => FlatDistance(other, point.X, point.Y) < PointGap))
                continue;
            kept.Add(point);
        }

        return kept;
    }

    private static List<(double X, double Y, double W)> Brush(double cx, double cy, double radius, int rings)
    {
        var spots = new List<(double X, double Y, double W)> { (cx, cy, 1) };
        for (var ring = 1; ring <= rings; ring++)
        {
            var r = radius * ring / rings;
            if (r >= radius)
                r = radius * 0.98;
            var count = 6 * ring;
            var weight = Falloff(r, radius);
            var twist = ring % 2 == 0 ? 0 : Math.PI / count;
            for (var i = 0; i < count; i++)
            {
                var angle = 2 * Math.PI * i / count + twist;
                spots.Add((cx + r * Math.Cos(angle), cy + r * Math.Sin(angle), weight));
            }
        }

        return spots;
    }

    private static double Falloff(double distance, double radius)
    {
        if (distance >= radius || radius <= 1e-9)
            return 0;
        var t = distance / radius;
        var smooth = t * t * (3 - 2 * t);
        return 1 - smooth;
    }

    private static CurveLoop BoxLoop(IList<XYZ> points, double pad, double z)
    {
        var minX = points.Min(p => p.X) - pad;
        var maxX = points.Max(p => p.X) + pad;
        var minY = points.Min(p => p.Y) - pad;
        var maxY = points.Max(p => p.Y) + pad;
        var a = new XYZ(minX, minY, z);
        var b = new XYZ(maxX, minY, z);
        var c = new XYZ(maxX, maxY, z);
        var d = new XYZ(minX, maxY, z);
        var loop = new CurveLoop();
        loop.Append(Line.CreateBound(a, b));
        loop.Append(Line.CreateBound(b, c));
        loop.Append(Line.CreateBound(c, d));
        loop.Append(Line.CreateBound(d, a));
        return loop;
    }

    private static bool ProjectsOnto(XYZ a, XYZ b, XYZ point, out double t, out double distance)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var length2 = abx * abx + aby * aby;
        if (length2 < 1e-9)
        {
            t = 0;
            distance = double.MaxValue;
            return false;
        }

        t = ((point.X - a.X) * abx + (point.Y - a.Y) * aby) / length2;
        if (t < 0 || t > 1)
        {
            distance = double.MaxValue;
            return false;
        }

        var x = a.X + abx * t;
        var y = a.Y + aby * t;
        distance = Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));
        return true;
    }

    private static double FlatDistance(XYZ point, double x, double y)
    {
        var dx = point.X - x;
        var dy = point.Y - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Toposolid PickTopo(UIDocument uidoc, string prompt)
    {
        var reference = uidoc.Selection.PickObject(ObjectType.Element, new TopoFilter(), prompt);
        return (Toposolid)uidoc.Document.GetElement(reference);
    }

    private static int InTransaction(Document doc, string name, Func<int> action)
    {
        using var transaction = new Transaction(doc, name);
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSilencer());
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();
        try
        {
            var result = action();
            transaction.Commit();
            return result;
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
                transaction.RollBack();
            throw;
        }
    }

    private sealed class WarningSilencer : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var message in accessor.GetFailureMessages())
            {
                if (message.GetSeverity() == FailureSeverity.Warning)
                    accessor.DeleteWarning(message);
            }

            return FailureProcessingResult.Continue;
        }
    }

    private sealed class TopoFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is Toposolid;
        public bool AllowReference(Reference reference, XYZ position) => true;
    }

    private sealed class ModelCurveFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) => element is ModelCurve;
        public bool AllowReference(Reference reference, XYZ position) => false;
    }

    private sealed class EdgeSourceFilter : ISelectionFilter
    {
        public bool AllowElement(Element element) =>
            element is Wall or Floor or RoofBase or ModelCurve or FamilyInstance or DirectShape;

        public bool AllowReference(Reference reference, XYZ position) => false;
    }
}
