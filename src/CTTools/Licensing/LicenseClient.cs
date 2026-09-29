using System.Reflection;
using Npgsql;

namespace CTTools.Licensing;

public enum LicenseStatus
{
    Valid,
    Invalid,
    Error,
}

public record LicenseCheckResult(LicenseStatus Status, string? Error = null);

/// <summary>
/// Asks the license database whether an email is allowed. Only calls check_license(), see database/licenses.sql.
/// </summary>
internal static class LicenseClient
{
    private static readonly string? ConnectionString = BuildConnectionString();

    public static LicenseCheckResult Check(string email)
    {
        if (ConnectionString is null)
            return new LicenseCheckResult(LicenseStatus.Error, "This build has no license server configured.");

        try
        {
            using var connection = new NpgsqlConnection(ConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand("select check_license(@email)", connection);
            command.Parameters.AddWithValue("email", email);
            var licensed = command.ExecuteScalar() is true;
            return new LicenseCheckResult(licensed ? LicenseStatus.Valid : LicenseStatus.Invalid);
        }
        catch (Exception ex)
        {
            return new LicenseCheckResult(LicenseStatus.Error, ex.Message);
        }
    }

    private static string? BuildConnectionString()
    {
        var raw = typeof(LicenseClient).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LicenseConnectionString")?.Value;
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Neon computes scale to zero; the first connection after idle can take a few seconds.
        return new NpgsqlConnectionStringBuilder(raw) { Timeout = 20, CommandTimeout = 20 }.ConnectionString;
    }
}
