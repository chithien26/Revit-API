using System.Windows;

namespace CTTools.Features.Terrain;

public partial class CutFillReportView : Window
{
    public CutFillReportView(CutFillViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
