using System.Windows.Media;

namespace CTTools.Core;

/// <summary>
/// Icons for every ribbon button. Colors are grouped by panel so related tools look related.
/// </summary>
public static class RibbonIcons
{
    private const string GeneralColor = "#546E7A";
    private const string AnnotateColor = "#D84315";
    private const string ToolsColor = "#00796B";

    public static Func<int, ImageSource> About { get; } = IconFactory.Glyph('\uE946', GeneralColor);
    public static Func<int, ImageSource> License { get; } = IconFactory.Glyph('\uE8D7', "#6A1B9A");

    public static Func<int, ImageSource> TestLicense { get; } = IconFactory.Glyph('\uE930', ToolsColor);
    public static Func<int, ImageSource> CountElements { get; } = IconFactory.Glyph('\uE8EF', "#1565C0");
    public static Func<int, ImageSource> ExportSchedules { get; } = IconFactory.Glyph('\uE80A', "#2E7D32");
    public static Func<int, ImageSource> FileFolders { get; } = IconFactory.Glyph('\uE838', "#EF6C00");

    /// <summary>Contour lines, the mark used for the topography tool.</summary>
    public static Func<int, ImageSource> Topo { get; } = IconFactory.Drawing("#2E7D32", p =>
    {
        p.Line(0.12, 0.72, 0.88, 0.66);
        p.Line(0.16, 0.52, 0.84, 0.46);
        p.Line(0.22, 0.34, 0.78, 0.28);
        p.Line(0.32, 0.18, 0.68, 0.14);
    });

    public static Func<int, ImageSource> CreateTopo { get; } = IconFactory.Drawing("#1565C0", p =>
    {
        p.Line(0.18, 0.72, 0.82, 0.72);
        p.Line(0.18, 0.72, 0.18, 0.28);
        p.Line(0.82, 0.72, 0.82, 0.28);
        p.Circle(0.34, 0.42, 0.05);
        p.Circle(0.5, 0.34, 0.05);
        p.Circle(0.66, 0.46, 0.05);
    });

    public static Func<int, ImageSource> Road { get; } = IconFactory.Drawing("#455A64", p =>
    {
        p.Line(0.12, 0.62, 0.88, 0.28);
        p.Line(0.12, 0.38, 0.88, 0.72);
    });

    public static Func<int, ImageSource> Excavate { get; } = IconFactory.Drawing("#6D4C41", p =>
    {
        p.Line(0.12, 0.32, 0.88, 0.32);
        p.Line(0.28, 0.32, 0.4, 0.72);
        p.Line(0.72, 0.32, 0.6, 0.72);
        p.Line(0.4, 0.72, 0.6, 0.72);
    });

    public static Func<int, ImageSource> AlignTopo { get; } = IconFactory.Drawing("#2E7D32", p =>
    {
        p.Line(0.16, 0.78, 0.84, 0.62);
        p.Line(0.5, 0.18, 0.5, 0.58);
        p.Line(0.38, 0.46, 0.5, 0.62);
        p.Line(0.62, 0.46, 0.5, 0.62);
    });

    public static Func<int, ImageSource> CutFill { get; } = IconFactory.Drawing("#EF6C00", p =>
    {
        p.Line(0.18, 0.7, 0.46, 0.7);
        p.Line(0.54, 0.32, 0.82, 0.32);
        p.Line(0.68, 0.18, 0.68, 0.46);
    });

    public static Func<int, ImageSource> EditWall { get; } = IconFactory.Drawing("#6D4C41", p =>
    {
        p.Box(0.28, 0.22, 0.72, 0.78);
        p.Line(0.28, 0.5, 0.72, 0.5);
    });

    public static Func<int, ImageSource> ShapeWall { get; } = IconFactory.Drawing("#6D4C41", p =>
    {
        p.Line(0.16, 0.72, 0.84, 0.58);
        p.Line(0.28, 0.72, 0.28, 0.3);
        p.Line(0.72, 0.58, 0.72, 0.22);
        p.Line(0.28, 0.3, 0.72, 0.22);
    });

    public static Func<int, ImageSource> WallRailing { get; } = IconFactory.Drawing("#6D4C41", p =>
    {
        p.Line(0.2, 0.62, 0.8, 0.62);
        p.Line(0.3, 0.62, 0.3, 0.28);
        p.Line(0.5, 0.62, 0.5, 0.28);
        p.Line(0.7, 0.62, 0.7, 0.28);
    });

