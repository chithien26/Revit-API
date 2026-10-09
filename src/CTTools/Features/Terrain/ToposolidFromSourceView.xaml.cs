using System.Windows;

namespace CTTools.Features.Terrain;

public partial class ToposolidFromSourceView : Window
{
    public ToposolidFromSourceView(ToposolidFromSourceViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
