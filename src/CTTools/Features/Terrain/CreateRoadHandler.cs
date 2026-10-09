using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CTTools.Features.Topo;

namespace CTTools.Features.Terrain;

public sealed class CreateRoadHandler : IExternalEventHandler
{
    public RoadViewModel? Model { get; set; }
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
            if (doc.GetElement(request.TypeId) is not ToposolidType)
                throw new InvalidOperationException("Choose a toposolid type for the road.");
            var host = TerrainGeometry.RequireSurface(doc, request.HostId, "host toposolid");
            IList<Reference> references = Array.Empty<Reference>();
            TerrainHost.Picking(Window, () =>
            {
                references = uidoc.Selection.PickObjects(ObjectType.Element, new BoundaryFilter(),
                    "Pick the closed road or sidewalk outline. Click Finish on the ribbon.");
            });
            var loops = TerrainGeometry.Boundaries(doc, references);
            ElementId? createdId = null;
            var separate = false;
            TerrainGeometry.Transact(doc, "Create road", () =>
            {
                Toposolid created;
                try
                {
                    created = host.CreateSubDivision(doc, loops);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    separate = true;
                    created = Drape(doc, host, loops, request.TypeId, request.OffsetFeet);
                }

                createdId = created.Id;
                if (separate)
                    return;
                if (created.GetTypeId() != request.TypeId)
                {
                    var changed = created.ChangeTypeId(request.TypeId);
                    if (changed != ElementId.InvalidElementId && doc.GetElement(changed) is Toposolid replaced)
                    {
                        created = replaced;
                        createdId = replaced.Id;
                    }
                }
                // Official offset of a subdivision above the host. Positive lifts a sidewalk.
                var height = created.get_Parameter(BuiltInParameter.TOPOSOLID_SUBDIVIDE_HEIGHT);
                if (height is { IsReadOnly: false })
                    height.Set(request.OffsetFeet);
                else if (Math.Abs(request.OffsetFeet) > 1e-8)
                    Lift(created, request.OffsetFeet);
            });
            if (createdId is null)
                throw new InvalidOperationException("Revit did not create the road.");
            uidoc.Selection.SetElementIds(new List<ElementId> { createdId });
            model.Report(separate
                ? "Created a separate surface that follows the host. The outline could not subdivide it."
                : "Created the road on the host surface.", false);
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

    public string GetName() => "CTTools Create Road";

    private static void Lift(Toposolid topo, double extra)
    {
        var editor = TopoEditor.Open(topo);
        var basis = TopoEditor.BaseElevation(topo);
        foreach (var vertex in TopoEditor.Vertices(editor))
            editor.ModifySubElement(vertex, vertex.Position.Z + extra - basis);
    }

    private static Toposolid Drape(Document doc, Toposolid host, IList<CurveLoop> loops, ElementId typeId, double offset)
    {
        var probe = TopoProbe.Build(host) ?? throw new InvalidOperationException("The host surface has no solid.");
        var points = new List<XYZ>();
        foreach (var loop in loops)
        {
            foreach (var curve in loop)
            {
                foreach (var point in curve.Tessellate())
                {
                    if (!probe.TryVertical(point.X, point.Y, out var z))
                        continue;
                    points.Add(new XYZ(point.X, point.Y, z + offset));
                }
            }
        }

        if (points.Count < 3)
            throw new InvalidOperationException("The outline does not sit on the host surface.");
        return Toposolid.Create(doc, loops, points, typeId, host.LevelId);
    }
}
