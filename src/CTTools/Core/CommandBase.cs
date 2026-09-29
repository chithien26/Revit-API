using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CTTools.Licensing;

namespace CTTools.Core;

/// <summary>
/// Base class for all commands: enforces the license check and handles user cancellation and
/// unexpected errors in one place. Derived classes still need their own [Transaction] attribute.
/// </summary>
public abstract class CommandBase : IExternalCommand
{
    /// <summary>Override with false only for commands that must work without a license (About, License).</summary>
    protected virtual bool RequiresLicense => true;

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var uiApp = commandData.Application;
            if (RequiresLicense && !LicenseManager.EnsureLicensed(uiApp.Application))
                return Result.Cancelled;

            return Run(uiApp);
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException)
        {
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    protected abstract Result Run(UIApplication uiApp);
}
