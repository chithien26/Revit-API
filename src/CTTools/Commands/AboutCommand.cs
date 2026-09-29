using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Licensing;

namespace CTTools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class AboutCommand : CommandBase
{
    protected override bool RequiresLicense => false;

    protected override Result Run(UIApplication uiApp)
    {
        var version = typeof(AboutCommand).Assembly.GetName().Version;
        TaskDialog.Show("CTTools",
            $"CTTools v{version?.ToString(3)}\n" +
            $"Revit {uiApp.Application.VersionNumber} ({uiApp.Application.VersionBuild})\n" +
            $"Autodesk account: {LicenseManager.GetSignedInAccount(uiApp.Application) ?? "(not signed in)"}");
        return Result.Succeeded;
    }
}
