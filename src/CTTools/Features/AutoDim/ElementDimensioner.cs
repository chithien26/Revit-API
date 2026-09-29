using Autodesk.Revit.DB;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Dimensions beams, walls and columns to their nearest parallel grid (grid - face - face), showing offset and width.
/// Beams and walls get one string across their midpoint; columns get one string per direction, just outside the column.
/// Must be called inside an open Transaction.
/// </summary>
public class ElementDimensioner
{
    /// <summary>Paper gap between a column and its dimension string.</summary>
    private const double ColumnGapMm = 5;

    private static readonly BuiltInCategory[] Categories =
    [
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Columns,
    ];

    private static readonly AutoDimResult Done = new(1, 0);
    private static readonly AutoDimResult Skip = new(0, 1);

    private readonly DimensionWriter _writer;
    private readonly View _view;
    private readonly Document _doc;
    private readonly double _tolerance;
    private List<GridLine> _grids = [];

    public ElementDimensioner(DimensionWriter writer)
    {
        _writer = writer;
        _view = writer.View;
        _doc = _view.Document;
        _tolerance = _doc.Application.ShortCurveTolerance;
    }

    public AutoDimResult Run()
    {
        _grids = AutoDimGeometry.GetStraightGrids(_view, out _);
        if (_grids.Count == 0)
            return default;

        var elements = new FilteredElementCollector(_doc, _view.Id)
            .WherePasses(new ElementMulticategoryFilter(Categories))
            .WhereElementIsNotElementType()
            .ToElements();

        var result = default(AutoDimResult);
        foreach (var element in elements)
        {
            result += element.Location switch
            {
                LocationCurve { Curve: Line line } => DimensionLinear(element, line),
                LocationPoint point => DimensionColumn(element, point.Point),
                _ => Skip,
            };
        }
        return result;
    }

    private AutoDimResult DimensionLinear(Element element, Line line)
    {
        var direction = new XYZ(line.Direction.X, line.Direction.Y, 0);
        if (direction.IsZeroLength())
            return Skip;

        direction = direction.Normalize();
        var across = XYZ.BasisZ.CrossProduct(direction);
        var middle = line.Evaluate(0.5, true);
        return TryDimension(element, across, direction, middle.DotProduct(direction), middle.Z) ? Done : Skip;
    }

    private AutoDimResult DimensionColumn(Element element, XYZ center)
    {
        var result = default(AutoDimResult);
        var axes = new[] { (_view.RightDirection, _view.UpDirection), (_view.UpDirection, _view.RightDirection) };
        foreach (var (across, along) in axes)
        {
            var extent = GetOuterFaces(element, along);
            var at = extent is null
                ? center.DotProduct(along)
                : extent.Value.Max.Position + AutoDimGeometry.PaperToModel(_view, ColumnGapMm);
            result += TryDimension(element, across, along, at, center.Z) ? Done : Skip;
        }
        return result;
    }

    /// <param name="across">Dimension line direction: perpendicular to the measured faces and to the grid.</param>
    /// <param name="along">Direction the grid runs in.</param>
    /// <param name="at">Position of the dimension line along <paramref name="along"/>.</param>
    private bool TryDimension(Element element, XYZ across, XYZ along, double at, double z)
    {
        var faces = GetOuterFaces(element, across);
        if (faces is null)
            return false;

        var center = (faces.Value.Min.Position + faces.Value.Max.Position) / 2;
        var grid = _grids
            .Where(g => AutoDimGeometry.IsParallel(g.Line.Direction, along))
            .Select(g => new DimRef(new Reference(g.Grid), g.Line.GetEndPoint(0).DotProduct(across)))
            .OrderBy(g => Math.Abs(g.Position - center))
            .FirstOrDefault();
        if (grid.Reference is null)
            return false;

        // Grid listed first so it survives when a face is flush with it (equal positions = zero-length segment)
        var refs = new List<DimRef>();
        foreach (var r in new[] { grid, faces.Value.Min, faces.Value.Max }.OrderBy(r => r.Position))
        {
            if (refs.Count == 0 || r.Position - refs[^1].Position > _tolerance)
                refs.Add(r);
        }
        if (refs.Count < 2)
            return false;

        var references = new ReferenceArray();
        foreach (var r in refs)
            references.Append(r.Reference);

        var start = AutoDimGeometry.PointAt(across, refs[0].Position, along, at, z);
        var end = AutoDimGeometry.PointAt(across, refs[^1].Position, along, at, z);
        try
        {
            _writer.Create(Line.CreateBound(start, end), references);
            return true;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Some family faces are not dimensionable; skip the element instead of failing the whole run
            return false;
        }
    }

    /// <summary>The two outermost planar faces whose normal is parallel to <paramref name="axis"/>.</summary>
    private (DimRef Min, DimRef Max)? GetOuterFaces(Element element, XYZ axis)
    {
        var faces = new List<DimRef>();
        var geometry = element.get_Geometry(new Options { ComputeReferences = true, View = _view });
        if (geometry is null)
            return null;

        foreach (var obj in geometry)
        {
            if (obj is Solid solid)
            {
                Collect(solid, Transform.Identity);
            }
            else if (obj is GeometryInstance instance)
            {
                // Only symbol geometry carries references usable for dimensions; its transform maps it into the model
                foreach (var symbolObj in instance.GetSymbolGeometry())
                {
                    if (symbolObj is Solid symbolSolid)
                        Collect(symbolSolid, instance.Transform);
                }
            }
        }

        if (faces.Count < 2)
            return null;

        var min = faces.MinBy(f => f.Position);
        var max = faces.MaxBy(f => f.Position);
        return max.Position - min.Position > _tolerance ? (min, max) : null;

        void Collect(Solid solid, Transform transform)
        {
            foreach (Face face in solid.Faces)
            {
                if (face is PlanarFace { Reference: not null } planar
                    && AutoDimGeometry.IsParallel(transform.OfVector(planar.FaceNormal), axis))
                {
                    faces.Add(new DimRef(planar.Reference, transform.OfPoint(planar.Origin).DotProduct(axis)));
                }
            }
        }
    }
}
