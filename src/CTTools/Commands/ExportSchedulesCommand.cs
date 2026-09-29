using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Core;
using Microsoft.Win32;

namespace CTTools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class ExportSchedulesCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var doc = uiApp.ActiveUIDocument?.Document;
        if (doc is null)
            return Result.Cancelled;

        var schedules = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(s => !s.IsTemplate && !s.IsTitleblockRevisionSchedule && !s.IsInternalKeynoteSchedule)
            .ToList();

        if (schedules.Count == 0)
        {
            TaskDialog.Show("Export Schedules", "This project has no schedules.");
            return Result.Cancelled;
        }

        var picker = new OpenFolderDialog { Title = "Choose a folder for the CSV files" };
        if (picker.ShowDialog() != true)
            return Result.Cancelled;

        var folder = picker.FolderName;
        var options = new ViewScheduleExportOptions
        {
            FieldDelimiter = ",",
            TextQualifier = ExportTextQualifier.DoubleQuote,
        };

        var failed = new List<string>();
        foreach (var schedule in schedules)
        {
            try
            {
                schedule.Export(folder, ToSafeFileName(schedule.Name) + ".csv", options);
            }
            catch (Exception ex)
            {
                failed.Add($"{schedule.Name}: {ex.Message}");
            }
        }

        var exported = schedules.Count - failed.Count;
        var dialog = new TaskDialog("Export Schedules")
        {
            MainInstruction = $"Exported {exported}/{schedules.Count} schedules.",
            MainContent = failed.Count > 0 ? "Failed:\n" + string.Join("\n", failed) : folder,
        };
        dialog.Show();

        if (exported > 0)
            Process.Start("explorer.exe", folder);

        return Result.Succeeded;
    }

    private static string ToSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
