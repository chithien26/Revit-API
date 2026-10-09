using System.Windows;

namespace CTTools.Features.Terrain;

public partial class RoadView : Window
{
    public RoadView(RoadViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
