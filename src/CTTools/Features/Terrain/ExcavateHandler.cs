using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CTTools.Features.Terrain;

public sealed class ExcavateHandler : IExternalEventHandler
{
    public ExcavateViewModel? Model { get; set; }
    public Window? Window { get; set; }

    public void Execute(UIApplication app)
    {
        var model = Model;
        if (model is null)
            return;
        try
        {
            var uidoc = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
            var doc = uidoc.Document;
            var request = model.Read(doc);
            var host = TerrainGeometry.RequireSurface(doc, request.HostId, "toposolid");
            IList<Reference> references = Array.Empty<Reference>();
            TerrainHost.Picking(Window, () =>
            {
                references = uidoc.Selection.PickObjects(ObjectType.Element, new BoundaryFilter(),
                    "Pick the closed bottom of the excavation. Click Finish on the ribbon.");
            });
            var loops = TerrainGeometry.Boundaries(doc, references);
            // 1:1.5 means the slope travels 1.5 horizontally for each 1 of height, until it daylights on the existing ground.
            var changed = TerrainGeometry.Grade(host, loops, request.BottomFeet, request.RunPerRise);
            uidoc.Selection.SetElementIds(new List<ElementId> { host.Id });
            model.Report(changed == 0
                ? "Nothing changed. The outline needs to sit on the surface."
                : "Excavated the pit and graded the side slope up to the existing ground.", false);
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

    public string GetName() => "CTTools Excavate";
}
