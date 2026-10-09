using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Terrain;

public sealed class ExcavateRequest
{
    public ElementId HostId { get; init; } = ElementId.InvalidElementId;
    public double BottomFeet { get; init; }
    public double RunPerRise { get; init; }
}

public sealed class ExcavateViewModel : TerrainViewModel
{
    private readonly ExternalEvent _event;
    private TerrainChoice? _host;
    private string _bottom;
    private string _slope = "1:1.5";

    public ExcavateViewModel(Document doc, ExternalEvent externalEvent, ExcavateHandler handler)
    {
        _event = externalEvent;
        handler.Model = this;
        Hosts = TerrainGeometry.Surfaces(doc);
        SelectedHost = Hosts.FirstOrDefault();
        var elevation = doc.ActiveView?.GenLevel?.Elevation ?? 0;
        _bottom = UnitFormatUtils.Format(doc.GetUnits(), SpecTypeId.Length, elevation, false);
        ExcavateCommand = new RelayCommand(() => Submit(() => Raise(_event)));
        Report("Bottom elevation uses project units. Slope 1:1.5 means 1 vertical to 1.5 horizontal.", false);
    }

    public IReadOnlyList<TerrainChoice> Hosts { get; }
    public RelayCommand ExcavateCommand { get; }

    public TerrainChoice? SelectedHost
    {
        get => _host;
        set => Set(ref _host, value);
    }

    public string BottomText
    {
        get => _bottom;
        set => Set(ref _bottom, value);
    }

    public string SlopeText
    {
        get => _slope;
        set => Set(ref _slope, value);
    }

    public ExcavateRequest Read(Document doc) => new()
    {
        HostId = SelectedHost?.Id ?? ElementId.InvalidElementId,
        BottomFeet = TerrainGeometry.ParseProjectLength(doc, BottomText, "Bottom elevation"),
        RunPerRise = TerrainGeometry.ParseSlope(SlopeText),
    };
}
