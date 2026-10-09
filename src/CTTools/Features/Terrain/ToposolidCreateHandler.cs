using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Terrain;

public sealed class ToposolidCreateHandler : IExternalEventHandler
{
    public ToposolidFromSourceViewModel? Model { get; set; }

    public void Execute(UIApplication app)
    {
        var model = Model;
        if (model is null)
            return;
        try
        {
            var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
            var doc = uidoc.Document;
            var request = model.Read();
            TerrainGeometry.RequireTypeAndLevel(doc, request.TypeId, request.LevelId);
            // CAD vertices are already in feet. CSV/TXT numbers are converted from m or mm.
            var points = request.FromFile
                ? TerrainGeometry.FromFile(request.FilePath, request.Millimeters)
                : TerrainGeometry.FromImport(doc, request.ImportId, request.Layers);
            var created = TerrainGeometry.CreateSurface(doc, points, request.TypeId, request.LevelId, "Create toposolid");
            uidoc.Selection.SetElementIds(new List<ElementId> { created.Id });
            model.Report($"Created a toposolid from {points.Count} points.", false);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            model.Report("Cancelled.", false);
        }
        catch (Exception ex)
        {
            model.Report(TerrainGeometry.Clean(ex), true);
        }
    }

    public string GetName() => "CTTools Create Toposolid";
}
