using Autodesk.Revit.DB;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Creates dimensions in one view with the chosen dimension type and remembers them for post-processing.
/// </summary>
public sealed class DimensionWriter(View view, DimensionType? type)
{
    public View View { get; } = view;

    public List<Dimension> Created { get; } = [];

    public Dimension Create(Line line, ReferenceArray references)
    {
        var doc = View.Document;
        var dimension = type is null
            ? doc.Create.NewDimension(View, line, references)
            : doc.Create.NewDimension(View, line, references, type);
        Created.Add(dimension);
        return dimension;
    }
}
