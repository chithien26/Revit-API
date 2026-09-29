using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.UI;

namespace CTTools.Licensing;

public static class LicenseManager
{
    /// <summary>How long a previously verified account keeps working when the license server can't be reached.</summary>
    private static readonly TimeSpan OfflineGracePeriod = TimeSpan.FromDays(7);

    private static string? _verifiedAccount;
    private static string? _quietCheckedAccount;
    private static bool _quietCheckResult;

    /// <summary>
    /// The Autodesk account signed in to Revit (usually the email), or null when signed out.
    /// While signed in, Revit locks Username to the Autodesk account, so users can't type someone else's email.
    /// </summary>
    public static string? GetSignedInAccount(Application app)
    {
        if (!Application.IsLoggedIn || string.IsNullOrWhiteSpace(app.Username))
            return null;
        return app.Username.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Verifies the signed-in Autodesk account once per Revit session (again if the user switches account).
    /// </summary>
    public static bool EnsureLicensed(Application app)
    {
        var account = GetSignedInAccount(app);
        if (account is null)
        {
            ShowDialog("Please sign in to your Autodesk account in Revit to use CTTools.");
            return false;
        }

        if (account == _verifiedAccount)
            return true;

        var result = Verify(account);
        if (result.Status != LicenseStatus.Valid)
            ShowDialog(result.Error!, account);
        return result.Status == LicenseStatus.Valid;
    }

    /// <summary>
    /// License check for background features: never shows UI, and asks the server at most once per session and account.
    /// </summary>
    public static bool IsLicensedQuietly(Application app)
    {
        var account = GetSignedInAccount(app);
        if (account is null)
            return false;
        if (account == _verifiedAccount)
            return true;
        if (account == _quietCheckedAccount)
            return _quietCheckResult;

        _quietCheckedAccount = account;
        _quietCheckResult = Verify(account).Status == LicenseStatus.Valid;
        return _quietCheckResult;
    }

    /// <summary>Always asks the server, ignoring the session cache.</summary>
    public static LicenseCheckResult Verify(string account)
    {
        var result = LicenseClient.Check(account);
        switch (result.Status)
        {
            case LicenseStatus.Valid:
                LicenseStore.Save(new LicenseState { Email = account, LastVerifiedUtc = DateTime.UtcNow });
                _verifiedAccount = account;
                return result;

            case LicenseStatus.Invalid:
                _verifiedAccount = null;
                return result with { Error = "This Autodesk account does not have an active CTTools license." };

            default:
                var state = LicenseStore.Load();
                if (state.Email == account && state.LastVerifiedUtc > DateTime.UtcNow - OfflineGracePeriod)
                {
                    _verifiedAccount = account;
                    return new LicenseCheckResult(LicenseStatus.Valid);
                }
                return result with { Error = $"Could not reach the license server: {result.Error}" };
        }
    }

    private static void ShowDialog(string message, string? account = null)
    {
        var dialog = new TaskDialog("CTTools License")
        {
            MainInstruction = message,
            MainContent = account is null ? null : $"Signed-in account: {account}\nSend this account to the vendor to get access.",
            MainIcon = TaskDialogIcon.TaskDialogIconWarning,
        };
        dialog.Show();
    }
}
