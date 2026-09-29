using Autodesk.Revit.DB;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Creates grid dimension strings in a plan view: an overall dimension next to the grid ends
/// and a grid-to-grid chain just inside it. Must be called inside an open Transaction.
/// </summary>
public class GridDimensioner
{
    /// <summary>Paper distance from the grid ends (bubbles) to the first dimension string.</summary>
    private const double FirstOffsetMm = 12;

    /// <summary>Paper distance between two dimension strings.</summary>
    private const double SpacingMm = 8;

    private readonly DimensionWriter _writer;
    private readonly View _view;
    private readonly Document _doc;

    public GridDimensioner(DimensionWriter writer)
    {
        _writer = writer;
        _view = writer.View;
        _doc = _view.Document;
    }

    public AutoDimResult Run(DimSides sides)
    {
        var right = _view.RightDirection;
        var up = _view.UpDirection;

        // "Vertical" grids run along the view's up direction and are spaced left to right; "horizontal" the opposite.
        var vertical = new List<GridLine>();
        var horizontal = new List<GridLine>();

        var grids = AutoDimGeometry.GetStraightGrids(_view, out var skipped);
        foreach (var grid in grids)
        {
            if (AutoDimGeometry.IsParallel(grid.Line.Direction, up))
                vertical.Add(grid);
            else if (AutoDimGeometry.IsParallel(grid.Line.Direction, right))
                horizontal.Add(grid);
            else
                skipped++;
        }

        var created = 0;
        if (sides.HasFlag(DimSides.Top))
            created += DimensionGroup(vertical, across: right, along: up, atMaxEnd: true);
        if (sides.HasFlag(DimSides.Bottom))
            created += DimensionGroup(vertical, across: right, along: up, atMaxEnd: false);
        if (sides.HasFlag(DimSides.Right))
            created += DimensionGroup(horizontal, across: up, along: right, atMaxEnd: true);
        if (sides.HasFlag(DimSides.Left))
            created += DimensionGroup(horizontal, across: up, along: right, atMaxEnd: false);

        return new AutoDimResult(created, skipped);
    }

    /// <param name="across">Direction in which the grids are spaced (the dimension line direction).</param>
    /// <param name="along">Direction the grids run in.</param>
    /// <param name="atMaxEnd">Place dimensions at the grid ends with the largest coordinate along <paramref name="along"/>.</param>
    private int DimensionGroup(List<GridLine> grids, XYZ across, XYZ along, bool atMaxEnd)
    {
        var sorted = grids
            .Select(g => (g.Line, Ref: new DimRef(new Reference(g.Grid), g.Line.GetEndPoint(0).DotProduct(across))))
            .OrderBy(g => g.Ref.Position)
            .ToList();

        // Grids lying on top of each other would create zero-length segments, which Revit rejects
        var tolerance = _doc.Application.ShortCurveTolerance;
        var distinct = new List<(Line Line, DimRef Ref)>();
        foreach (var g in sorted)
        {
            if (distinct.Count == 0 || g.Ref.Position - distinct[^1].Ref.Position > tolerance)
                distinct.Add(g);
        }

        if (distinct.Count < 2)
            return 0;

        var endCoordinates = distinct
            .SelectMany(g => new[] { g.Line.GetEndPoint(0), g.Line.GetEndPoint(1) })
            .Select(p => p.DotProduct(along))
            .ToList();
        var gridEnd = atMaxEnd ? endCoordinates.Max() : endCoordinates.Min();
        var inward = atMaxEnd ? -1 : 1;
        var z = distinct[0].Line.GetEndPoint(0).Z;

        var offset = AutoDimGeometry.PaperToModel(_view, FirstOffsetMm);
        var created = 0;

        if (distinct.Count > 2)
        {
            CreateDimension([distinct[0].Ref, distinct[^1].Ref], across, along, gridEnd + inward * offset, z);
            created++;
            offset += AutoDimGeometry.PaperToModel(_view, SpacingMm);
        }

        CreateDimension(distinct.Select(g => g.Ref).ToList(), across, along, gridEnd + inward * offset, z);
        created++;

        return created;
    }

    private void CreateDimension(IReadOnlyList<DimRef> refs, XYZ across, XYZ along, double at, double z)
    {
        var references = new ReferenceArray();
        foreach (var r in refs)
            references.Append(r.Reference);

        var start = AutoDimGeometry.PointAt(across, refs[0].Position, along, at, z);
        var end = AutoDimGeometry.PointAt(across, refs[^1].Position, along, at, z);
        _writer.Create(Line.CreateBound(start, end), references);
    }
}
