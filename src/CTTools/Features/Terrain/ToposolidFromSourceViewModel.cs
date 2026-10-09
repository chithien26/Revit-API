using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace CTTools.Features.Terrain;

public sealed class SourceRequest
{
    public bool FromFile { get; init; }
    public ElementId ImportId { get; init; } = ElementId.InvalidElementId;
    public string Layers { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool Millimeters { get; init; }
    public ElementId TypeId { get; init; } = ElementId.InvalidElementId;
    public ElementId LevelId { get; init; } = ElementId.InvalidElementId;
}

public sealed class ToposolidFromSourceViewModel : TerrainViewModel
{
    private readonly ExternalEvent _event;
    private bool _file;
    private string _layers = "CONTOUR, POINTS";
    private string _filePath = "";
    private string _unit = "Meters";
    private TerrainChoice? _import;
    private TerrainChoice? _type;
    private TerrainChoice? _level;

    public ToposolidFromSourceViewModel(Document doc, ExternalEvent externalEvent, ToposolidCreateHandler handler)
    {
        _event = externalEvent;
        handler.Model = this;
        Imports = TerrainGeometry.Imports(doc);
        Types = TerrainGeometry.Types(doc);
        Levels = TerrainGeometry.Levels(doc);
        SelectedImport = Imports.FirstOrDefault();
        SelectedType = Types.FirstOrDefault();
        SelectedLevel = Levels.FirstOrDefault(item => item.Id == doc.ActiveView?.GenLevel?.Id) ?? Levels.FirstOrDefault();
        Units = new[] { "Meters", "Millimeters" };
        BrowseCommand = new RelayCommand(Browse);
        CreateCommand = new RelayCommand(() => Submit(Create));
        Report(Imports.Count == 0
            ? "No CAD import in this project. You can still use a point file."
            : "CAD points stay in project coordinates. A point file is converted from meters or millimeters.", false);
    }

    public IReadOnlyList<TerrainChoice> Imports { get; }
    public IReadOnlyList<TerrainChoice> Types { get; }
    public IReadOnlyList<TerrainChoice> Levels { get; }
    public IReadOnlyList<string> Units { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand CreateCommand { get; }

    public bool IsFileMode
    {
        get => _file;
        set
        {
            if (!Set(ref _file, value))
                return;
            Raise(nameof(IsCadMode));
        }
    }

    public bool IsCadMode
    {
        get => !_file;
        set
        {
            if (value != _file)
                return;
            IsFileMode = !value;
        }
    }

    public string Layers
    {
        get => _layers;
        set => Set(ref _layers, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => Set(ref _filePath, value);
    }

    public string UnitName
    {
        get => _unit;
        set => Set(ref _unit, value);
    }

    public TerrainChoice? SelectedImport
    {
        get => _import;
        set => Set(ref _import, value);
    }

    public TerrainChoice? SelectedType
    {
        get => _type;
        set => Set(ref _type, value);
    }

    public TerrainChoice? SelectedLevel
    {
        get => _level;
        set => Set(ref _level, value);
    }

    public SourceRequest Read() => new()
    {
        FromFile = IsFileMode,
        ImportId = SelectedImport?.Id ?? ElementId.InvalidElementId,
        Layers = Layers,
        FilePath = FilePath,
        Millimeters = UnitName == "Millimeters",
        TypeId = SelectedType?.Id ?? ElementId.InvalidElementId,
        LevelId = SelectedLevel?.Id ?? ElementId.InvalidElementId,
    };

    private void Browse()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Point files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
            Title = "Point file",
        };
        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
            IsFileMode = true;
        }
    }

    private void Create()
    {
        _ = Read();
        Raise(_event);
    }
}
