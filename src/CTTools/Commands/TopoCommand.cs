using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Features.Topo;

namespace CTTools.Commands;

[Transaction(TransactionMode.Manual)]
public class TopoCommand : CommandBase
{
    private static TopoWindow? _window;
    private static ExternalEvent? _event;
    private static TopoEventHandler? _handler;

    protected override Result Run(UIApplication uiApp)
    {
        if (_window is { IsLoaded: true })
        {
            if (_window.WindowState == System.Windows.WindowState.Minimized)
                _window.WindowState = System.Windows.WindowState.Normal;
            _window.Show();
            _window.Activate();
            return Result.Succeeded;
        }

        _handler = new TopoEventHandler();
        _event = ExternalEvent.Create(_handler);
        _window = new TopoWindow(uiApp, _event, _handler);
        _window.Closed += (_, _) =>
        {
            _event?.Dispose();
            _event = null;
            _handler = null;
            _window = null;
        };
        _window.Show();
        return Result.Succeeded;
    }
}
