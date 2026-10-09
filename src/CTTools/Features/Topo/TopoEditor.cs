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

    public static void Transact(Document doc, string name, Action work) =>
        InTransaction(doc, name, () =>
        {
            work();
            return 0;
        });

    public static int Sculpt(Toposolid topo, double x, double y, double outerRing, int rings, double gain, ShapeAction action)
    {
        var probe = TopoProbe.Build(topo);
        if (probe is null || outerRing <= PointGap)
            return 0;

        var samples = new List<(double X, double Y, double Z, double W)>();
        foreach (var spot in Brush(x, y, outerRing, Math.Clamp(rings, 1, 12)))
        {
            if (probe.TryVertical(spot.X, spot.Y, out var z))
                samples.Add((spot.X, spot.Y, z, spot.W));
        }

        if (samples.Count == 0)
            return 0;

        var average = samples.Average(p => p.Z);
        var targets = new List<(double X, double Y, double Z)>();
        foreach (var spot in samples.OrderByDescending(p => p.W))
        {
            if (targets.Any(kept => FlatDistance(new XYZ(kept.X, kept.Y, 0), spot.X, spot.Y) < PointGap))
                continue;
            var z = action switch
            {
                ShapeAction.Lower => spot.Z - gain * spot.W,
                ShapeAction.SmoothOut => spot.Z + (average - spot.Z) * spot.W,
                _ => spot.Z + gain * spot.W,
            };
            targets.Add((spot.X, spot.Y, z));
        }

        return Place(topo, targets, probe);
    }

    public static int SmoothSurface(Toposolid topo, int passes) => SmoothOnce(topo, Math.Clamp(passes, 1, 4));

    public static void Inflate(Toposolid topo, double percent, double cell)
    {
        if (Math.Abs(percent) < 0.05)
            return;
        StampGrid(topo, cell, 0);
        var editor = Open(topo);
        var verts = Vertices(editor);
        if (verts.Count == 0)
            return;

        var minX = verts.Min(v => v.Position.X);
        var maxX = verts.Max(v => v.Position.X);
        var minY = verts.Min(v => v.Position.Y);
        var maxY = verts.Max(v => v.Position.Y);
        var rx = Math.Max((maxX - minX) / 2, 1);
        var ry = Math.Max((maxY - minY) / 2, 1);
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var amplitude = 0.15 * Math.Min(maxX - minX, maxY - minY) * (percent / 50.0);
        var basis = BaseElevation(topo);
        foreach (var vertex in verts)
        {
            if (vertex.VertexType == SlabShapeVertexType.Corner)
                continue;
            var nx = (vertex.Position.X - cx) / rx;
            var ny = (vertex.Position.Y - cy) / ry;
            var distance = Math.Sqrt(nx * nx + ny * ny);
            if (distance >= 1)
                continue;
            var dome = Math.Cos(distance * Math.PI / 2);
            editor.ModifySubElement(vertex, vertex.Position.Z + amplitude * dome - basis);
        }
    }

    public static void StampGrid(Toposolid topo, double cell, double rotationDegrees)
    {
        var probe = TopoProbe.Build(topo);
        if (probe is null)
            return;
        if (cell < 1)
            cell = 1;
        var angle = rotationDegrees * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        var cx = (probe.MinX + probe.MaxX) / 2;
        var cy = (probe.MinY + probe.MaxY) / 2;
        var reach = Math.Sqrt(Math.Pow(probe.MaxX - probe.MinX, 2) + Math.Pow(probe.MaxY - probe.MinY, 2));
        var count = Math.Min(30, (int)Math.Ceiling(reach / cell));
        var spots = new List<(double X, double Y, double Z)>();
        for (var i = -count; i <= count; i++)
        {
            for (var j = -count; j <= count; j++)
            {
                var localX = i * cell;
                var localY = j * cell;
                var x = cx + localX * cosine - localY * sine;
                var y = cy + localX * sine + localY * cosine;
                if (!probe.TryVertical(x, y, out var z))
                    continue;
                spots.Add((x, y, z));
            }
        }

        Place(topo, spots, probe);
    }

    public static int PlaceAbsolute(Toposolid topo, double x, double y, double z) =>
        Place(topo, new[] { (x, y, z) });

    public static int PlaceRelative(Toposolid topo, double x, double y, double offset)
    {
        if (!TryElevationAt(topo, x, y, out var z))
            return 0;
        return Place(topo, new[] { (x, y, z + offset) });
    }

    public static int InsertMidpoint(Toposolid topo, XYZ a, XYZ b)
    {
        var x = (a.X + b.X) / 2;
        var y = (a.Y + b.Y) / 2;
        var z = (a.Z + b.Z) / 2;
        return Place(topo, new[] { (x, y, z) });
    }

    public static void SlopeBetween(Toposolid topo, XYZ a, XYZ b, double endZ)
    {
        var run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        if (run < PointGap)
            return;
        var spacing = Math.Max(run / 24, 0.5);
        var count = Math.Max(2, (int)Math.Ceiling(run / spacing));
        var spots = new List<(double X, double Y, double Z)>();
        for (var i = 0; i <= count; i++)
        {
            var t = i / (double)count;
            spots.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + t * (endZ - a.Z)));
        }

        var editor = Open(topo);
        var basis = BaseElevation(topo);
        foreach (var vertex in Vertices(editor))
        {
            if (!ProjectsOnto(a, b, vertex.Position, out var t, out var distance) || distance > spacing * 0.75)
                continue;
            editor.ModifySubElement(vertex, a.Z + t * (endZ - a.Z) - basis);
        }

        Place(topo, spots);
    }

    public static int SplitLine(Toposolid topo, XYZ a, XYZ b)
    {
        Place(topo, new[] { (a.X, a.Y, a.Z), (b.X, b.Y, b.Z) });
        var editor = Open(topo);
        SlabShapeVertex? start = null;
        SlabShapeVertex? end = null;
        var startDistance = double.MaxValue;
        var endDistance = double.MaxValue;
        foreach (var vertex in Vertices(editor))
        {
            var fromStart = FlatDistance(vertex.Position, a.X, a.Y);
            var fromEnd = FlatDistance(vertex.Position, b.X, b.Y);
            if (fromStart < startDistance)
            {
                startDistance = fromStart;
                start = vertex;
            }

            if (fromEnd < endDistance)
            {
                endDistance = fromEnd;
                end = vertex;
            }
        }

        if (start is null || end is null || startDistance > 2 || endDistance > 2)
            return 0;
        editor.AddSplitLine(start, end);
        return 1;
    }

    public static int NudgeNearest(Toposolid topo, double x, double y, double delta, double reach)
    {
        var editor = Open(topo);
        var basis = BaseElevation(topo);
        SlabShapeVertex? nearest = null;
        var best = reach;
        foreach (var vertex in Vertices(editor))
        {
            var distance = FlatDistance(vertex.Position, x, y);
            if (distance < best)
            {
                best = distance;
                nearest = vertex;
            }
        }

        if (nearest is null)
            return PlaceRelative(topo, x, y, delta);
        editor.ModifySubElement(nearest, nearest.Position.Z + delta - basis);
        return 1;
    }

    public static Toposolid CreatePad(Document doc, double x1, double y1, double x2, double y2, double z, ElementId typeId, ElementId levelId)
    {
        var points = new List<XYZ>
        {
            new(x1, y1, z),
            new(x2, y1, z),
            new(x2, y2, z),
            new(x1, y2, z),
        };
        return CreateSurface(doc, points, typeId, levelId, "Create topography pad");
    }

    public static Toposolid CreateFromElements(Document doc, IEnumerable<Element> elements, bool edges, ElementId typeId, ElementId levelId)
    {
        var points = new List<XYZ>();
        foreach (var element in elements)
        {
            foreach (var curve in edges ? TopEdges(element) : ModelCurves(element))
                points.AddRange(Sample(curve));
        }

        points = Dedupe(points);
        if (points.Count < 3)
            throw new InvalidOperationException(edges
                ? "Need at least 3 points. Pick walls, floors, roofs, or model lines."
                : "Need at least 3 points. Pick model lines, not detail lines.");
        return CreateSurface(doc, points, typeId, levelId, edges ? "Topography from edges" : "Topography from lines");
    }

    public static int StampCurves(Toposolid topo, IEnumerable<Element> elements, bool edges)
    {
        var spots = new List<(double X, double Y, double Z)>();
        foreach (var element in elements)
        {
            foreach (var curve in edges ? TopEdges(element) : ModelCurves(element))
            {
                foreach (var point in Sample(curve))
                    spots.Add((point.X, point.Y, point.Z));
            }
        }

        return Place(topo, spots);
    }

    public static List<(double X, double Y, double Z)> Capture(Toposolid topo)
    {
        var editor = topo.GetSlabShapeEditor();
        if (!editor.IsEnabled)
            return new List<(double X, double Y, double Z)>();
        var points = new List<(double X, double Y, double Z)>();
        foreach (var vertex in Vertices(editor))
            points.Add((vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
        return points;
    }

    public static void Restore(Toposolid topo, IReadOnlyList<(double X, double Y, double Z)> snap)
    {
        var editor = topo.GetSlabShapeEditor();
        if (snap.Count == 0)
        {
            if (editor.IsEnabled)
                editor.ResetSlabShape();
            return;
        }

        if (!editor.IsEnabled)
            editor.Enable();
        var current = Vertices(editor);
        var basis = BaseElevation(topo);
        var matched = new bool[current.Count];
        foreach (var point in snap)
        {
            var found = -1;
            var best = PointGap;
            for (var i = 0; i < current.Count; i++)
            {
                if (matched[i])
                    continue;
                var distance = FlatDistance(current[i].Position, point.X, point.Y);
                if (distance <= best)
                {
                    best = distance;
                    found = i;
                }
            }

            if (found >= 0)
            {
                matched[found] = true;
                editor.ModifySubElement(current[found], point.Z - basis);
                continue;
            }

            try
            {
                var added = editor.AddPoint(new XYZ(point.X, point.Y, point.Z));
                editor.ModifySubElement(added, point.Z - basis);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }
        }

        for (var i = current.Count - 1; i >= 0; i--)
        {
            if (matched[i] || current[i].VertexType == SlabShapeVertexType.Corner)
                continue;
            try
            {
                editor.DeletePoint(current[i]);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
            }
        }

        topo.Document.Regenerate();
    }

    public static void Prepare(Toposolid topo)
    {
        var editor = topo.GetSlabShapeEditor();
        if (!editor.IsEnabled)
            editor.Enable();
    }

    public static Toposolid PickSurface(UIDocument uidoc) =>
        PickTopo(uidoc, "Pick the topography to edit.");

    public static IList<Reference> PickLines(UIDocument uidoc) =>
        uidoc.Selection.PickObjects(ObjectType.Element, new ModelCurveFilter(),
            "Select model lines. Click Finish.");

    public static IList<Reference> PickEdgeSources(UIDocument uidoc) =>
        uidoc.Selection.PickObjects(ObjectType.Element, new EdgeSourceFilter(),
            "Select walls, floors, roofs, or model lines. Click Finish.");

    private static Toposolid CreateSurface(Document doc, IList<XYZ> points, ElementId typeId, ElementId levelId, string name)
    {
        Toposolid? created = null;
        InTransaction(doc, name, () =>
        {
            var unique = Dedupe(points);
            try
            {
                created = Toposolid.Create(doc, unique, typeId, levelId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                var level = (Level)doc.GetElement(levelId);
                var loop = BoxLoop(unique, 2, level.Elevation);
                created = Toposolid.Create(doc, new List<CurveLoop> { loop }, unique, typeId, levelId);
            }

            return 0;
        });
        return created ?? throw new InvalidOperationException("Revit could not create the surface.");
    }

    internal static int Place(Toposolid topo, IEnumerable<(double X, double Y, double Z)> spots, TopoProbe? probe = null)
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

            var inside = probe is not null
                ? probe.TryVertical(spot.X, spot.Y, out _)
                : TryElevationAt(topo, spot.X, spot.Y, out _);
            if (!inside)
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

    internal static SlabShapeEditor Open(Toposolid topo)
    {
        var editor = topo.GetSlabShapeEditor();
        if (!editor.IsEnabled)
            editor.Enable();
        return editor;
    }

    internal static List<SlabShapeVertex> Vertices(SlabShapeEditor editor)
    {
        var list = new List<SlabShapeVertex>();
        foreach (SlabShapeVertex vertex in editor.SlabShapeVertices)
            list.Add(vertex);
        return list;
    }

    internal static double BaseElevation(Toposolid topo)
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
