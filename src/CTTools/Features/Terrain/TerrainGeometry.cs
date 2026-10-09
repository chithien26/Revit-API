using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Features.Topo;

namespace CTTools.Features.Terrain;

/// <summary>
/// Point cleanup, CAD/CSV reading, closed boundaries, and the surface math shared by the terrain commands.
/// Lengths that come from Revit geometry are already in feet. Only file coordinates are converted.
/// </summary>
internal static class TerrainGeometry
{
    private const double JoinGap = 0.2; // feet, about 60 mm

    public static List<TerrainChoice> Types(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>()
            .OrderBy(type => type.Name)
            .Select(type => new TerrainChoice { Id = type.Id, Name = type.Name })
            .ToList();

    public static List<TerrainChoice> Levels(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(level => level.Elevation)
            .Select(level => new TerrainChoice { Id = level.Id, Name = level.Name })
            .ToList();

    public static List<TerrainChoice> Imports(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
            .Select(import => new TerrainChoice
            {
                Id = import.Id,
                Name = string.IsNullOrWhiteSpace(import.Category?.Name) ? $"CAD {import.Id.Value}" : import.Category!.Name,
            })
            .OrderBy(item => item.Name)
            .ToList();

    public static List<TerrainChoice> Surfaces(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Toposolid)).Cast<Toposolid>()
            .Select(topo =>
            {
                var typeName = (doc.GetElement(topo.GetTypeId()) as ElementType)?.Name ?? "Toposolid";
                var mark = topo.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                var name = string.IsNullOrWhiteSpace(mark) ? typeName : $"{typeName} {mark}";
                return new TerrainChoice { Id = topo.Id, Name = $"{name} [{topo.Id.Value}]" };
            })
            .OrderBy(item => item.Name)
            .ToList();

    public static Toposolid RequireSurface(Document doc, ElementId id, string label)
    {
        if (doc.GetElement(id) is not Toposolid topo)
            throw new InvalidOperationException($"Choose a {label}.");
        return topo;
    }

    public static void RequireTypeAndLevel(Document doc, ElementId typeId, ElementId levelId)
    {
        if (doc.GetElement(typeId) is not ToposolidType)
            throw new InvalidOperationException("Choose a toposolid type.");
        if (doc.GetElement(levelId) is not Level)
            throw new InvalidOperationException("Choose a level.");
    }

    /// <summary>Bare numbers are millimeters. A unit suffix such as "150 mm" uses project units.</summary>
    public static double ParseMillimeterEntry(Document doc, string text, string label)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            throw new InvalidOperationException($"{label} is empty.");
        if (trimmed.Any(char.IsLetter))
        {
            if (!UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, trimmed, out var parsed))
                throw new InvalidOperationException($"{label} is not a length.");
            return parsed;
        }

        if (!TryDouble(trimmed, out var millimeters))
            throw new InvalidOperationException($"{label} is not a number. Example: 150");
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
    }

    public static double ParseProjectLength(Document doc, string text, string label)
    {
        if (!UnitFormatUtils.TryParse(doc.GetUnits(), SpecTypeId.Length, text.Trim(), out var feet))
            throw new InvalidOperationException($"{label} is not a length. Example: -2.5 m");
        return feet;
    }

    /// <summary>"1:1.5" is 1 vertical to 1.5 horizontal. A single number is horizontal per vertical.</summary>
    public static double ParseSlope(string text)
    {
        var parts = text.Trim().Split(new[] { ':', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && TryDouble(parts[0], out var vertical) && TryDouble(parts[1], out var horizontal) && vertical > 0 && horizontal > 0)
            return horizontal / vertical;
        if (parts.Length == 1 && TryDouble(parts[0], out var ratio) && ratio > 0)
            return ratio;
        throw new InvalidOperationException("Slope is not a ratio. Example: 1:1.5");
    }

    public static List<XYZ> FromFile(string path, bool millimeters)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException("Choose a CSV or TXT file.");
        var unit = millimeters ? UnitTypeId.Millimeters : UnitTypeId.Meters;
        var points = new List<XYZ>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//"))
                continue;
            var parts = SplitRow(line);
            if (parts.Length < 3)
                continue;
            if (!TryDouble(parts[0], out var x) || !TryDouble(parts[1], out var y) || !TryDouble(parts[2], out var z))
                continue;
            points.Add(new XYZ(
                UnitUtils.ConvertToInternalUnits(x, unit),
                UnitUtils.ConvertToInternalUnits(y, unit),
                UnitUtils.ConvertToInternalUnits(z, unit)));
        }

