using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using CTTools.Features.AutoDim;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class AutoDimGridsCommand : AutoDimCommandBase
{
    protected override string Title => "Auto Dim Grids";

    protected override string NothingToDimensionHint =>
        "Grid dimensions measure between parallel straight grids, so each direction needs at least 2 visible grids.";

    protected override AutoDimResult Dimension(DimensionWriter writer) =>
        new GridDimensioner(writer).Run(DimSides.Top | DimSides.Left);
}
