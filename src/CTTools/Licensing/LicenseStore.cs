using System.IO;
using System.Text.Json;

namespace CTTools.Licensing;

public class LicenseState
{
    public string? Email { get; set; }
    public DateTime? LastVerifiedUtc { get; set; }
}

/// <summary>
/// Remembers the activated email on this machine: %APPDATA%\CTTools\license.json
/// </summary>
internal static class LicenseStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CTTools", "license.json");

    public static LicenseState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LicenseState>(File.ReadAllText(FilePath)) ?? new LicenseState();
        }
        catch (Exception)
        {
            // Corrupt file: treat as not activated
        }
        return new LicenseState();
    }

    public static void Save(LicenseState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(state));
    }
}