        return Dedupe(points);
    }

    /// <summary>Layer names match the CAD subcategory. Geometry is already in project feet after the import transform.</summary>
    public static List<XYZ> FromImport(Document doc, ElementId importId, string layerText)
    {
        if (doc.GetElement(importId) is not ImportInstance)
            throw new InvalidOperationException("Choose a CAD import.");
        var layers = layerText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (layers.Count == 0)
            throw new InvalidOperationException("Enter at least one layer name. Example: CONTOUR, POINTS");

        var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = true };
        var points = new List<XYZ>();
        var geometry = doc.GetElement(importId).get_Geometry(options);
        if (geometry is not null)
            Collect(doc, geometry, Transform.Identity, layers, points, 0);
        return Dedupe(points);
    }

    public static Toposolid CreateSurface(Document doc, IList<XYZ> points, ElementId typeId, ElementId levelId, string name)
    {
        var unique = Dedupe(points);
        if (unique.Count < 3)
            throw new InvalidOperationException("Need at least 3 points that are not on top of each other.");
        if (unique.Count > 20000)
            throw new InvalidOperationException($"There are {unique.Count} points. Use fewer layers or a coarser file (limit 20000).");

        Toposolid? created = null;
        Transact(doc, name, () =>
        {
            try
            {
                created = Toposolid.Create(doc, unique, typeId, levelId);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                var level = (Level)doc.GetElement(levelId);
                created = Toposolid.Create(doc, new List<CurveLoop> { Box(unique, level.Elevation) }, unique, typeId, levelId);
            }
        });
        return created ?? throw new InvalidOperationException("Revit could not create the surface.");
    }

    public static List<CurveLoop> Boundaries(Document doc, IEnumerable<Reference> references)
    {
        var curves = new List<Curve>();
        foreach (var reference in references)
        {
            if (doc.GetElement(reference) is CurveElement element && element.GeometryCurve is { IsBound: true } curve && curve.Length > 1e-4)
                curves.Add(curve);
        }

        if (curves.Count == 0)
            throw new InvalidOperationException("Pick model lines or detail lines that form a closed boundary.");
        return Chain(curves);
    }

    public static int Grade(Toposolid topo, IList<CurveLoop> loops, double bottom, double horizontalPerVertical)
    {
        var probe = TopoProbe.Build(topo) ?? throw new InvalidOperationException("The surface has no solid to measure.");
        var placed = 0;
        Transact(topo.Document, "Excavate toposolid", () =>
        {
            foreach (var loop in loops)
                placed += GradeOne(topo, probe, loop, bottom, horizontalPerVertical);
        });
        return placed;
    }

    public static CutFillNumbers Measure(Toposolid existing, Toposolid proposed)
    {
        var existingProbe = TopoProbe.Build(existing) ?? throw new InvalidOperationException("The existing surface has no solid.");
        var proposedProbe = TopoProbe.Build(proposed) ?? throw new InvalidOperationException("The proposed surface has no solid.");
        var minX = Math.Max(existingProbe.MinX, proposedProbe.MinX);
        var maxX = Math.Min(existingProbe.MaxX, proposedProbe.MaxX);
        var minY = Math.Max(existingProbe.MinY, proposedProbe.MinY);
        var maxY = Math.Min(existingProbe.MaxY, proposedProbe.MaxY);
        if (maxX - minX < 0.2 || maxY - minY < 0.2)
            throw new InvalidOperationException("The two surfaces do not overlap in plan.");

        const int cells = 56;
        var dx = (maxX - minX) / cells;
        var dy = (maxY - minY) / cells;
        var area = dx * dy;
        double cut = 0;
        double fill = 0;
        var samples = 0;
        for (var i = 0; i < cells; i++)
        {
            var x = minX + (i + 0.5) * dx;
            for (var j = 0; j < cells; j++)
            {
                var y = minY + (j + 0.5) * dy;
                if (!existingProbe.TryVertical(x, y, out var zExisting) || !proposedProbe.TryVertical(x, y, out var zProposed))
                    continue;
                var delta = zExisting - zProposed;
                if (delta > 0)
                    cut += delta * area;
                else
                    fill += -delta * area;
                samples++;
            }
        }

        if (samples == 0)
            throw new InvalidOperationException("No grid point hit both surfaces.");
        return new CutFillNumbers
        {
            ExistingSolidM3 = CubicMeters(SolidVolume(existing)),
            ProposedSolidM3 = CubicMeters(SolidVolume(proposed)),
            CutM3 = CubicMeters(cut),
            FillM3 = CubicMeters(fill),
            Samples = samples,
            CellXMeters = UnitUtils.ConvertFromInternalUnits(dx, UnitTypeId.Meters),
            CellYMeters = UnitUtils.ConvertFromInternalUnits(dy, UnitTypeId.Meters),
        };
    }

    public static void WriteReport(string path, CutFillNumbers numbers)
    {
        var culture = CultureInfo.CurrentCulture;
        var separator = culture.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        var rows = new[]
        {
            Row(separator, "Item", "Cubic meters"),
            Row(separator, "Existing solid volume", numbers.ExistingSolidM3, culture),
            Row(separator, "Proposed solid volume", numbers.ProposedSolidM3, culture),
            Row(separator, "Cut", numbers.CutM3, culture),
            Row(separator, "Fill", numbers.FillM3, culture),
            Row(separator, "Net cut minus fill", numbers.CutM3 - numbers.FillM3, culture),
            Row(separator, "Grid samples", numbers.Samples.ToString(culture)),
            Row(separator, "Cell size m", $"{numbers.CellXMeters.ToString("0.###", culture)} x {numbers.CellYMeters.ToString("0.###", culture)}"),
        };
        File.WriteAllLines(path, rows, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static View3D? Find3D(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(view => !view.IsTemplate);

    public static void Transact(Document doc, string name, Action work)
    {
        using var transaction = new Transaction(doc, name);
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new WarningSilencer());
        transaction.SetFailureHandlingOptions(options);
        transaction.Start();
        try
        {
            work();
            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
                transaction.RollBack();
            throw;
        }
    }

    public static string Clean(Exception ex)
    {
        if (ex is Autodesk.Revit.Exceptions.ArgumentException argument && string.IsNullOrWhiteSpace(argument.Message))
            return "Revit rejected the points or the boundary.";
        return string.IsNullOrWhiteSpace(ex.Message) ? "The edit failed." : ex.Message;
    }

    private static int GradeOne(Toposolid topo, TopoProbe probe, CurveLoop loop, double bottom, double runPerRise)
    {
        var polygon = Polygon(loop);
        if (polygon.Count < 3)
            return 0;
        var edgeZ = new List<double>();
        foreach (var point in polygon)
        {
            if (probe.TryVertical(point.X, point.Y, out var z))
                edgeZ.Add(z);
        }

        if (edgeZ.Count == 0)
            return 0;
        var cutting = bottom <= edgeZ.Average();
        var drop = edgeZ.Max(z => Math.Abs(z - bottom));
        var reach = Math.Max(drop * runPerRise + 2, 2);
        var minX = polygon.Min(p => p.X) - reach;
        var maxX = polygon.Max(p => p.X) + reach;
        var minY = polygon.Min(p => p.Y) - reach;
        var maxY = polygon.Max(p => p.Y) + reach;
        var span = Math.Max(maxX - minX, maxY - minY);
        var step = Math.Max(span / 48.0, 0.5);
        var spots = new List<(double X, double Y, double Z)>();
        for (var x = minX; x <= maxX; x += step)
        {
            for (var y = minY; y <= maxY; y += step)
            {
                if (TryTarget(probe, polygon, x, y, bottom, runPerRise, cutting, step, out var z))
                    spots.Add((x, y, z));
            }
        }

        foreach (var point in polygon)
            spots.Add((point.X, point.Y, bottom));

        var editor = TopoEditor.Open(topo);
        var basis = TopoEditor.BaseElevation(topo);
        var moved = 0;
        foreach (var vertex in TopoEditor.Vertices(editor))
        {
            if (!TryTarget(probe, polygon, vertex.Position.X, vertex.Position.Y, bottom, runPerRise, cutting, step, out var z))
                continue;
            editor.ModifySubElement(vertex, z - basis);
            moved++;
        }

        return moved + TopoEditor.Place(topo, spots, probe);
    }

    /// <summary>
    /// Inside the boundary the ground goes to the pit bottom.
    /// Outside, elevation climbs (or falls) at the slope until it meets the original surface. That meeting line is the daylight.
    /// </summary>
    private static bool TryTarget(TopoProbe probe, List<(double X, double Y)> polygon, double x, double y, double bottom, double runPerRise, bool cutting, double step, out double z)
    {
        z = 0;
        if (!probe.TryVertical(x, y, out var surface))
            return false;
        if (Contains(polygon, x, y))
        {
            z = bottom;
            return true;
        }

        var distance = Distance(polygon, x, y);
        var sloped = cutting ? bottom + distance / runPerRise : bottom - distance / runPerRise;
        if (cutting)
        {
            if (sloped >= surface - 0.02)
            {
                var daylight = (surface - bottom) * runPerRise;
                if (Math.Abs(distance - daylight) > step * 1.25)
                    return false;
                z = surface;
                return true;
            }

            z = sloped;
            return true;
        }

        if (sloped <= surface + 0.02)
        {
            var daylight = (bottom - surface) * runPerRise;
            if (Math.Abs(distance - daylight) > step * 1.25)
                return false;
            z = surface;
            return true;
        }

        z = sloped;
        return true;
    }

    private static void Collect(Document doc, GeometryElement geometry, Transform transform, HashSet<string> layers, List<XYZ> points, int depth)
    {
        if (depth > 6)
            return;
        foreach (var item in geometry)
        {
            if (item is GeometryInstance instance)
            {
                var nested = instance.GetSymbolGeometry();
                if (nested is not null)
                    Collect(doc, nested, transform.Multiply(instance.Transform), layers, points, depth + 1);
                continue;
            }

            if (!OnLayer(doc, item, layers))
                continue;
            switch (item)
            {
                case PolyLine poly:
                    foreach (var point in poly.GetCoordinates())
                        points.Add(transform.OfPoint(point));
                    break;
                case Point point:
                    points.Add(transform.OfPoint(point.Coord));
                    break;
                case Curve curve:
                    foreach (var point in curve.Tessellate())
                        points.Add(transform.OfPoint(point));
                    break;
            }
        }
    }

    private static bool OnLayer(Document doc, GeometryObject item, HashSet<string> layers)
    {
        if (doc.GetElement(item.GraphicsStyleId) is not GraphicsStyle style)
            return false;
        var name = style.GraphicsStyleCategory?.Name;
        return name is not null && layers.Contains(name);
    }

    private static List<CurveLoop> Chain(List<Curve> source)
    {
        var pool = source.Select(curve => curve.Clone()).ToList();
        var loops = new List<CurveLoop>();
        while (pool.Count > 0)
        {
            var chain = new List<Curve> { pool[0] };
            pool.RemoveAt(0);
            var guard = 0;
            while (!Closed(chain) && guard++ < source.Count + 2)
            {
                if (Attach(chain, pool, prepend: false))
                    continue;
                if (Attach(chain, pool, prepend: true))
                    continue;
                break;
            }

            if (!Closed(chain))
            {
                var gap = chain[^1].GetEndPoint(1).DistanceTo(chain[0].GetEndPoint(0));
                throw new InvalidOperationException(
                    $"The boundary does not close. The open gap is about {gap * 304.8:0} mm. Make the line ends meet.");
            }

            SnapClosed(chain);
            var loop = CurveLoop.Create(chain);
            if (loop.IsOpen())
                throw new InvalidOperationException("The boundary does not close.");
            loops.Add(Flatten(loop));
        }

        return loops;
    }

    private static bool Attach(List<Curve> chain, List<Curve> pool, bool prepend)
    {
        var anchor = prepend ? chain[0].GetEndPoint(0) : chain[^1].GetEndPoint(1);
        for (var i = 0; i < pool.Count; i++)
        {
            var curve = pool[i];
            var atStart = curve.GetEndPoint(0).DistanceTo(anchor) <= JoinGap;
            var atEnd = curve.GetEndPoint(1).DistanceTo(anchor) <= JoinGap;
            if (!atStart && !atEnd)
                continue;
            pool.RemoveAt(i);
            if (prepend)
            {
                if (!atEnd)
                    curve = curve.CreateReversed();
                chain.Insert(0, curve);
            }
            else
            {
                if (!atStart)
                    curve = curve.CreateReversed();
                chain.Add(curve);
            }

            return true;
        }

        return false;
    }

    private static bool Closed(List<Curve> chain) =>
        chain.Count >= 1 && chain[^1].GetEndPoint(1).DistanceTo(chain[0].GetEndPoint(0)) <= JoinGap &&
        (chain.Count > 1 || chain[0].Length > JoinGap);

    private static void SnapClosed(List<Curve> chain)
    {
        var start = chain[0].GetEndPoint(0);
        var end = chain[^1].GetEndPoint(1);
        if (end.DistanceTo(start) <= 1e-6)
            return;
        var lastStart = chain[^1].GetEndPoint(0);
        if (lastStart.DistanceTo(start) < 1e-4)
            return;
        chain[^1] = Line.CreateBound(lastStart, start);
    }

    private static CurveLoop Flatten(CurveLoop loop)
    {
        var z = loop.First().GetEndPoint(0).Z;
        var flat = new CurveLoop();
        foreach (var curve in loop)
        {
            var points = curve.Tessellate();
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = new XYZ(points[i].X, points[i].Y, z);
                var b = new XYZ(points[i + 1].X, points[i + 1].Y, z);
                if (a.DistanceTo(b) < 1e-4)
                    continue;
                flat.Append(Line.CreateBound(a, b));
            }
        }

        if (flat.IsOpen())
            throw new InvalidOperationException("The boundary is too small after it was laid flat.");
        if (!flat.IsCounterclockwise(XYZ.BasisZ))
            flat.Flip();
        return flat;
    }

    private static List<(double X, double Y)> Polygon(CurveLoop loop)
    {
        var polygon = new List<(double X, double Y)>();
        foreach (var curve in loop)
        {
            foreach (var point in curve.Tessellate())
            {
                if (polygon.Count > 0 && Math.Abs(polygon[^1].X - point.X) < 1e-6 && Math.Abs(polygon[^1].Y - point.Y) < 1e-6)
                    continue;
                polygon.Add((point.X, point.Y));
            }
        }

        if (polygon.Count > 1 && Math.Abs(polygon[0].X - polygon[^1].X) < 1e-6 && Math.Abs(polygon[0].Y - polygon[^1].Y) < 1e-6)
            polygon.RemoveAt(polygon.Count - 1);
        return polygon;
    }

    private static bool Contains(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
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

    private static double Distance(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        var best = double.MaxValue;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            best = Math.Min(best, Segment(polygon[j], polygon[i], x, y));
        return best;
    }

    private static double Segment((double X, double Y) a, (double X, double Y) b, double x, double y)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var length = abx * abx + aby * aby;
        var t = length < 1e-12 ? 0 : Math.Clamp(((x - a.X) * abx + (y - a.Y) * aby) / length, 0, 1);
        var dx = x - (a.X + abx * t);
        var dy = y - (a.Y + aby * t);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static List<XYZ> Dedupe(IEnumerable<XYZ> points)
    {
        const double gap = 0.15;
        var kept = new List<XYZ>();
        var grid = new Dictionary<(int X, int Y), List<int>>();
        foreach (var point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
                continue;
            var key = ((int)Math.Floor(point.X / gap), (int)Math.Floor(point.Y / gap));
            var duplicate = false;
            for (var dx = -1; dx <= 1 && !duplicate; dx++)
            {
                for (var dy = -1; dy <= 1 && !duplicate; dy++)
                {
                    if (!grid.TryGetValue((key.Item1 + dx, key.Item2 + dy), out var bucket))
                        continue;
                    foreach (var index in bucket)
                    {
                        var other = kept[index];
                        var ox = other.X - point.X;
                        var oy = other.Y - point.Y;
                        if (ox * ox + oy * oy < gap * gap)
                        {
                            duplicate = true;
                            break;
                        }
                    }
                }
            }

            if (duplicate)
                continue;
            if (!grid.TryGetValue(key, out var list))
                grid[key] = list = new List<int>();
            list.Add(kept.Count);
            kept.Add(point);
        }

        return kept;
    }

    private static CurveLoop Box(IList<XYZ> points, double z)
    {
        var minX = points.Min(p => p.X) - 2;
        var maxX = points.Max(p => p.X) + 2;
        var minY = points.Min(p => p.Y) - 2;
        var maxY = points.Max(p => p.Y) + 2;
        var loop = new CurveLoop();
        loop.Append(Line.CreateBound(new XYZ(minX, minY, z), new XYZ(maxX, minY, z)));
        loop.Append(Line.CreateBound(new XYZ(maxX, minY, z), new XYZ(maxX, maxY, z)));
        loop.Append(Line.CreateBound(new XYZ(maxX, maxY, z), new XYZ(minX, maxY, z)));
        loop.Append(Line.CreateBound(new XYZ(minX, maxY, z), new XYZ(minX, minY, z)));
        return loop;
    }

    private static double SolidVolume(Toposolid topo)
    {
        var parameter = topo.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (parameter is { StorageType: StorageType.Double, HasValue: true } && parameter.AsDouble() > 1e-6)
            return parameter.AsDouble();
        double volume = 0;
        var options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
        var geometry = topo.get_Geometry(options);
        if (geometry is null)
            return 0;
        foreach (var item in geometry)
        {
            if (item is Solid solid && solid.Volume > 0)
                volume += solid.Volume;
        }

        return volume;
    }

    private static double CubicMeters(double cubicFeet) =>
        UnitUtils.ConvertFromInternalUnits(cubicFeet, UnitTypeId.CubicMeters);

    private static string[] SplitRow(string line)
    {
        if (line.Contains(';'))
            return line.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (line.Contains(','))
            return line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static string Row(string separator, string name, double value, CultureInfo culture) =>
        $"{name}{separator}{value.ToString("0.###", culture)}";

    private static string Row(string separator, string name, string value) => $"{name}{separator}{value}";

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
}

public sealed class CutFillNumbers
{
    public double ExistingSolidM3 { get; init; }
    public double ProposedSolidM3 { get; init; }
    public double CutM3 { get; init; }
    public double FillM3 { get; init; }
    public int Samples { get; init; }
    public double CellXMeters { get; init; }
    public double CellYMeters { get; init; }
}
