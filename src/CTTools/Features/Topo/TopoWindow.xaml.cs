using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfCombo = System.Windows.Controls.ComboBox;

namespace CTTools.Features.Topo;

public partial class TopoWindow : Window
{
    private readonly UIApplication _uiApp;
    private readonly ExternalEvent _event;
    private readonly TopoEventHandler _handler;
    private Document? _loaded;
    private bool _busy;

    public TopoWindow(UIApplication uiApp, ExternalEvent externalEvent, TopoEventHandler handler)
    {
        _uiApp = uiApp;
        _event = externalEvent;
        _handler = handler;
        InitializeComponent();
        new System.Windows.Interop.WindowInteropHelper(this).Owner = uiApp.MainWindowHandle;
        Loaded += (_, _) => LoadDocument();
    }

    public bool IsBusy => _busy;

    public void Finish(string message, bool error)
    {
        _busy = false;
        RunButton.IsEnabled = true;
        Status.Text = message;
        Status.Foreground = error ? System.Windows.Media.Brushes.DarkRed : new SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 90, 100));
        if (IsLoaded)
            Show();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!_busy)
            LoadDocument();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_busy)
            e.Cancel = true;
        base.OnClosing(e);
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
        SlopeEndBox.Text = FormatLength(units, elevation);
        RadiusBox.Text = FormatLength(units, UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Meters));
        GainBox.Text = FormatLength(units, UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters));
        ModeChanged(this, new RoutedEventArgs());
    }

    private void ModeChanged(object sender, RoutedEventArgs e)
    {
        if (ElevationPanel is null)
            return;

        var mode = SelectedMode();
        ElevationPanel.Visibility = mode is TopoMode.NewPad or TopoMode.AddPoints ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        OffsetBox.Visibility = mode == TopoMode.AddPoints ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        ShapePanel.Visibility = mode == TopoMode.ShapeByPoint ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SmoothPanel.Visibility = mode == TopoMode.Smooth ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        SlopePanel.Visibility = mode == TopoMode.SlopePath ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        Hint.Text = mode switch
        {
            TopoMode.NewPad => "In a plan view, pick two opposite corners. The pad is flat at Elevation.",
            TopoMode.NewFromLines => "Select model lines drawn at their contour height, then click Finish. Detail lines are ignored.",
            TopoMode.FromEdges => "Select walls, floors, roofs, or model lines, then click Finish. The new surface passes through their top edges.",
            TopoMode.AddPoints => "Pick a surface, then click inside it. Esc ends the command. Uncheck the offset box to use a fixed elevation.",
            TopoMode.AddLines => "Pick a surface, then select model lines to stamp on it. Their height becomes the new ground height.",
            TopoMode.ShapeByPoint => "Pick a surface, then click where it should change. Each click applies Height per click at the center, fading to zero at Radius.",
            TopoMode.Smooth => "Pick a surface. Interior points move toward their neighbors. More passes look smoother and follow the original contours less closely.",
            TopoMode.InsertMidpoint => "Pick a surface, then two spots. A point is inserted halfway between them so the slope can be edited.",
            TopoMode.SlopePath => "Pick a surface, then the start and the end. Ground along that line is pulled to the end elevation or the slope angle.",
            _ => "",
        };
    }

    private void ShapeChanged(object sender, RoutedEventArgs e)
    {
        if (GainBox is null)
            return;
        GainBox.IsEnabled = ShapeSmooth?.IsChecked != true;
    }

    private void SlopeChanged(object sender, RoutedEventArgs e)
    {
        if (SlopeEndBox is null)
            return;
        var byAngle = SlopeAngle.IsChecked == true;
        SlopeEndBox.IsEnabled = !byAngle;
        SlopeDegreesBox.IsEnabled = byAngle;
    }

    private void RunClick(object sender, RoutedEventArgs e)
    {
        LoadDocument();
        var doc = _uiApp.ActiveUIDocument?.Document;
        if (doc is null)
        {
            Report("Open a project first.", error: true);
            return;
        }

        var mode = SelectedMode();
        var creates = mode is TopoMode.NewPad or TopoMode.NewFromLines or TopoMode.FromEdges;
        TryChoice(LevelBox, out var levelId);
        TryChoice(TypeBox, out var typeId);
        if (creates && (levelId == ElementId.InvalidElementId || typeId == ElementId.InvalidElementId))
        {
            Report("This project needs a level and a toposolid type before a new surface can be created.", error: true);
            return;
        }

        var units = doc.GetUnits();
        double elevation = 0, radius = 1, gain = 0, slopeEnd = 0, degrees = 0;
        if (mode is TopoMode.NewPad or TopoMode.AddPoints)
        {
            if (!TryLength(units, ElevationBox.Text, out elevation))
            {
                Report("Elevation is not a length. Example: 5000 or 5 m.", error: true);
                return;
            }
        }

        if (mode == TopoMode.ShapeByPoint)
        {
            if (!TryLength(units, RadiusBox.Text, out radius) || radius <= 0)
            {
                Report("Radius is not a length. Example: 20000 or 20 m.", error: true);
                return;
            }

            var smoothOut = ShapeSmooth.IsChecked == true;
            if (!smoothOut && (!TryLength(units, GainBox.Text, out gain) || gain < 0))
            {
                Report("Height per click is not a length. Example: 1000 or 1 m.", error: true);
                return;
            }
        }

        if (!int.TryParse(RingsBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var rings))
            rings = 5;
        if (!int.TryParse(PassesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var passes))
            passes = 2;

        if (mode == TopoMode.SlopePath)
        {
            if (SlopeAngle.IsChecked == true)
            {
                if (!TryDegrees(SlopeDegreesBox.Text, out degrees))
                {
                    Report("Slope angle must be a number of degrees, for example -15.", error: true);
                    return;
                }
            }
            else if (!TryLength(units, SlopeEndBox.Text, out slopeEnd))
            {
                Report("End elevation is not a length.", error: true);
                return;
            }
        }

        var request = new TopoRequest
        {
            Mode = mode,
            LevelId = levelId,
            TypeId = typeId,
            Elevation = elevation,
            ElevationIsOffset = OffsetBox.IsChecked == true,
            Radius = radius,
            Rings = Math.Clamp(rings, 1, 12),
            Gain = gain,
            Shape = ShapeLower.IsChecked == true ? ShapeAction.Lower
                : ShapeSmooth.IsChecked == true ? ShapeAction.SmoothOut
                : ShapeAction.Raise,
            SmoothPasses = Math.Clamp(passes, 1, 4),
            SlopeByAngle = SlopeAngle.IsChecked == true,
            SlopeDegrees = degrees,
            SlopeEndElevation = slopeEnd,
        };

        _busy = true;
        RunButton.IsEnabled = false;
        Status.Text = "Working in Revit. Follow the status bar. Press Esc to finish a click tool.";
        _handler.Request = request;
        _handler.Window = this;
        if (_event.Raise() != ExternalEventRequest.Accepted)
            Finish("Revit is busy. Try again in a moment.", error: true);
    }

    private TopoMode SelectedMode()
    {
        if (ModeLines.IsChecked == true) return TopoMode.NewFromLines;
        if (ModeEdges.IsChecked == true) return TopoMode.FromEdges;
        if (ModePoints.IsChecked == true) return TopoMode.AddPoints;
        if (ModeAddLines.IsChecked == true) return TopoMode.AddLines;
        if (ModeShape.IsChecked == true) return TopoMode.ShapeByPoint;
        if (ModeSmooth.IsChecked == true) return TopoMode.Smooth;
        if (ModeMid.IsChecked == true) return TopoMode.InsertMidpoint;
        if (ModeSlope.IsChecked == true) return TopoMode.SlopePath;
        return TopoMode.NewPad;
    }

    private void Report(string message, bool error)
    {
        Status.Text = message;
        Status.Foreground = error ? System.Windows.Media.Brushes.DarkRed : new SolidColorBrush(System.Windows.Media.Color.FromRgb(69, 90, 100));
    }

    private static bool TryChoice(WpfCombo box, out ElementId id)
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
    public TopoRequest? Request { get; set; }
    public TopoWindow? Window { get; set; }

    public void Execute(UIApplication app)
    {
        var request = Request;
        Request = null;
        string? message = null;
        var failed = false;
        try
        {
            Window?.Hide();
            if (request is not null)
                TopoEditor.Run(app, request);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            message = "Cancelled.";
        }
        catch (Exception ex)
        {
            message = ex.Message;
            failed = true;
        }
        finally
        {
            Window?.Finish(message ?? request?.Result ?? "", failed);
        }
    }

    public string GetName() => "CTTools Topo";
}
