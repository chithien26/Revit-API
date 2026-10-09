using System.Windows;

namespace CTTools.Features.Terrain;

public partial class AlignView : Window
{
    public AlignView(AlignViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
