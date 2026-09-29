using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Licensing;

namespace CTTools.Commands;

/// <summary>
/// Minimal licensed command: if this dialog appears, the license check passed.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class LicenseTestCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var account = LicenseManager.GetSignedInAccount(uiApp.Application);
        var document = uiApp.ActiveUIDocument?.Document.Title ?? "(no document open)";

        var dialog = new TaskDialog("Test License")
        {
            MainInstruction = "License OK - this tool is running.",
            MainContent = $"Account: {account}\nDocument: {document}",
            MainIcon = TaskDialogIcon.TaskDialogIconInformation,
        };
        dialog.Show();
        return Result.Succeeded;
    }
}
