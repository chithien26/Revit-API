using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Topo;

public partial class TopoWindow : Window
{
    private readonly UIApplication _uiApp;
    private readonly TopoEventHandler _handler;
    private readonly TopoSession _session;
    private Document? _loaded;

    public TopoWindow(UIApplication uiApp, ExternalEvent externalEvent, TopoEventHandler handler)
    {
        _uiApp = uiApp;
        _handler = handler;
        InitializeComponent();
        _session = new TopoSession(uiApp, this, externalEvent, handler);
        handler.Session = _session;
        new System.Windows.Interop.WindowInteropHelper(this).Owner = uiApp.MainWindowHandle;
        Loaded += (_, _) =>
        {
            LoadDocument();
            Left = SystemParameters.WorkArea.Right - ActualWidth - 18;
            Top = SystemParameters.WorkArea.Top + 36;
            _session.Start();
            ShowTools();
        };
        Closed += (_, _) => _session.Stop();
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
            return false;
        var origin = PointToScreen(new System.Windows.Point(0, 0));
        var far = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
        return x >= Math.Min(origin.X, far.X) && x <= Math.Max(origin.X, far.X)
            && y >= Math.Min(origin.Y, far.Y) && y <= Math.Max(origin.Y, far.Y);
    }

    public void SetSurface(string name) => SurfaceName.Text = name;

