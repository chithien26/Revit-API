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

    public static PulldownButton AddMenu(this RibbonPanel panel, string name, string text, Func<int, ImageSource> icon, string tooltip)
    {
        var data = new PulldownButtonData(name, text)
        {
            ToolTip = tooltip,
            Image = icon(16),
            LargeImage = icon(32),
        };
        return (PulldownButton)panel.AddItem(data);
    }

    public static void AddToMenu<TCommand>(this PulldownButton menu, string text, Func<int, ImageSource> icon, string tooltip)
        where TCommand : IExternalCommand
    {
        var type = typeof(TCommand);
        menu.AddPushButton(new PushButtonData(type.FullName + ".menu", text, type.Assembly.Location, type.FullName)
        {
            ToolTip = tooltip,
            Image = icon(16),
            LargeImage = icon(32),
        });
    }
}
