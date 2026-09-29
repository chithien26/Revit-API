using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using CTTools.Features.AutoDim;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class AutoDimElementsCommand : AutoDimCommandBase
{
    protected override string Title => "Auto Dim Elements";

    protected override string NothingToDimensionHint =>
        "No beams, walls or columns parallel to a visible straight grid were found in this view.";

    protected override AutoDimResult Dimension(DimensionWriter writer) => new ElementDimensioner(writer).Run();
}
