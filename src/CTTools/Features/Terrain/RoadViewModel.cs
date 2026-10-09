using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Terrain;

public sealed class RoadRequest
{
    public ElementId HostId { get; init; } = ElementId.InvalidElementId;
    public ElementId TypeId { get; init; } = ElementId.InvalidElementId;
    public double OffsetFeet { get; init; }
}

public sealed class RoadViewModel : TerrainViewModel
{
    private readonly ExternalEvent _event;
    private TerrainChoice? _host;
    private TerrainChoice? _type;
    private string _offset = "0";

    public RoadViewModel(Document doc, ExternalEvent externalEvent, CreateRoadHandler handler)
    {
        _event = externalEvent;
        handler.Model = this;
        Hosts = TerrainGeometry.Surfaces(doc);
        Types = TerrainGeometry.Types(doc);
        SelectedHost = Hosts.FirstOrDefault();
        SelectedType = Types.FirstOrDefault();
        CreateCommand = new RelayCommand(() => Submit(() => Raise(_event)));
        Report("Pick a closed outline after you click Create. Offset is in millimeters: positive raises the sidewalk.", false);
    }

    public IReadOnlyList<TerrainChoice> Hosts { get; }
    public IReadOnlyList<TerrainChoice> Types { get; }
    public RelayCommand CreateCommand { get; }

    public TerrainChoice? SelectedHost
    {
        get => _host;
        set => Set(ref _host, value);
    }

    public TerrainChoice? SelectedType
    {
        get => _type;
        set => Set(ref _type, value);
    }

    public string OffsetText
    {
        get => _offset;
        set => Set(ref _offset, value);
    }

    public RoadRequest Read(Document doc) => new()
    {
        HostId = SelectedHost?.Id ?? ElementId.InvalidElementId,
        TypeId = SelectedType?.Id ?? ElementId.InvalidElementId,
        OffsetFeet = TerrainGeometry.ParseMillimeterEntry(doc, OffsetText, "Offset"),
    };
}
