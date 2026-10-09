using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CTTools.Features.Topo;

namespace CTTools.Features.Terrain;

public sealed class AlignToTopoHandler : IExternalEventHandler
{
    public AlignViewModel? Model { get; set; }
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
            var elements = Selected(uidoc);
            if (elements.Count == 0)
            {
                IList<Reference> references = Array.Empty<Reference>();
                TerrainHost.Picking(Window, () =>
                {
                    references = uidoc.Selection.PickObjects(ObjectType.Element, new AlignFilter(),
                        "Pick trees, lights, walls, or model lines. Click Finish on the ribbon.");
                });
                elements = references.Select(doc.GetElement).Where(element => element is not null).Cast<Element>().ToList();
            }

            var moved = 0;
            var skipped = 0;
            // One intersector for the whole selection. A 3D view makes thousands of trees cheap; the solid probe covers plans.
            var view = TerrainGeometry.Find3D(doc);
            ReferenceIntersector? intersector = null;
            try
            {
                if (view is not null)
                    intersector = new ReferenceIntersector(host.Id, FindReferenceTarget.Face, view);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                intersector = null;
            }

            var probe = TopoProbe.Build(host);
            var high = (host.get_BoundingBox(null)?.Max.Z ?? 0) + 30;
            var pending = new List<Element>();
            foreach (var element in elements)
            {
                if (element.Id == host.Id || element.Pinned || element.GroupId != ElementId.InvalidElementId)
                {
                    skipped++;
                    continue;
                }

                pending.Add(element);
            }

            TerrainGeometry.Transact(doc, "Align to toposolid", () =>
            {
                foreach (var element in pending)
                {
                    try
                    {
                        if (Move(element, intersector, probe, high, request.OffsetFeet))
                            moved++;
                        else
                            skipped++;
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        skipped++;
                    }
                }
            });
            model.Report($"Moved {moved} element(s) onto the surface. Skipped {skipped}.", moved == 0);
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

    public string GetName() => "CTTools Align to Topo";

    private static List<Element> Selected(UIDocument uidoc)
    {
        var list = new List<Element>();
        foreach (var id in uidoc.Selection.GetElementIds())
        {
            var element = uidoc.Document.GetElement(id);
            if (element is FamilyInstance or CurveElement or Wall)
                list.Add(element);
        }

        return list;
    }

    private static bool Move(Element element, ReferenceIntersector? intersector, TopoProbe? probe, double high, double offset)
    {
        if (element.Location is LocationPoint point)
        {
            if (!TryHit(point.Point.X, point.Point.Y, high, intersector, probe, out var z))
                return false;
            return SetPoint(element, point, z + offset);
        }

        if (element.Location is not LocationCurve curve)
            return false;
        if (element is CurveElement && curve.Curve is Line line)
            return DrapeLine(curve, line, high, intersector, probe, offset);
        var middle = curve.Curve.Evaluate(0.5, true);
        if (!TryHit(middle.X, middle.Y, high, intersector, probe, out var hit))
            return false;
        curve.Move(new XYZ(0, 0, hit + offset - middle.Z));
        return true;
    }

    private static bool DrapeLine(LocationCurve location, Line line, double high, ReferenceIntersector? intersector, TopoProbe? probe, double offset)
    {
        var a = line.GetEndPoint(0);
        var b = line.GetEndPoint(1);
        if (!TryHit(a.X, a.Y, high, intersector, probe, out var za) || !TryHit(b.X, b.Y, high, intersector, probe, out var zb))
            return false;
        var start = new XYZ(a.X, a.Y, za + offset);
        var end = new XYZ(b.X, b.Y, zb + offset);
        if (start.DistanceTo(end) < 1e-6)
            return false;
        location.Curve = Line.CreateBound(start, end);
        return true;
    }

    private static bool SetPoint(Element element, LocationPoint point, double target)
    {
        var delta = target - point.Point.Z;
        if (Math.Abs(delta) < 1e-8)
            return true;
        var elevation = element.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
        if (elevation is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            elevation.Set(elevation.AsDouble() + delta);
            return true;
        }

        var free = element.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
        if (free is { IsReadOnly: false, StorageType: StorageType.Double })
        {
            free.Set(free.AsDouble() + delta);
            return true;
        }

        point.Move(new XYZ(0, 0, delta));
        return true;
    }

    private static bool TryHit(double x, double y, double high, ReferenceIntersector? intersector, TopoProbe? probe, out double z)
    {
        z = 0;
        if (intersector is not null)
        {
            var hit = intersector.FindNearest(new XYZ(x, y, high), XYZ.BasisZ.Negate());
            var point = hit?.GetReference()?.GlobalPoint;
            if (point is not null)
            {
                z = point.Z;
                return true;
            }
        }

        return probe is not null && probe.TryVertical(x, y, out z);
    }
}
