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