    public static Func<int, ImageSource> SetElevation { get; } = IconFactory.Drawing("#1565C0", p =>
    {
        p.Line(0.18, 0.7, 0.82, 0.7);
        p.Line(0.5, 0.28, 0.5, 0.62);
        p.Line(0.4, 0.5, 0.5, 0.62);
        p.Line(0.6, 0.5, 0.5, 0.62);
    });

    public static Func<int, ImageSource> SetCoordinate { get; } = IconFactory.Drawing("#1565C0", p =>
    {
        p.Line(0.22, 0.78, 0.22, 0.22);
        p.Line(0.22, 0.78, 0.78, 0.78);
        p.Circle(0.55, 0.42, 0.08);
    });

    public static Func<int, ImageSource> GetLocation { get; } = IconFactory.Drawing("#1565C0", p =>
    {
        p.Line(0.5, 0.18, 0.5, 0.62);
        p.Line(0.32, 0.44, 0.5, 0.62);
        p.Line(0.68, 0.44, 0.5, 0.62);
        p.Circle(0.5, 0.74, 0.08);
    });

    public static Func<int, ImageSource> CadPoints { get; } = IconFactory.Drawing("#455A64", p =>
    {
        p.Circle(0.3, 0.68, 0.07);
        p.Circle(0.55, 0.42, 0.07);
        p.Circle(0.72, 0.7, 0.07);
    });

    public static Func<int, ImageSource> Planting { get; } = IconFactory.Drawing("#2E7D32", p =>
    {
        p.Circle(0.5, 0.58, 0.16);
        p.Line(0.5, 0.42, 0.5, 0.18);
    });

    public static Func<int, ImageSource> Scatter { get; } = IconFactory.Drawing("#2E7D32", p =>
    {
        p.Circle(0.3, 0.66, 0.08);
        p.Circle(0.68, 0.66, 0.08);
        p.Circle(0.48, 0.34, 0.08);
    });

    public static Func<int, ImageSource> Elevation { get; } = IconFactory.Drawing("#EF6C00", p =>
    {
        p.Line(0.18, 0.78, 0.82, 0.28);
        p.Line(0.18, 0.28, 0.82, 0.28);
    });

    public static Func<int, ImageSource> WallLayout { get; } = IconFactory.Drawing("#546E7A", p =>
    {
        p.Line(0.22, 0.22, 0.22, 0.78);
        p.Line(0.5, 0.22, 0.5, 0.78);
        p.Line(0.78, 0.22, 0.78, 0.78);
    });

    /// <summary>Two stacked dimension strings: overall + chain.</summary>
    public static Func<int, ImageSource> AutoDimAll { get; } = IconFactory.Drawing(AnnotateColor, p =>
    {
        p.Line(0.2, 0.22, 0.2, 0.8);
        p.Line(0.5, 0.56, 0.5, 0.8);
        p.Line(0.8, 0.22, 0.8, 0.8);

        p.Line(0.15, 0.32, 0.85, 0.32);
        p.Tick(0.2, 0.32);
        p.Tick(0.8, 0.32);

        p.Line(0.15, 0.66, 0.85, 0.66);
        p.Tick(0.2, 0.66);
        p.Tick(0.5, 0.66);
        p.Tick(0.8, 0.66);
    });

    /// <summary>Two grid lines with bubbles and a dimension between them.</summary>
    public static Func<int, ImageSource> AutoDimGrids { get; } = IconFactory.Drawing("#C62828", p =>
    {
        p.Circle(0.28, 0.24, 0.13);
        p.Circle(0.72, 0.24, 0.13);
        p.Line(0.28, 0.37, 0.28, 0.9);
        p.Line(0.72, 0.37, 0.72, 0.9);

        p.Line(0.2, 0.64, 0.8, 0.64);
        p.Tick(0.28, 0.64);
        p.Tick(0.72, 0.64);
    });

    /// <summary>A beam next to a grid line, dimensioned grid - face - face.</summary>
    public static Func<int, ImageSource> AutoDimElements { get; } = IconFactory.Drawing("#AD1457", p =>
    {
        p.Line(0.22, 0.12, 0.22, 0.88);
        p.Box(0.46, 0.14, 0.78, 0.56);
        p.Line(0.46, 0.56, 0.46, 0.84);
        p.Line(0.78, 0.56, 0.78, 0.84);

        p.Line(0.16, 0.74, 0.84, 0.74);
        p.Tick(0.22, 0.74);
        p.Tick(0.46, 0.74);
        p.Tick(0.78, 0.74);
    });
}
