using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Features.AutoDim;

namespace CTTools.Commands;

/// <summary>
/// Shared flow for auto-dimension commands: picks the target plan views, runs everything in one transaction
/// (one Ctrl+Z undoes it all) and reports the result. Derived classes need [Transaction(TransactionMode.Manual)].
/// </summary>
public abstract class AutoDimCommandBase : CommandBase
{
    protected abstract string Title { get; }

    protected abstract string NothingToDimensionHint { get; }

    protected abstract AutoDimResult Dimension(DimensionWriter writer);

    protected override Result Run(UIApplication uiApp)
    {
        var uiDoc = uiApp.ActiveUIDocument;
        if (uiDoc is null)
            return Result.Cancelled;

        var views = GetTargetViews(uiDoc);
        if (views.Count == 0)
        {
            TaskDialog.Show(Title, "Open a plan view, or a sheet that contains plan views.");
            return Result.Cancelled;
        }

        var dimensionType = DimensionTypePicker.Pick(uiApp, Title);

        var total = default(AutoDimResult);
        var dimensionedViews = 0;
        var movedTexts = 0;
        using (var tx = new Transaction(uiDoc.Document, Title))
        {
            tx.Start();
            var writers = new List<DimensionWriter>();
            foreach (var view in views)
            {
                var writer = new DimensionWriter(view, dimensionType);
                var result = Dimension(writer);
                writers.Add(writer);
                total += result;
                if (result.Created > 0)
                    dimensionedViews++;
            }

            if (total.Created == 0)
            {
                tx.RollBack();
                var dialog = new TaskDialog(Title)
                {
                    MainInstruction = "Nothing to dimension",
                    MainContent = NothingToDimensionHint + SkippedNote(total),
                };
                dialog.Show();
                return Result.Cancelled;
            }

            // Segment values and text positions are only available after regeneration
            uiDoc.Document.Regenerate();
            foreach (var writer in writers)
                movedTexts += DimensionTextFitter.Fit(writer.Created, writer.View);

            tx.Commit();
        }

        var movedNote = movedTexts > 0 ? $"\n{movedTexts} texts on short segments were moved outside so they don't overlap." : string.Empty;
        TaskDialog.Show(Title, $"Created {total.Created} dimension strings in {dimensionedViews} view(s)." + movedNote + SkippedNote(total));
        return Result.Succeeded;
    }

    private static string SkippedNote(AutoDimResult result) =>
        result.Skipped > 0
            ? $"\n{result.Skipped} items were skipped (curved, angled, no parallel grid or no usable faces)."
            : string.Empty;

    /// <summary>
    /// The active plan view, or - on a sheet - the selected plan viewports (all plan viewports if none selected).
    /// </summary>
    private static List<ViewPlan> GetTargetViews(UIDocument uiDoc)
    {
        var doc = uiDoc.Document;
        switch (uiDoc.ActiveView)
        {
            case ViewPlan plan:
                return [plan];

            case ViewSheet sheet:
                var selectedViewports = uiDoc.Selection.GetElementIds()
                    .Select(doc.GetElement)
                    .OfType<Viewport>()
                    .ToList();
                var viewports = selectedViewports.Count > 0
                    ? selectedViewports
                    : sheet.GetAllViewports().Select(doc.GetElement).OfType<Viewport>().ToList();
                return viewports
                    .Select(vp => doc.GetElement(vp.ViewId))
                    .OfType<ViewPlan>()
                    .ToList();

            default:
                return [];
        }
    }
}
