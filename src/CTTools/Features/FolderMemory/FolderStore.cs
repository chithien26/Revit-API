using System.IO;
using System.Text.Json;

namespace CTTools.Features.FolderMemory;

public enum FileOperation
{
    Open,
    LoadFamily,
    LinkRevit,
    LinkCad,
    ImportCad,
    ExportCad,
}

/// <summary>
/// Last folder used per file operation, stored in %APPDATA%\CTTools\folders.json
/// </summary>
internal static class FolderStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CTTools", "folders.json");

    private static Dictionary<FileOperation, string>? _folders;

    public static IReadOnlyDictionary<FileOperation, string> All => Folders;

    private static Dictionary<FileOperation, string> Folders => _folders ??= Load();

    public static string? Get(FileOperation operation) =>
        Folders.TryGetValue(operation, out var folder) && Directory.Exists(folder) ? folder : null;

    public static void Set(FileOperation operation, string folder)
    {
        if (Folders.TryGetValue(operation, out var current) && string.Equals(current, folder, StringComparison.OrdinalIgnoreCase))
            return;
        Folders[operation] = folder;
        Save();
    }

    public static void Clear()
    {
        Folders.Clear();
        Save();
    }

    private static Dictionary<FileOperation, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<FileOperation, string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception)
        {
            // Corrupt file: start fresh
        }
        return [];
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Folders, new JsonSerializerOptions { WriteIndented = true }));
    }
}
