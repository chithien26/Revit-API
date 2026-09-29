using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CTTools.Core;
using CTTools.Features.FolderMemory;

namespace CTTools.Commands;

[Transaction(TransactionMode.ReadOnly)]
public class FileFoldersCommand : CommandBase
{
    protected override Result Run(UIApplication uiApp)
    {
        var content = new StringBuilder();
        foreach (var operation in Enum.GetValues<FileOperation>())
        {
            var folder = FolderStore.All.TryGetValue(operation, out var f) ? f : "(not used yet)";
            content.AppendLine($"{operation}: {folder}");
        }

        var dialog = new TaskDialog("File Folders")
        {
            MainInstruction = "Each file operation remembers its own last folder",
            MainContent = content.ToString(),
            FooterText = FolderMemoryService.UnavailableCommands.Count > 0
                ? "Not available (used by another add-in): " + string.Join(", ", FolderMemoryService.UnavailableCommands)
                : null,
        };
        dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Forget all remembered folders");

        if (dialog.Show() == TaskDialogResult.CommandLink1)
            FolderStore.Clear();

        return Result.Succeeded;
    }
}
