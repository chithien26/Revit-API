using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Analysis;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI.Selection;
using CTTools.Features.Terrain;
using CTTools.Features.Topo;

namespace CTTools.Features.Site;

internal static class SiteWork
{
    private const int PlaceLimit = 2000;

    public static int ShapeWalls(Document doc, Toposolid ground, IList<Wall> walls)
    {
        var probe = TopoProbe.Build(ground) ?? throw new InvalidOperationException("The ground has no solid.");
        var shaped = 0;
        TerrainGeometry.Transact(doc, "Shape walls by topography", () =>
        {
            foreach (var wall in walls)
            {
                if (TryShape(doc, wall, probe))
                    shaped++;
            }
        });
        return shaped;
    }

    public static int RailingsOnWalls(Document doc, IList<Wall> walls, ElementId railingTypeId)
    {
        var placed = 0;
        TerrainGeometry.Transact(doc, "Railing on walls", () =>
        {
            foreach (var wall in walls)
            {
                if (wall.Location is not LocationCurve location || location.Curve is not Line line)
                    continue;
                var z = TopOf(doc, wall);
                var start = new XYZ(line.GetEndPoint(0).X, line.GetEndPoint(0).Y, z);
                var end = new XYZ(line.GetEndPoint(1).X, line.GetEndPoint(1).Y, z);
                if (start.DistanceTo(end) < 0.2)
                    continue;
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(start, end));
                Railing.Create(doc, loop, railingTypeId, wall.LevelId);
                placed++;
            }
        });
        return placed;
    }

    public static int SetElevation(Document doc, IList<ModelCurve> curves, double elevation)
    {
        var moved = 0;
        TerrainGeometry.Transact(doc, "Set line elevation", () =>
        {
            foreach (var curve in curves)
            {
                curve.GeometryCurve = AtHeight(curve.GeometryCurve, elevation);
                moved++;
            }
        });
        return moved;
    }

    public static void MoveCurve(Document doc, ModelCurve curve, XYZ targetStart)
    {
        var start = curve.GeometryCurve.GetEndPoint(0);
        TerrainGeometry.Transact(doc, "Set line coordinate", () =>
            ElementTransformUtils.MoveElement(doc, curve.Id, targetStart - start));
    }

    public static int PlaceOnClicks(Document doc, FamilySymbol symbol, Toposolid ground, IList<XYZ> clicks)
    {
        var probe = TopoProbe.Build(ground) ?? throw new InvalidOperationException("The ground has no solid.");
        var placed = 0;
        TerrainGeometry.Transact(doc, "Plant on topography", () =>
        {
            Activate(symbol);
            foreach (var click in clicks)
            {
                if (!probe.TryVertical(click.X, click.Y, out var z))
                    continue;
                doc.Create.NewFamilyInstance(new XYZ(click.X, click.Y, z), symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                placed++;
            }
        });
        return placed;
    }

    public static int Scatter(Document doc, FamilySymbol symbol, Toposolid ground, IList<CurveLoop> loops, double spacing)
    {
        var probe = TopoProbe.Build(ground) ?? throw new InvalidOperationException("The ground has no solid.");
        var spots = new List<XYZ>();
        foreach (var loop in loops)
        {
            var polygon = Corners(loop);
            if (polygon.Count < 3)
                continue;
            var minX = polygon.Min(p => p.X);
            var maxX = polygon.Max(p => p.X);
            var minY = polygon.Min(p => p.Y);
            var maxY = polygon.Max(p => p.Y);
            for (var x = minX; x <= maxX && spots.Count < PlaceLimit; x += spacing)
            {
                for (var y = minY; y <= maxY && spots.Count < PlaceLimit; y += spacing)
                {
                    if (!Inside(polygon, x, y) || !probe.TryVertical(x, y, out var z))
                        continue;
                    spots.Add(new XYZ(x, y, z));
                }
            }
        }

        var placed = 0;
        TerrainGeometry.Transact(doc, "Scatter on topography", () =>
        {
            Activate(symbol);
            foreach (var spot in spots)
            {
                doc.Create.NewFamilyInstance(spot, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                placed++;
            }
        });
        return placed;
    }

    public static int BlocksFromCad(Document doc, ImportInstance import, string layers, FamilySymbol symbol, Toposolid? ground)
    {
        var points = TerrainGeometry.FromImport(doc, import.Id, layers);
        if (points.Count == 0)
            throw new InvalidOperationException("No points on those layers.");
        if (points.Count > PlaceLimit)
            throw new InvalidOperationException($"Those layers have {points.Count} points. Use a shorter layer list (limit {PlaceLimit}).");
        var probe = ground is null ? null : TopoProbe.Build(ground);
        var placed = 0;
        TerrainGeometry.Transact(doc, "Place blocks on CAD points", () =>
        {
            Activate(symbol);
            foreach (var point in points)
            {
                var z = point.Z;
                if (probe is not null && probe.TryVertical(point.X, point.Y, out var surface))
                    z = surface;
                doc.Create.NewFamilyInstance(new XYZ(point.X, point.Y, z), symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                placed++;
            }
        });
        return placed;
    }

    public static Toposolid Alignment(Document doc, Toposolid host, ElementId typeId, IList<Curve> path, double width)
    {
        if (path.Count == 0)
            throw new InvalidOperationException("Pick the centerline.");
        var half = width / 2;
        if (half < 0.2)
            throw new InvalidOperationException("The road is too narrow.");
        var samples = Samples(path);
        if (samples.Count < 2)
            throw new InvalidOperationException("The centerline is too short.");
        var left = new List<XYZ>();
        var right = new List<XYZ>();
        for (var i = 0; i < samples.Count; i++)
        {
            var normal = Normal(samples, i);
            left.Add(samples[i] + normal * half);
            right.Add(samples[i] - normal * half);
        }

        var loop = new CurveLoop();
        Append(loop, left);
        loop.Append(Line.CreateBound(left[^1], right[^1]));
        right.Reverse();
        Append(loop, right);
        loop.Append(Line.CreateBound(right[^1], left[0]));
        Toposolid? created = null;
        TerrainGeometry.Transact(doc, "Create alignment", () =>
        {
            try
            {
                created = host.CreateSubDivision(doc, new List<CurveLoop> { loop });
                if (created.GetTypeId() != typeId)
                    created.ChangeTypeId(typeId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                created = Drape(doc, host, loop, typeId);
            }
        });
        return created ?? throw new InvalidOperationException("Revit could not build the road.");
    }

    public static (double Min, double Max, int Count) ElevationMap(Document doc, View view, Toposolid ground)
    {
        if (view is not View3D)
            throw new InvalidOperationException("Open a 3D view. The colors are drawn there.");
        var probe = TopoProbe.Build(ground) ?? throw new InvalidOperationException("The ground has no solid.");
        const int cells = 36;
        var dx = Math.Max((probe.MaxX - probe.MinX) / cells, 0.5);
        var dy = Math.Max((probe.MaxY - probe.MinY) / cells, 0.5);
        var points = new List<XYZ>();
        var values = new List<double>();
        for (var x = probe.MinX; x <= probe.MaxX; x += dx)
        {
            for (var y = probe.MinY; y <= probe.MaxY; y += dy)
            {
                if (!probe.TryVertical(x, y, out var z))
                    continue;
                points.Add(new XYZ(x, y, z));
                values.Add(z);
            }
        }

        if (points.Count == 0)
            throw new InvalidOperationException("The surface has no measurable points.");
        TerrainGeometry.Transact(doc, "Elevation analysis", () => Paint(doc, view, points, values));
        return (values.Min(), values.Max(), points.Count);
    }

    public static List<(string Mark, double Length, double Area)> WallRows(IEnumerable<Wall> walls)
    {
        var rows = new List<(string, double, double)>();
        foreach (var wall in walls)
        {
            var mark = wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            if (string.IsNullOrWhiteSpace(mark))
                mark = wall.Id.Value.ToString();
            var length = wall.Location is LocationCurve curve ? curve.Curve.Length : 0;
            var area = wall.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            rows.Add((mark!, length, area));
        }

        return rows;
    }

    public static List<FamilySymbol> Symbols(Document doc, bool plantingOnly)
    {
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(symbol => symbol.Category is not null)
            .ToList();
        var planting = symbols.Where(symbol => symbol.Category.BuiltInCategory == BuiltInCategory.OST_Planting).OrderBy(symbol => symbol.FamilyName).ThenBy(symbol => symbol.Name).ToList();
        if (plantingOnly && planting.Count > 0)
            return planting;
        if (plantingOnly)
            planting = symbols.Where(symbol => symbol.Category.BuiltInCategory == BuiltInCategory.OST_GenericModel).ToList();
        if (plantingOnly)
            return planting.OrderBy(symbol => symbol.FamilyName).ThenBy(symbol => symbol.Name).ToList();
        var allowed = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Planting,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_Site,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_SpecialityEquipment,
        };
        return symbols.Where(symbol => allowed.Contains(symbol.Category.BuiltInCategory))
            .OrderBy(symbol => symbol.FamilyName).ThenBy(symbol => symbol.Name).ToList();
    }

    public static string SymbolLabel(FamilySymbol symbol) => $"{symbol.FamilyName} : {symbol.Name}";

    private static bool TryShape(Document doc, Wall wall, TopoProbe probe)
    {
        if (wall.CurtainGrid is not null)
            return false;
        if (wall.FindInserts(true, false, true, true).Count > 0)
            return false;
        if (wall.Location is not LocationCurve location || location.Curve is not Line line)
            return false;
        if (line.Length < 0.5)
            return false;
        var count = Math.Max(4, (int)Math.Ceiling(line.Length / 3));
        var bottom = new List<XYZ>();
        var top = new List<XYZ>();
        var peak = 0.0;
        for (var i = 0; i <= count; i++)
        {
            var point = line.Evaluate(i / (double)count, true);
            var high = TopOf(doc, wall);
            peak = Math.Max(peak, high);
            var low = probe.TryVertical(point.X, point.Y, out var z) ? z : high - 8;
            if (high - low < 0.4)
                low = high - 0.4;
            bottom.Add(new XYZ(point.X, point.Y, low));
            top.Add(new XYZ(point.X, point.Y, high));
        }

        var profile = new List<Curve>();
        Chain(profile, bottom);
        profile.Add(Line.CreateBound(bottom[^1], top[^1]));
        top.Reverse();
        Chain(profile, top);
        profile.Add(Line.CreateBound(top[^1], bottom[0]));
        var created = Wall.Create(doc, profile, wall.WallType.Id, wall.LevelId, false, wall.Orientation);
        var mark = wall.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
        if (!string.IsNullOrWhiteSpace(mark))
            created.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.Set(mark);
        doc.Delete(wall.Id);
        return created is not null;
    }

    private static double TopOf(Document doc, Wall wall)
    {
        var constraint = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId;
        if (constraint != ElementId.InvalidElementId && doc.GetElement(constraint) is Level top)
        {
            var offset = wall.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.AsDouble() ?? 0;
            return top.Elevation + offset;
        }

        var level = doc.GetElement(wall.LevelId) as Level;
        var baseOffset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        var height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 10;
        return (level?.Elevation ?? 0) + baseOffset + height;
    }

    private static Curve AtHeight(Curve curve, double z)
    {
        var a = curve.GetEndPoint(0);
        var b = curve.GetEndPoint(1);
        if (curve is Line)
            return Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z));
        if (curve is Arc)
        {
            var mid = curve.Evaluate(0.5, true);
            return Arc.Create(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z), new XYZ(mid.X, mid.Y, z));
        }

        throw new InvalidOperationException("Only straight lines and arcs can take a new elevation.");
    }

    private static void Paint(Document doc, View view, List<XYZ> points, List<double> values)
    {
        var manager = SpatialFieldManager.GetSpatialFieldManager(view) ?? SpatialFieldManager.CreateSpatialFieldManager(view, 1);
        manager.Clear();
        var schema = new AnalysisResultSchema("Elevation", "Ground elevation");
        schema.SetUnits(new List<string> { "m" }, new List<double> { 0.3048 });
        var schemaIndex = manager.RegisterResult(schema);
        var domain = new FieldDomainPointsByXYZ(points);
        var readings = values.Select(value => new ValueAtPoint(new List<double> { value })).ToList();
        var primitive = manager.AddSpatialFieldPrimitive();
        manager.UpdateSpatialFieldPrimitive(primitive, domain, new FieldValues(readings), schemaIndex);
        try
        {
            var style = FindStyle(doc) ?? NewStyle(doc);
            view.AnalysisDisplayStyleId = style.Id;
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // The points are still stored on the view if the legend style cannot be created.
        }
    }

    private static AnalysisDisplayStyle? FindStyle(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(AnalysisDisplayStyle)).Cast<AnalysisDisplayStyle>()
            .FirstOrDefault(style => style.Name == "CTTools Elevation");

    private static AnalysisDisplayStyle NewStyle(Document doc)
    {
        var markers = new AnalysisDisplayMarkersAndTextSettings
        {
            MarkerSize = 0.4,
            TextLabelType = AnalysisDisplayStyleMarkerTextLabelType.ShowAll,
        };
        var colors = new AnalysisDisplayColorSettings
        {
            MinColor = new Color(30, 90, 180),
            MaxColor = new Color(200, 50, 20),
        };
        var legend = new AnalysisDisplayLegendSettings { ShowLegend = true, ShowUnits = true };
        return AnalysisDisplayStyle.CreateAnalysisDisplayStyle(doc, "CTTools Elevation", markers, colors, legend);
    }

    private static Toposolid Drape(Document doc, Toposolid host, CurveLoop loop, ElementId typeId)
    {
        var probe = TopoProbe.Build(host) ?? throw new InvalidOperationException("The ground has no solid.");
        var points = new List<XYZ>();
        foreach (var curve in loop)
        {
            foreach (var point in curve.Tessellate())
            {
                if (probe.TryVertical(point.X, point.Y, out var z))
                    points.Add(new XYZ(point.X, point.Y, z));
            }
        }

        if (points.Count < 3)
            throw new InvalidOperationException("The alignment does not sit on the ground.");
        return Toposolid.Create(doc, new List<CurveLoop> { loop }, points, typeId, host.LevelId);
    }

    private static List<XYZ> Samples(IList<Curve> path)
    {
        var samples = new List<XYZ>();
        foreach (var curve in path)
        {
            var count = Math.Max(2, (int)Math.Ceiling(curve.Length / 4));
            for (var i = 0; i <= count; i++)
            {
                var point = curve.Evaluate(i / (double)count, true);
                var flat = new XYZ(point.X, point.Y, samples.Count == 0 ? point.Z : samples[0].Z);
                if (samples.Count == 0 || samples[^1].DistanceTo(flat) > 0.2)
                    samples.Add(flat);
            }
        }

        return samples;
    }

    private static XYZ Normal(List<XYZ> samples, int index)
    {
        var a = samples[Math.Max(0, index - 1)];
        var b = samples[Math.Min(samples.Count - 1, index + 1)];
        var direction = b - a;
        var flat = new XYZ(direction.X, direction.Y, 0);
        if (flat.GetLength() < 1e-6)
            return XYZ.BasisX;
        flat = flat.Normalize();
        return new XYZ(-flat.Y, flat.X, 0);
    }

    private static void Append(CurveLoop loop, IReadOnlyList<XYZ> points)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (points[i].DistanceTo(points[i + 1]) < 1e-4)
                continue;
            loop.Append(Line.CreateBound(points[i], points[i + 1]));
        }
    }

    private static void Chain(List<Curve> curves, IReadOnlyList<XYZ> points)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            if (points[i].DistanceTo(points[i + 1]) < 1e-3)
                continue;
            curves.Add(Line.CreateBound(points[i], points[i + 1]));
        }
    }

    private static void Activate(FamilySymbol symbol)
    {
        if (!symbol.IsActive)
            symbol.Activate();
    }

    private static List<(double X, double Y)> Corners(CurveLoop loop)
    {
        var polygon = new List<(double X, double Y)>();
        foreach (var curve in loop)
        {
            foreach (var point in curve.Tessellate())
            {
                if (polygon.Count == 0 || Math.Abs(polygon[^1].X - point.X) > 1e-6 || Math.Abs(polygon[^1].Y - point.Y) > 1e-6)
                    polygon.Add((point.X, point.Y));
            }
        }

        if (polygon.Count > 1 && Math.Abs(polygon[0].X - polygon[^1].X) < 1e-6 && Math.Abs(polygon[0].Y - polygon[^1].Y) < 1e-6)
            polygon.RemoveAt(polygon.Count - 1);
        return polygon;
    }

    private static bool Inside(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var yi = polygon[i].Y;
            var yj = polygon[j].Y;
            if ((yi > y) == (yj > y))
                continue;
            var xi = polygon[i].X;
            var xj = polygon[j].X;
            if (x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                inside = !inside;
        }

        return inside;
    }
}

internal sealed class WallFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is Wall;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

internal sealed class ModelLineFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is ModelCurve;
    public bool AllowReference(Reference reference, XYZ position) => false;
}

internal sealed class ImportFilter : ISelectionFilter
{
    public bool AllowElement(Element element) => element is ImportInstance;
    public bool AllowReference(Reference reference, XYZ position) => false;
}
