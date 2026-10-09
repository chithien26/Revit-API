using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Features.Terrain;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class CreateToposolidCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        if (TerrainHost.TryActivate<ToposolidFromSourceView>())
            return Result.Succeeded;
        var doc = uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
        var handler = new ToposolidCreateHandler();
        var externalEvent = ExternalEvent.Create(handler);
        var view = new ToposolidFromSourceView(new ToposolidFromSourceViewModel(doc, externalEvent, handler));
        Show(uiApp, view, externalEvent);
        return Result.Succeeded;
    }

    internal static void Show(UIApplication uiApp, System.Windows.Window view, ExternalEvent externalEvent)
    {
        new WindowInteropHelper(view).Owner = uiApp.MainWindowHandle;
        TerrainHost.Show(view, externalEvent);
    }
}

[Transaction(TransactionMode.Manual)]
public class CreateRoadCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        if (TerrainHost.TryActivate<RoadView>())
            return Result.Succeeded;
        var doc = uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
        var handler = new CreateRoadHandler();
        var externalEvent = ExternalEvent.Create(handler);
        var view = new RoadView(new RoadViewModel(doc, externalEvent, handler));
        handler.Window = view;
        CreateToposolidCommand.Show(uiApp, view, externalEvent);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class ExcavateCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        if (TerrainHost.TryActivate<ExcavateView>())
            return Result.Succeeded;
        var doc = uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
        var handler = new ExcavateHandler();
        var externalEvent = ExternalEvent.Create(handler);
        var view = new ExcavateView(new ExcavateViewModel(doc, externalEvent, handler));
        handler.Window = view;
        CreateToposolidCommand.Show(uiApp, view, externalEvent);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class AlignToTopoCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        if (TerrainHost.TryActivate<AlignView>())
            return Result.Succeeded;
        var doc = uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
        var handler = new AlignToTopoHandler();
        var externalEvent = ExternalEvent.Create(handler);
        var view = new AlignView(new AlignViewModel(doc, externalEvent, handler));
        handler.Window = view;
        CreateToposolidCommand.Show(uiApp, view, externalEvent);
        return Result.Succeeded;
    }
}

[Transaction(TransactionMode.Manual)]
public class CutFillReportCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        if (TerrainHost.TryActivate<CutFillReportView>())
            return Result.Succeeded;
        var doc = uiApp.ActiveUIDocument?.Document ?? throw new InvalidOperationException("Open a project first.");
        var handler = new CutFillReportHandler();
        var externalEvent = ExternalEvent.Create(handler);
        var view = new CutFillReportView(new CutFillViewModel(doc, externalEvent, handler));
        CreateToposolidCommand.Show(uiApp, view, externalEvent);
        return Result.Succeeded;
    }
}
