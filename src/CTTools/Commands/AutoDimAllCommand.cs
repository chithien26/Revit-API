using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using CTTools.Features.AutoDim;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class AutoDimAllCommand : AutoDimCommandBase
{
    protected override string Title => "Auto Dim All";

    protected override string NothingToDimensionHint =>
        "Needs at least 2 parallel straight grids, or beams/walls/columns parallel to a visible grid.";

    protected override AutoDimResult Dimension(DimensionWriter writer) =>
        new GridDimensioner(writer).Run(DimSides.Top | DimSides.Left) + new ElementDimensioner(writer).Run();
}
