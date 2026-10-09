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

        var model = application.CreateRibbonPanel(TabName, "Model Elements");
        model.AddButton<EditWallCommand>("Edit Wall", RibbonIcons.EditWall,
            "Set a wall's base offset and height.");
        model.AddButton<ShapeByTopoCommand>("Shape by\nTopography", RibbonIcons.ShapeWall,
            "Cut the bottom of straight walls so they follow the ground.");
        model.AddButton<WallRailingCommand>("Wall\nRailing", RibbonIcons.WallRailing,
            "Place a railing along the top of selected walls.");

        var lines = application.CreateRibbonPanel(TabName, "Model Lines");
        lines.AddButton<SetElevationCommand>("Set\nElevation", RibbonIcons.SetElevation,
            "Move model lines to one elevation.");
        lines.AddButton<SetCoordinateCommand>("Set\nCoordinate", RibbonIcons.SetCoordinate,
            "Move a model line so its start sits at an X, Y, Z.");
        lines.AddButton<GetLocationCommand>("Get\nLocation", RibbonIcons.GetLocation,
            "Click a point and copy its coordinates.");

        var civil = application.CreateRibbonPanel(TabName, "Civil Data");
        civil.AddButton<ScanToBlocksCommand>("CAD\nPoints", RibbonIcons.CadPoints,
            "Place a family on points or contour vertices of a chosen CAD layer.");
        civil.AddButton<CreateToposolidCommand>("Import\nSurface", RibbonIcons.CreateTopo,
            "Create a toposolid from a CAD import or a CSV/TXT point file.");
        civil.AddButton<AlignmentCommand>("Alignment", RibbonIcons.Road,
            "Build a road along a model-line centerline, on a toposolid.");

        var site = application.CreateRibbonPanel(TabName, "Site");
        var topography = site.AddMenu("TopographyTools", "Topography\nTools", RibbonIcons.Topo,
            "Sculpt, create, and measure toposolids.");
        topography.AddToMenu<TopoCommand>("Sculpt", RibbonIcons.Topo,
            "Click the surface to raise, lower, smooth, inflate, or edit the mesh.");
        topography.AddToMenu<CreateToposolidCommand>("New surface", RibbonIcons.CreateTopo,
            "Create a toposolid from CAD or a point file.");
        topography.AddToMenu<CreateRoadCommand>("Road outline", RibbonIcons.Road,
            "A closed outline becomes a road or sidewalk on the host surface.");
        topography.AddToMenu<ExcavateCommand>("Excavate", RibbonIcons.Excavate,
            "Cut a pit and grade the side slope.");
        topography.AddToMenu<AlignToTopoCommand>("Drop to ground", RibbonIcons.AlignTopo,
            "Move trees, lights, walls, and lines onto the surface.");
        topography.AddToMenu<CutFillReportCommand>("Cut and fill", RibbonIcons.CutFill,
            "Compare two surfaces and export the volumes.");
        site.AddButton<PlantingCommand>("Planting", RibbonIcons.Planting,
            "Click the ground to place a planting family.");
        site.AddButton<AreaScatterCommand>("Area\nScatter", RibbonIcons.Scatter,
            "Fill a closed outline with a family, spaced on the ground.");

        var analysis = application.CreateRibbonPanel(TabName, "Analysis");
        analysis.AddButton<ElevationAnalysisCommand>("Elevation", RibbonIcons.Elevation,
            "Color a 3D view by ground elevation.");
        analysis.AddButton<CutFillReportCommand>("Cut &\nFill", RibbonIcons.CutFill,
            "Compare two toposolids and export cut and fill volumes.");

        var docs = application.CreateRibbonPanel(TabName, "Documentation");
        docs.AddButton<WallLayoutCommand>("Wall\nLayout", RibbonIcons.WallLayout,
            "List wall lengths and areas, and export them to CSV.");

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