    public void SetStatus(string message, bool error)
    {
        Status.Text = message;
        Status.Foreground = error
            ? System.Windows.Media.Brushes.DarkRed
            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(55, 71, 79));
    }

    public bool TryRead(out TopoSettings settings)
    {
        settings = new TopoSettings
        {
            Tool = TopoTool.Shape,
            LevelId = ElementId.InvalidElementId,
            TypeId = ElementId.InvalidElementId,
        };
        var doc = _uiApp.ActiveUIDocument?.Document;
        if (doc is null)
            return false;

        var units = doc.GetUnits();
        var tool = CurrentTool();
        if (!TryLength(units, SizeBox.Text, out var size) || size <= 0)
        {
            if (tool == TopoTool.Shape)
            {
                SetStatus("Shape size is not a length. Example: 20000 or 20 m.", true);
                return false;
            }

            size = UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Meters);
        }

        if (!TryLength(units, GainBox.Text, out var gain))
        {
            if (tool is TopoTool.Shape or TopoTool.Mesh)
            {
                SetStatus("Gain is not a length. Example: 1000 or 1 m.", true);
                return false;
            }

            gain = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters);
        }

        if (!TryLength(units, CellBox.Text, out var cell) || cell <= 0)
            cell = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Meters);
        var elevationBox = tool == TopoTool.Pad ? PadElevationBox.Text : ElevationBox.Text;
        if (!TryLength(units, elevationBox, out var elevation))
            elevation = 0;
        if (!TryLength(units, SlopeEndBox.Text, out var slopeEnd))
            slopeEnd = elevation;
        if (!TryDegrees(SlopeDegreesBox.Text, out var degrees))
            degrees = -15;
        TryChoice(LevelBox, out var levelId);
        TryChoice(TypeBox, out var typeId);

        settings = new TopoSettings
        {
            Tool = tool,
            Shape = ShapeLower.IsChecked == true ? ShapeAction.Lower
                : ShapeSmoothOut.IsChecked == true ? ShapeAction.SmoothOut
                : ShapeAction.Raise,
            OuterRing = size,
            Density = (int)Math.Round(DensitySlider.Value),
            Gain = Math.Abs(gain),
            SmoothPasses = (int)Math.Round(PassSlider.Value),
            InflatePercent = InflateSlider.Value,
            Cell = cell,
            Rotation = RotationSlider.Value,
            RelativePoint = RelativeBox.IsChecked == true,
            Elevation = elevation,
            SlopeByAngle = SlopeByAngle.IsChecked == true,
            SlopeDegrees = degrees,
            SlopeEnd = slopeEnd,
            LevelId = levelId,
            TypeId = typeId,
        };
        return true;
    }

    private void LoadDocument()
    {
        var doc = _uiApp.ActiveUIDocument?.Document;
        if (doc is null || ReferenceEquals(doc, _loaded))
            return;
        _loaded = doc;
        var units = doc.GetUnits();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(level => level.Elevation)
            .Select(level => new IdChoice(level.Id, level.Name))
            .ToList();
        var types = new FilteredElementCollector(doc).OfClass(typeof(ToposolidType)).Cast<ToposolidType>()
            .OrderBy(type => type.Name)
            .Select(type => new IdChoice(type.Id, type.Name))
            .ToList();
        LevelBox.ItemsSource = levels;
        TypeBox.ItemsSource = types;
        var viewLevel = doc.ActiveView?.GenLevel;
        LevelBox.SelectedItem = levels.FirstOrDefault(item => item.Id == viewLevel?.Id) ?? levels.FirstOrDefault();
        TypeBox.SelectedItem = types.FirstOrDefault();
        var elevation = viewLevel?.Elevation ?? 0;
        ElevationBox.Text = FormatLength(units, elevation);
        PadElevationBox.Text = FormatLength(units, elevation);
        SlopeEndBox.Text = FormatLength(units, elevation);
        SizeBox.Text = FormatLength(units, UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Meters));
        GainBox.Text = FormatLength(units, UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters));
        CellBox.Text = FormatLength(units, UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Meters));
    }

    private void ShowTools()
    {
        if (ModifyTools is null)
            return;
        var modify = PageModify.IsChecked == true;
        var points = PagePoints.IsChecked == true;
        var create = PageCreate.IsChecked == true;
        var tool = CurrentTool();
        ModifyTools.Visibility = modify ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PointTools.Visibility = points ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PointActions.Visibility = points ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        CreateOptions.Visibility = create ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ShapeOptions.Visibility = tool is TopoTool.Shape or TopoTool.Mesh ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SmoothOptions.Visibility = tool == TopoTool.Smooth ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        InflateOptions.Visibility = tool == TopoTool.Inflate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        EffectButtons.Visibility = tool is TopoTool.Smooth or TopoTool.Inflate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        GridOptions.Visibility = tool is TopoTool.Smooth or TopoTool.Inflate or TopoTool.Mesh ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        PointOptions.Visibility = tool == TopoTool.Point ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SlopeOptions.Visibility = tool == TopoTool.Slope ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Hint.Text = tool switch
        {
            TopoTool.Shape => "Click the surface. Each click raises or lowers the center by Gain, fading out to the blue ring. Shape size is that outer ring.",
            TopoTool.Smooth => "Smoothing level 1 stays closer to the contours. Level 4 is softer. Preview, then Apply.",
            TopoTool.Inflate => "Negative digs a basin, positive raises a dome. The blue grid is the cell size used to shape it.",
            TopoTool.Mesh => "The grid shows the edit spacing. Click near a point to push it by Gain.",
            TopoTool.Point => "Click the surface to drop a point. Relative adds the elevation to the ground there. Otherwise it is a fixed height.",
            TopoTool.Mid => "Click two spots. A point is inserted halfway between them.",
            TopoTool.Slope => "Click the start, then the end. Ground along that line follows the end elevation or the slope angle.",
            TopoTool.Split => "Click two spots to crease the surface between them.",
            TopoTool.Pad => "In a plan view, click two opposite corners.",
            _ => "",
        };
    }

    private TopoTool CurrentTool()
    {
        if (PageCreate?.IsChecked == true)
            return TopoTool.Pad;
        if (PagePoints?.IsChecked == true)
        {
            if (ToolMid.IsChecked == true) return TopoTool.Mid;
            if (ToolSlope.IsChecked == true) return TopoTool.Slope;
            if (ToolSplit.IsChecked == true) return TopoTool.Split;
            return TopoTool.Point;
        }

        if (ToolSmooth?.IsChecked == true) return TopoTool.Smooth;
        if (ToolInflate?.IsChecked == true) return TopoTool.Inflate;
        if (ToolMesh?.IsChecked == true) return TopoTool.Mesh;
        return TopoTool.Shape;
    }

    private void PageChanged(object sender, RoutedEventArgs e) => ShowTools();
    private void ToolChanged(object sender, RoutedEventArgs e) => ShowTools();
    private void PickClick(object sender, RoutedEventArgs e) => _session.PickSurface();
    private void AddLinesClick(object sender, RoutedEventArgs e) => _session.AddLines();
    private void AddEdgesClick(object sender, RoutedEventArgs e) => _session.AddEdges();
    private void CreateLinesClick(object sender, RoutedEventArgs e) => _session.CreateFromLines();
    private void CreateEdgesClick(object sender, RoutedEventArgs e) => _session.CreateFromEdges();
    private void PreviewClick(object sender, RoutedEventArgs e) => _session.PreviewEffect();
    private void ApplyClick(object sender, RoutedEventArgs e) => _session.ApplyEffect();
    private void UndoClick(object sender, RoutedEventArgs e) => _session.Undo();
    private void RevertClick(object sender, RoutedEventArgs e) => _session.Revert();

    private void DensityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DensityLabel is not null)
            DensityLabel.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture);
    }

    private void PassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PassLabel is not null)
            PassLabel.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture);
    }

    private void InflateChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InflateLabel is not null)
            InflateLabel.Text = ((int)Math.Round(e.NewValue)).ToString(CultureInfo.CurrentCulture);
    }

    private void RotationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RotationLabel is not null)
            RotationLabel.Text = $"{(int)Math.Round(e.NewValue)}°";
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        LoadDocument();
    }

    private static bool TryChoice(System.Windows.Controls.ComboBox box, out ElementId id)
    {
        if (box.SelectedItem is IdChoice choice)
        {
            id = choice.Id;
            return true;
        }

        id = ElementId.InvalidElementId;
        return false;
    }

    private static bool TryLength(Units units, string text, out double feet) =>
        UnitFormatUtils.TryParse(units, SpecTypeId.Length, text.Trim(), out feet);

    private static bool TryDegrees(string text, out double degrees) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out degrees)
        || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);

    private static string FormatLength(Units units, double feet) =>
        UnitFormatUtils.Format(units, SpecTypeId.Length, feet, false);

    private sealed class IdChoice(ElementId id, string name)
    {
        public ElementId Id { get; } = id;
        public override string ToString() => name;
    }
}

public sealed class TopoEventHandler : IExternalEventHandler
{
    public Action<UIApplication>? Work { get; set; }
    public TopoSession? Session { get; set; }

    public void Execute(UIApplication app)
    {
        var work = Work;
        Work = null;
        string? message = null;
        try
        {
            work?.Invoke(app);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            message = "Cancelled.";
        }
        catch (Exception ex)
        {
            message = ex.Message;
        }
        finally
        {
            Session?.OnApiFinished(message);
        }
    }

    public string GetName() => "CTTools Topo";
}
