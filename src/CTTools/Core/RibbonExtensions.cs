using System.Windows.Media;
using Autodesk.Revit.UI;

namespace CTTools.Core;

public static class RibbonExtensions
{
    public static PushButton AddButton<TCommand>(this RibbonPanel panel, string text, Func<int, ImageSource> icon, string tooltip)
        where TCommand : IExternalCommand
    {
        var type = typeof(TCommand);
        var data = new PushButtonData(type.FullName, text, type.Assembly.Location, type.FullName)
        {
            ToolTip = tooltip,
            Image = icon(16),
            LargeImage = icon(32),
        };
        return (PushButton)panel.AddItem(data);
    }
}
