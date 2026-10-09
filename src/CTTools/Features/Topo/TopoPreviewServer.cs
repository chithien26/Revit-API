using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace CTTools.Features.Topo;

public readonly record struct PreviewSegment(XYZ Start, XYZ End, byte Red, byte Green, byte Blue);

/// <summary>Draws the influence rings and the edit grid without adding model lines.</summary>
public sealed class TopoPreviewServer : IDirectContext3DServer
{
    private static readonly Guid ServerId = new("c3b1a7e2-6f44-4e1a-9c2d-7a1e5b0d44f8");
    private readonly object _gate = new();
    private List<PreviewSegment> _segments = new();
    private Outline _outline = new(new XYZ(-1, -1, -1), new XYZ(1, 1, 1));

    public void SetSegments(IReadOnlyList<PreviewSegment> segments)
    {
        Outline outline;
        if (segments.Count == 0)
        {
            outline = new Outline(new XYZ(-1, -1, -1), new XYZ(1, 1, 1));
        }
        else
        {
            var first = segments[0].Start;
            outline = new Outline(first, first + new XYZ(1, 1, 1));
            foreach (var segment in segments)
            {
                outline.AddPoint(segment.Start);
                outline.AddPoint(segment.End);
            }
        }

        lock (_gate)
        {
            _segments = segments.ToList();
            _outline = outline;
        }
    }

    public void Register(Document doc)
    {
        if (ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service)
            return;
        if (!service.GetRegisteredServerIds().Contains(ServerId))
            service.AddServer(this);

        var active = service.GetActiveServerIds().ToList();
        if (!active.Contains(ServerId))
        {
            active.Add(ServerId);
            service.SetActiveServers(active);
        }
    }

    public void Unregister()
    {
        if (ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService)
            is not MultiServerService service)
            return;
        var active = service.GetActiveServerIds().Where(id => id != ServerId).ToList();
        service.SetActiveServers(active);
        if (service.GetRegisteredServerIds().Contains(ServerId))
            service.RemoveServer(ServerId);
    }

    public bool CanExecute(View dBView) => true;
    public string GetApplicationId() => "CTTools";
    public Guid GetServerId() => ServerId;
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetSourceId() => "TopoPreview";
    public string GetName() => "CTTools Topo Preview";
    public string GetVendorId() => "ChiThien";
    public string GetDescription() => "Influence ring and edit grid for topography sculpting.";
    public bool UseInTransparentPass(View dBView) => true;
    public bool UsesHandles() => false;

    public Outline GetBoundingBox(View dBView)
    {
        lock (_gate)
            return _outline;
    }

    public void RenderScene(View dBView, DisplayStyle displayStyle)
    {
        if (!DrawContext.IsAvailable())
            return;

        List<PreviewSegment> segments;
        lock (_gate)
            segments = _segments;
        if (segments.Count == 0)
            return;

        DrawContext.SetWorldTransform(Transform.Identity);
        var format = new VertexFormat(VertexFormatBits.PositionColored);
        var vertexCount = segments.Count * 2;
        var vertices = new VertexBuffer(vertexCount);
        vertices.Map(vertexCount * VertexPositionColored.GetSizeInFloats());
        var stream = vertices.GetVertexStreamPositionColored();
        foreach (var segment in segments)
        {
            var color = new ColorWithTransparency(segment.Red, segment.Green, segment.Blue, 0);
            stream.AddVertex(new VertexPositionColored(segment.Start, color));
            stream.AddVertex(new VertexPositionColored(segment.End, color));
        }

        vertices.Unmap();

        var indexCount = segments.Count * 2;
        var indices = new IndexBuffer(indexCount);
        indices.Map(indexCount);
        var indexStream = indices.GetIndexStreamLine();
        for (var i = 0; i < segments.Count; i++)
            indexStream.AddLine(new IndexLine(i * 2, i * 2 + 1));
        indices.Unmap();

        var effect = new EffectInstance(VertexFormatBits.PositionColored);
        DrawContext.FlushBuffer(vertices, vertexCount, indices, indexCount, format, effect,
            PrimitiveType.LineList, 0, segments.Count);
    }
}
