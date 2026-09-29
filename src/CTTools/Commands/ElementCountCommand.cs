using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Core;

namespace CTTools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class ElementCountCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        var view = doc?.ActiveView;
        if (doc is null || view is null)
        {
            TaskDialog.Show("Count Elements", "Please open a view first.");
            return Result.Cancelled;
        }

        var groups = new FilteredElementCollector(doc, view.Id)
            .WhereElementIsNotElementType()
            .Where(e => e.Category is not null)
            .GroupBy(e => e.Category.Name)
            .OrderByDescending(g => g.Count())
            .ToList();

        var report = new StringBuilder();
        foreach (var group in groups)
            report.AppendLine($"{group.Key}: {group.Count()}");

        var dialog = new TaskDialog("Count Elements")
        {
            MainInstruction = $"{groups.Sum(g => g.Count())} elements in \"{view.Name}\"",
            MainContent = report.Length > 0 ? report.ToString() : "No elements found.",
        };
        dialog.Show();
        return Result.Succeeded;
    }
}
