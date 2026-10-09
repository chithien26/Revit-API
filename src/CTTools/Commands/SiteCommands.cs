using System.Globalization;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CTTools.Core;
using CTTools.Features.Site;
using CTTools.Features.Terrain;
using Microsoft.Win32;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class EditWallCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var wall = (Wall)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new WallFilter(), "Pick a wall."));
        var units = doc.GetUnits();
        var offset = wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.AsDouble() ?? 0;
        var height = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 10;
        var initial = UnitFormatUtils.Format(units, SpecTypeId.Length, offset, false);
        if (!SiteDialog.Text(uiApp.MainWindowHandle, "Edit wall", "Base offset, then the wall height on the next line. Example:\n0\n3000", initial + "\n" + UnitFormatUtils.Format(units, SpecTypeId.Length, height, false), out var text))
            return Result.Cancelled;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            throw new InvalidOperationException("Enter the base offset and the height, one per line.");
        var newOffset = TerrainGeometry.ParseProjectLength(doc, lines[0], "Base offset");
        var newHeight = TerrainGeometry.ParseProjectLength(doc, lines[1], "Height");
        TerrainGeometry.Transact(doc, "Edit wall", () =>
        {
            wall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET)?.Set(newOffset);
            var heightParameter = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (heightParameter is { IsReadOnly: false })
                heightParameter.Set(newHeight);
        });
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ShapeByTopoCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var walls = uidoc.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Pick straight walls. Click Finish.")
            .Select(doc.GetElement).OfType<Wall>().ToList();
        var shaped = SiteWork.ShapeWalls(doc, ground, walls);
        TaskDialog.Show("Shape by topography", shaped == 0
            ? "No wall was changed. Use straight walls with no doors or windows."
            : $"Shaped {shaped} wall(s) so the bottom follows the ground.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class WallRailingCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var types = new FilteredElementCollector(doc).OfClass(typeof(RailingType)).Cast<RailingType>().OrderBy(type => type.Name).ToList();
        if (types.Count == 0)
            throw new InvalidOperationException("This project has no railing type.");
        if (!SiteDialog.Choice(uiApp.MainWindowHandle, "Wall railing", "Railing type.", types.Select(type => type.Name).ToList(), out var index))
            return Result.Cancelled;
        var walls = uidoc.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Pick walls. Click Finish.")
            .Select(doc.GetElement).OfType<Wall>().ToList();
        var placed = SiteWork.RailingsOnWalls(doc, walls, types[index].Id);
        TaskDialog.Show("Wall railing", $"Placed {placed} railing(s) on the top of the walls.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class SetElevationCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var curves = uidoc.Selection.PickObjects(ObjectType.Element, new ModelLineFilter(), "Pick model lines. Click Finish.")
            .Select(doc.GetElement).OfType<ModelCurve>().ToList();
        if (!SiteDialog.Text(uiApp.MainWindowHandle, "Set elevation", "Elevation of the lines, in project units.", "0", out var text))
            return Result.Cancelled;
        var elevation = TerrainGeometry.ParseProjectLength(doc, text, "Elevation");
        var moved = SiteWork.SetElevation(doc, curves, elevation);
        TaskDialog.Show("Set elevation", $"Moved {moved} line(s).");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class SetCoordinateCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var curve = (ModelCurve)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ModelLineFilter(), "Pick a model line."));
        var start = curve.GeometryCurve.GetEndPoint(0);
        var units = doc.GetUnits();
        var seed = string.Join("\n", new[] { start.X, start.Y, start.Z }.Select(value => UnitFormatUtils.Format(units, SpecTypeId.Length, value, false)));
        if (!SiteDialog.Text(uiApp.MainWindowHandle, "Set coordinate", "New X, Y and Z of the line start, one per line. The whole line moves.", seed, out var text))
            return Result.Cancelled;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 3)
            throw new InvalidOperationException("Enter X, Y and Z, one per line.");
        var target = new XYZ(
            TerrainGeometry.ParseProjectLength(doc, lines[0], "X"),
            TerrainGeometry.ParseProjectLength(doc, lines[1], "Y"),
            TerrainGeometry.ParseProjectLength(doc, lines[2], "Z"));
        SiteWork.MoveCurve(doc, curve, target);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.ReadOnly)]
