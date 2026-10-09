using Autodesk.Revit.DB;

namespace CTTools.Features.Topo;

/// <summary>Cached solid of a toposolid so cursor hits do not rebuild geometry on every move.</summary>
public sealed class TopoProbe
{
    private readonly List<Solid> _solids;
    private readonly double _high;
    private readonly double _low;

    private TopoProbe(List<Solid> solids, BoundingBoxXYZ box)
    {
        _solids = solids;
        _high = box.Max.Z + 20;
        _low = box.Min.Z - 20;
        MinX = box.Min.X;
        MaxX = box.Max.X;
        MinY = box.Min.Y;
        MaxY = box.Max.Y;
        MaxZ = box.Max.Z;
    }

    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }
    public double MaxZ { get; }

    public static TopoProbe? Build(Toposolid topo)
    {
        var box = topo.get_BoundingBox(null);
        if (box is null)
            return null;

        var options = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
        };
        var solids = new List<Solid>();
        Collect(topo.get_Geometry(options), solids);
        return solids.Count == 0 ? null : new TopoProbe(solids, box);
    }

    public bool TryVertical(double x, double y, out double z)
    {
        z = 0;
        var line = Line.CreateBound(new XYZ(x, y, _high), new XYZ(x, y, _low));
        double? best = null;
        foreach (var solid in _solids)
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace planar && planar.FaceNormal.Z <= 0.05)
                    continue;
                if (!Highest(face, line, ref best))
                    continue;
            }
        }

        if (best is null)
            return false;
        z = best.Value;
        return true;
    }

    public bool TryRay(XYZ origin, XYZ direction, out XYZ point)
    {
        point = XYZ.Zero;
        var dir = direction.Normalize();
        var line = Line.CreateBound(origin - dir * 30, origin + dir * 8000);
        double? best = null;
        XYZ? hit = null;
        foreach (var solid in _solids)
        {
            foreach (Face face in solid.Faces)
            {
                var compared = face.Intersect(line, out IntersectionResultArray hits);
                if (compared != SetComparisonResult.Overlap || hits is null)
                    continue;
                foreach (IntersectionResult item in hits)
                {
                    var distance = item.XYZPoint.DistanceTo(origin);
                    var forward = (item.XYZPoint - origin).DotProduct(dir);
                    if (forward < -1)
                        continue;
                    if (best is null || distance < best)
                    {
                        best = distance;
                        hit = item.XYZPoint;
                    }
                }
            }
        }

        if (hit is null)
            return false;
        point = hit;
        return true;
    }

    private static bool Highest(Face face, Line line, ref double? best)
    {
        var compared = face.Intersect(line, out IntersectionResultArray hits);
        if (compared != SetComparisonResult.Overlap || hits is null)
            return false;
        foreach (IntersectionResult hit in hits)
        {
            if (best is null || hit.XYZPoint.Z > best)
                best = hit.XYZPoint.Z;
        }

        return true;
    }

    private static void Collect(GeometryElement? geometry, List<Solid> solids)
    {
        if (geometry is null)
            return;
        foreach (var item in geometry)
        {
            if (item is Solid solid && solid.Faces.Size > 0)
                solids.Add(solid);
            else if (item is GeometryInstance instance)
                Collect(instance.GetInstanceGeometry(), solids);
        }
    }
}
