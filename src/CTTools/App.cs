using Autodesk.Revit.UI;
using CTTools.Commands;
using CTTools.Core;
using CTTools.Features.FolderMemory;

namespace CTTools;

public class App : IExternalApplication
{
    public const string TabName = "CTTools";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            application.CreateRibbonTab(TabName);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab already exists
        }

        var general = application.CreateRibbonPanel(TabName, "General");
        general.AddButton<AboutCommand>("About", RibbonIcons.About,
            "Show CTTools version information.");
        general.AddButton<LicenseCommand>("License", RibbonIcons.License,
            "Check the CTTools license of the Autodesk account signed in to Revit.");

        var annotate = application.CreateRibbonPanel(TabName, "Annotate");
        annotate.AddButton<AutoDimAllCommand>("Auto Dim\nAll", RibbonIcons.AutoDimAll,
            "Grid dimensions + beams, walls and columns dimensioned to their nearest grid. Works on a plan view or a sheet.");
        annotate.AddButton<AutoDimGridsCommand>("Auto Dim\nGrids", RibbonIcons.AutoDimGrids,
            "Dimension all straight grids: overall + grid-to-grid strings on the top and left.");
        annotate.AddButton<AutoDimElementsCommand>("Auto Dim\nElements", RibbonIcons.AutoDimElements,
            "Dimension beams, walls and columns to their nearest parallel grid (offset + width).");

        var tools = application.CreateRibbonPanel(TabName, "Tools");
        tools.AddButton<LicenseTestCommand>("Test\nLicense", RibbonIcons.TestLicense,
            "Simplest licensed tool: runs only when your Autodesk account is licensed.");
        tools.AddButton<ElementCountCommand>("Count\nElements", RibbonIcons.CountElements,
            "Count visible elements in the active view, grouped by category.");
        tools.AddButton<ExportSchedulesCommand>("Export\nSchedules", RibbonIcons.ExportSchedules,
            "Export every schedule in the project to CSV files.");
        tools.AddButton<FileFoldersCommand>("File\nFolders", RibbonIcons.FileFolders,
            "Open, Load Family, Link, Import and Export each remember their own last folder. Shows and resets them.");

        FolderMemoryService.Register(application);

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
}
