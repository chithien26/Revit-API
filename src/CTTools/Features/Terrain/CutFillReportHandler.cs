using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace CTTools.Features.Terrain;

public sealed class CutFillReportHandler : IExternalEventHandler
{
    public CutFillViewModel? Model { get; set; }

    public void Execute(UIApplication app)
    {
        var model = Model;
        if (model is null)
            return;
        try
        {
            var doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
            var (existingId, proposedId) = model.Read();
            var existing = TerrainGeometry.RequireSurface(doc, existingId, "existing surface");
            var proposed = TerrainGeometry.RequireSurface(doc, proposedId, "proposed surface");
            if (existing.Id == proposed.Id)
                throw new InvalidOperationException("Choose two different surfaces.");
            // Solid volume is the toposolid body. Cut and fill compare the top faces on a grid.
            var numbers = TerrainGeometry.Measure(existing, proposed);
            model.Show(numbers);
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

    public string GetName() => "CTTools Cut and Fill";
}

public sealed class CutFillViewModel : TerrainViewModel
{
    private readonly ExternalEvent _event;
    private TerrainChoice? _existing;
    private TerrainChoice? _proposed;
    private CutFillNumbers? _numbers;
    private bool _ready;
    private string _existingText = "";
    private string _proposedText = "";
    private string _cutText = "";
    private string _fillText = "";
    private string _netText = "";
    private string _gridText = "";

    public CutFillViewModel(Document doc, ExternalEvent externalEvent, CutFillReportHandler handler)
    {
        _event = externalEvent;
        handler.Model = this;
        Surfaces = TerrainGeometry.Surfaces(doc);
        SelectedExisting = Surfaces.FirstOrDefault();
        SelectedProposed = Surfaces.Skip(1).FirstOrDefault() ?? Surfaces.FirstOrDefault();
        MeasureCommand = new RelayCommand(() => Submit(() => Raise(_event)));
        ExportCommand = new RelayCommand(Export);
        Report("Compare the natural ground with the proposed ground.", false);
    }

    public IReadOnlyList<TerrainChoice> Surfaces { get; }
    public RelayCommand MeasureCommand { get; }
    public RelayCommand ExportCommand { get; }

    public TerrainChoice? SelectedExisting
    {
        get => _existing;
        set => Set(ref _existing, value);
    }

    public TerrainChoice? SelectedProposed
    {
        get => _proposed;
        set => Set(ref _proposed, value);
    }

    public bool HasResult
    {
        get => _ready;
        private set => Set(ref _ready, value);
    }

    public string ExistingText { get => _existingText; private set => Set(ref _existingText, value); }
    public string ProposedText { get => _proposedText; private set => Set(ref _proposedText, value); }
    public string CutText { get => _cutText; private set => Set(ref _cutText, value); }
    public string FillText { get => _fillText; private set => Set(ref _fillText, value); }
    public string NetText { get => _netText; private set => Set(ref _netText, value); }
    public string GridText { get => _gridText; private set => Set(ref _gridText, value); }

    public (ElementId Existing, ElementId Proposed) Read() =>
        (SelectedExisting?.Id ?? ElementId.InvalidElementId, SelectedProposed?.Id ?? ElementId.InvalidElementId);

    public void Show(CutFillNumbers numbers)
    {
        _numbers = numbers;
        var culture = CultureInfo.CurrentCulture;
        ExistingText = Format(numbers.ExistingSolidM3, culture);
        ProposedText = Format(numbers.ProposedSolidM3, culture);
        CutText = Format(numbers.CutM3, culture);
        FillText = Format(numbers.FillM3, culture);
        NetText = Format(numbers.CutM3 - numbers.FillM3, culture);
        GridText = $"{numbers.Samples} samples, cell {numbers.CellXMeters.ToString("0.##", culture)} × {numbers.CellYMeters.ToString("0.##", culture)} m";
        HasResult = true;
        Report("Cut is where the existing ground is higher. Net is cut minus fill.", false);
    }

    private void Export()
    {
        if (_numbers is null)
        {
            Report("Measure the two surfaces first.", true);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "cut-fill.csv",
            Title = "Export cut and fill",
        };
        if (dialog.ShowDialog() != true)
            return;
        TerrainGeometry.WriteReport(dialog.FileName, _numbers);
        Report($"Saved {dialog.FileName}", false);
    }

    private static string Format(double cubicMeters, CultureInfo culture) =>
        cubicMeters.ToString("N2", culture) + " m³";
}