public class GetLocationCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var point = uidoc.Selection.PickPoint("Click a point.");
        var units = uidoc.Document.GetUnits();
        string Length(double feet) => UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false);
        var meters = new XYZ(
            UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Meters),
            UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Meters),
            UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Meters));
        var message = $"X {Length(point.X)}\nY {Length(point.Y)}\nZ {Length(point.Z)}\n\n{meters.X:0.###}, {meters.Y:0.###}, {meters.Z:0.###} m";
        System.Windows.Clipboard.SetText($"{meters.X.ToString(CultureInfo.InvariantCulture)}, {meters.Y.ToString(CultureInfo.InvariantCulture)}, {meters.Z.ToString(CultureInfo.InvariantCulture)}");
        TaskDialog.Show("Location", message + "\n\nMeters were copied to the clipboard.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ScanToBlocksCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var symbols = SiteWork.Symbols(doc, plantingOnly: false);
        if (symbols.Count == 0)
            throw new InvalidOperationException("Load a planting, generic model, or site family first.");
        if (!SiteDialog.TextAndChoice(uiApp.MainWindowHandle, "CAD points", "Layer names, separated by commas. Then choose the family.", "POINTS", symbols.Select(SiteWork.SymbolLabel).ToList(), out var layers, out var index))
            return Result.Cancelled;
        var import = (ImportInstance)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ImportFilter(), "Pick the CAD import."));
        Toposolid? ground = null;
        var drop = new TaskDialog("CAD points") { MainInstruction = "Drop the blocks onto a toposolid?" };
        drop.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Use the point elevation from CAD");
        drop.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Drop onto a surface");
        if (drop.Show() == TaskDialogResult.CommandLink2)
            ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var placed = SiteWork.BlocksFromCad(doc, import, layers, symbols[index], ground);
        TaskDialog.Show("CAD points", $"Placed {placed} block(s).");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class AlignmentCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var types = TerrainGeometry.Types(doc);
        if (types.Count == 0)
            throw new InvalidOperationException("This project has no toposolid type.");
        if (!SiteDialog.TextAndChoice(uiApp.MainWindowHandle, "Alignment", "Road width in millimeters, and the surface type.", "6000", types.Select(type => type.Name).ToList(), out var widthText, out var index))
            return Result.Cancelled;
        var width = TerrainGeometry.ParseMillimeterEntry(doc, widthText, "Width");
        var ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var path = uidoc.Selection.PickObjects(ObjectType.Element, new ModelLineFilter(), "Pick the centerline. Click Finish.")
            .Select(doc.GetElement).OfType<ModelCurve>().Select(curve => curve.GeometryCurve).ToList();
        var road = SiteWork.Alignment(doc, ground, types[index].Id, path, width);
        uidoc.Selection.SetElementIds(new List<ElementId> { road.Id });
        TaskDialog.Show("Alignment", "The road follows the centerline on the ground.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class PlantingCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var symbols = SiteWork.Symbols(doc, plantingOnly: true);
        if (symbols.Count == 0)
            throw new InvalidOperationException("Load a planting family, or a generic model to stand in for one.");
        if (!SiteDialog.Choice(uiApp.MainWindowHandle, "Planting", "Family to place on the ground.", symbols.Select(SiteWork.SymbolLabel).ToList(), out var index))
            return Result.Cancelled;
        var ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var clicks = new List<XYZ>();
        while (true)
        {
            try
            {
                clicks.Add(uidoc.Selection.PickPoint("Click where the plant goes. Press Esc when finished."));
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                break;
            }
        }

        if (clicks.Count == 0)
            return Result.Cancelled;
        var placed = SiteWork.PlaceOnClicks(doc, symbols[index], ground, clicks);
        TaskDialog.Show("Planting", $"Placed {placed} plant(s).");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class AreaScatterCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var symbols = SiteWork.Symbols(doc, plantingOnly: false);
        if (symbols.Count == 0)
            throw new InvalidOperationException("Load a family to scatter.");
        if (!SiteDialog.TextAndChoice(uiApp.MainWindowHandle, "Area scatter", "Spacing in millimeters, and the family.", "3000", symbols.Select(SiteWork.SymbolLabel).ToList(), out var spacingText, out var index))
            return Result.Cancelled;
        var spacing = TerrainGeometry.ParseMillimeterEntry(doc, spacingText, "Spacing");
        if (spacing < 0.5)
            throw new InvalidOperationException("Spacing is too small.");
        var ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var loops = TerrainGeometry.Boundaries(doc, uidoc.Selection.PickObjects(ObjectType.Element, new BoundaryFilter(), "Pick a closed outline. Click Finish."));
        var placed = SiteWork.Scatter(doc, symbols[index], ground, loops, spacing);
        TaskDialog.Show("Area scatter", $"Placed {placed} instance(s).");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ElevationAnalysisCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var ground = (Toposolid)doc.GetElement(uidoc.Selection.PickObject(ObjectType.Element, new ToposolidFilter(), "Pick the ground."));
        var (min, max, count) = SiteWork.ElevationMap(doc, doc.ActiveView, ground);
        string Length(double feet) => UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, feet, false);
        TaskDialog.Show("Elevation", $"Low {Length(min)}\nHigh {Length(max)}\n{count} sample points.\n\nBlue is low, red is high. The legend is in meters.");
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.ReadOnly)]
public class WallLayoutCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var uidoc = uiApp.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
        var doc = uidoc.Document;
        var walls = uidoc.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Pick walls. Click Finish.")
            .Select(doc.GetElement).OfType<Wall>().ToList();
        var rows = SiteWork.WallRows(walls);
        var culture = CultureInfo.CurrentCulture;
        var preview = string.Join("\n", rows.Take(12).Select(row =>
            $"{row.Mark}   {UnitUtils.ConvertFromInternalUnits(row.Length, UnitTypeId.Meters).ToString("0.##", culture)} m   {UnitUtils.ConvertFromInternalUnits(row.Area, UnitTypeId.SquareMeters).ToString("0.##", culture)} m²"));
        var dialog = new TaskDialog("Wall layout")
        {
            MainInstruction = $"{rows.Count} wall(s)",
            MainContent = preview,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Export CSV");
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Close");
        if (dialog.Show() != TaskDialogResult.CommandLink1)
            return Result.Succeeded;
        var save = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "wall-layout.csv" };
        if (save.ShowDialog() != true)
            return Result.Cancelled;
        var separator = culture.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        var lines = new List<string> { $"Mark{separator}Length m{separator}Area m2" };
        lines.AddRange(rows.Select(row =>
            $"{row.Mark}{separator}{UnitUtils.ConvertFromInternalUnits(row.Length, UnitTypeId.Meters).ToString("0.###", culture)}{separator}{UnitUtils.ConvertFromInternalUnits(row.Area, UnitTypeId.SquareMeters).ToString("0.###", culture)}"));
        File.WriteAllLines(save.FileName, lines, new UTF8Encoding(true));
        return Result.Succeeded;
    }
}
