using Autodesk.Revit.DB;

namespace CTTools.Features.AutoDim;

[Flags]
public enum DimSides
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8,
}

/// <param name="Created">Dimension strings created.</param>
/// <param name="Skipped">Grids or elements that could not be dimensioned (curved, angled, no parallel grid...).</param>
public readonly record struct AutoDimResult(int Created, int Skipped)
{
    public static AutoDimResult operator +(AutoDimResult a, AutoDimResult b) =>
        new(a.Created + b.Created, a.Skipped + b.Skipped);
}

internal record GridLine(Grid Grid, Line Line);

/// <summary>A dimensionable reference and its position along the dimension direction.</summary>
internal readonly record struct DimRef(Reference Reference, double Position);

internal static class AutoDimGeometry
{
    private const double ParallelTolerance = 1e-4;

    public static bool IsParallel(XYZ a, XYZ b) => Math.Abs(Math.Abs(a.DotProduct(b)) - 1) < ParallelTolerance;

    public static double PaperToModel(View view, double millimeters) => millimeters / 304.8 * view.Scale;

    public static XYZ PointAt(XYZ across, double position, XYZ along, double at, double z)
    {
        var p = across * position + along * at;
        return new XYZ(p.X, p.Y, z);
    }

    /// <summary>Straight grids visible in the view, with their view-specific extents.</summary>
    public static List<GridLine> GetStraightGrids(View view, out int skipped)
    {
        var grids = new List<GridLine>();
        skipped = 0;

        var collector = new FilteredElementCollector(view.Document, view.Id)
            .OfClass(typeof(Grid))
            .Cast<Grid>();

        foreach (var grid in collector)
        {
            if (grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).FirstOrDefault() is Line line)
                grids.Add(new GridLine(grid, line));
            else
                skipped++;
        }
        return grids;
    }
}
