using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Terrain;

public sealed class AlignViewModel : TerrainViewModel
{
    private readonly ExternalEvent _event;
    private TerrainChoice? _host;
    private string _offset = "0";

    public AlignViewModel(Document doc, ExternalEvent externalEvent, AlignToTopoHandler handler)
    {
        _event = externalEvent;
        handler.Model = this;
        Hosts = TerrainGeometry.Surfaces(doc);
        SelectedHost = Hosts.FirstOrDefault();
        AlignCommand = new RelayCommand(() => Submit(() => Raise(_event)));
        Report("Select the trees and lights first, or you will be asked to pick them. Offset is in millimeters.", false);
    }

    public IReadOnlyList<TerrainChoice> Hosts { get; }
    public RelayCommand AlignCommand { get; }

    public TerrainChoice? SelectedHost
    {
        get => _host;
        set => Set(ref _host, value);
    }

    public string OffsetText
    {
        get => _offset;
        set => Set(ref _offset, value);
    }

    public (ElementId HostId, double OffsetFeet) Read(Document doc) =>
        (SelectedHost?.Id ?? ElementId.InvalidElementId, TerrainGeometry.ParseMillimeterEntry(doc, OffsetText, "Offset"));
}
