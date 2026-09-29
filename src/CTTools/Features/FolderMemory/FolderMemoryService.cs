using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CTTools.Licensing;

namespace CTTools.Features.FolderMemory;

/// <summary>
/// Makes Revit remember a separate "last folder" for each file operation (Open, Load Family, Link, Import, Export).
/// When one of those commands starts, its file dialog is sent to that operation's folder; when the user picks a file,
/// the folder is remembered via Revit events.
/// </summary>
public static class FolderMemoryService
{
    /// <summary>How long after a command starts its file events still count for that operation.</summary>
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private static readonly List<AddInCommandBinding> Bindings = [];
    private static Application? _app;
    private static FileOperation? _pending;
    private static DateTime _pendingSince;

    /// <summary>Commands that could not be hooked, usually because another add-in already bound them.</summary>
    public static List<string> UnavailableCommands { get; } = [];

    public static void Register(UIControlledApplication application)
    {
        foreach (var (operation, name, commandId) in GetCommands())
        {
            if (commandId is null || !commandId.CanHaveBinding)
            {
                UnavailableCommands.Add(name);
                continue;
            }

            try
            {
                var binding = application.CreateAddInCommandBinding(commandId);
                binding.BeforeExecuted += (sender, _) => OnCommandStarting(sender, operation);
                Bindings.Add(binding);
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                UnavailableCommands.Add(name);
            }
        }

        var events = application.ControlledApplication;
        events.ApplicationInitialized += (sender, _) => _app ??= sender as Application;
        events.DocumentOpened += OnDocumentOpened;
        events.FamilyLoadingIntoDocument += (_, e) => Remember(e.FamilyPath, FileOperation.LoadFamily);
        events.FileImporting += (_, e) => Remember(e.Path, FileOperation.ImportCad, FileOperation.LinkCad);
        events.FileExporting += (_, e) => Remember(e.Path, FileOperation.ExportCad);
        events.LinkedResourceOpened += (_, e) => Remember(e.LinkedResourcePathName, FileOperation.LinkRevit, FileOperation.LinkCad);
        events.DocumentChanged += OnDocumentChanged;
    }

    private static IEnumerable<(FileOperation Operation, string Name, RevitCommandId? Id)> GetCommands()
    {
        yield return (FileOperation.Open, "Open", Postable(PostableCommand.OpenRevitFile));
        yield return (FileOperation.Open, "Open Project", Postable(PostableCommand.OpenProject));
        yield return (FileOperation.Open, "Open Family", Postable(PostableCommand.OpenFamily));
        yield return (FileOperation.LoadFamily, "Load Family", RevitCommandId.LookupCommandId("ID_FAMILY_LOAD"));
        yield return (FileOperation.LinkRevit, "Link Revit", Postable(PostableCommand.LinkRevit));
        yield return (FileOperation.LinkCad, "Link CAD", Postable(PostableCommand.LinkCAD));
        yield return (FileOperation.ImportCad, "Import CAD", Postable(PostableCommand.ImportCAD));
        yield return (FileOperation.ExportCad, "Export DWG", Postable(PostableCommand.ExportCADFormatsDWG));
        yield return (FileOperation.ExportCad, "Export DXF", Postable(PostableCommand.ExportCADFormatsDXF));
        yield return (FileOperation.ExportCad, "Export DGN", Postable(PostableCommand.ExportCADFormatsDGN));

        static RevitCommandId? Postable(PostableCommand command) => RevitCommandId.LookupPostableCommandId(command);
    }

    private static void OnCommandStarting(object? sender, FileOperation operation)
    {
        var app = (sender as UIApplication)?.Application ?? _app;
        if (app is null || !LicenseManager.IsLicensedQuietly(app))
            return;

        _pending = operation;
        _pendingSince = DateTime.UtcNow;

        var folder = FolderStore.Get(operation);
        if (folder is not null)
            FileDialogNavigator.NavigateNextDialog(folder);
    }

    private static void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e)
    {
        _app ??= e.Document.Application;
        if (!e.Document.IsLinked)
            Remember(e.Document.PathName, FileOperation.Open);
    }

    /// <summary>Fallback for links: read the path of newly added external file references (CAD / Revit link types).</summary>
    private static void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (CurrentPending() is not (FileOperation.LinkCad or FileOperation.LinkRevit))
            return;

        var doc = e.GetDocument();
        foreach (var id in e.GetAddedElementIds())
        {
            if (!ExternalFileUtils.IsExternalFileReference(doc, id))
                continue;
            var path = ExternalFileUtils.GetExternalFileReference(doc, id).GetAbsolutePath();
            Remember(ModelPathUtils.ConvertModelPathToUserVisiblePath(path), FileOperation.LinkCad, FileOperation.LinkRevit);
        }
    }

    private static void Remember(string? path, params FileOperation[] accepted)
    {
        if (CurrentPending() is not { } operation || !accepted.Contains(operation) || string.IsNullOrWhiteSpace(path))
            return;

        // Cloud models and other non-file paths are ignored
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder is not null && Path.IsPathRooted(folder) && Directory.Exists(folder))
            FolderStore.Set(operation, folder);
    }

    private static FileOperation? CurrentPending() =>
        _pending is not null && DateTime.UtcNow - _pendingSince < PendingLifetime ? _pending : null;
}
