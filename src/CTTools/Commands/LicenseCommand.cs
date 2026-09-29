using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Licensing;

namespace CTTools.Commands;

/// <summary>
/// Re-checks the license on demand, e.g. right after the vendor adds the user's account to the database.
/// </summary>
[Transaction(TransactionMode.ReadOnly)]
public class LicenseCommand : CommandBase
{
    protected override bool RequiresLicense => false;

    protected override Result Run(UIApplication uiApp)
    {
        var account = LicenseManager.GetSignedInAccount(uiApp.Application);
        var dialog = new TaskDialog("CTTools License");

        if (account is null)
        {
            dialog.MainInstruction = "Not signed in";
            dialog.MainContent = "Please sign in to your Autodesk account in Revit to use CTTools.";
            dialog.MainIcon = TaskDialogIcon.TaskDialogIconWarning;
        }
        else
        {
            var result = LicenseManager.Verify(account);
            var valid = result.Status == LicenseStatus.Valid;
            dialog.MainInstruction = valid ? "License active" : result.Error;
            dialog.MainContent = $"Signed-in account: {account}";
            dialog.MainIcon = valid ? TaskDialogIcon.TaskDialogIconInformation : TaskDialogIcon.TaskDialogIconWarning;
        }

        dialog.Show();
        return Result.Succeeded;
    }
}
