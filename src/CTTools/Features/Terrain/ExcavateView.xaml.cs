using System.Windows;

namespace CTTools.Features.Terrain;

public partial class ExcavateView : Window
{
    public ExcavateView(ExcavateViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
