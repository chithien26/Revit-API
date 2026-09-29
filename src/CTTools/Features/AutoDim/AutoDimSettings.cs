using System.IO;
using System.Text.Json;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Auto Dim choices remembered between runs, stored in %APPDATA%\CTTools\autodim.json
/// </summary>
internal static class AutoDimSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CTTools", "autodim.json");

    private sealed record Data(string? DimensionTypeName);

    public static string? DimensionTypeName
    {
        get => Load().DimensionTypeName;
        set => Save(new Data(value));
    }

    private static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(null);
        }
        catch (Exception)
        {
            // Corrupt file: start fresh
        }
        return new Data(null);
    }

    private static void Save(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }
}
