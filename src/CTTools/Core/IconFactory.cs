using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CTTools.Core;

/// <summary>
/// Renders ribbon icons at any size (16 px small / 32 px large): a white pictogram on a rounded colored square,
/// either a Windows icon-font glyph or a custom line drawing. No image files needed.
/// </summary>
public static class IconFactory
{
    private static readonly Typeface GlyphFont = new(
        new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <param name="glyph">Code point from the Segoe MDL2 Assets font (ships with Windows 10/11).</param>
    public static Func<int, ImageSource> Glyph(char glyph, string colorHex) => size => Render(colorHex, size, dc =>
    {
        var text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            GlyphFont, size * 0.7, Brushes.White, 1.0);
        var geometry = text.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        geometry.Transform = new TranslateTransform(
            Math.Round((size - bounds.Width) / 2 - bounds.X),
            Math.Round((size - bounds.Height) / 2 - bounds.Y));
        // The font's strokes are hairlines; outlining them makes them readable at 16 px.
        dc.DrawGeometry(Brushes.White, new Pen(Brushes.White, size / 40.0), geometry);
    });

    public static Func<int, ImageSource> Drawing(string colorHex, Action<IconPen> draw) =>
        size => Render(colorHex, size, dc => draw(new IconPen(dc, size)));

    private static ImageSource Render(string colorHex, int size, Action<DrawingContext> content)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            dc.DrawRoundedRectangle(background, null, new Rect(0, 0, size, size), size * 0.18, size * 0.18);
            content(dc);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}

/// <summary>
/// Draws white strokes in icon coordinates: 0..1 across the icon, snapped to whole pixels so lines stay crisp at 16 px.
/// </summary>
public sealed class IconPen
{
    private static readonly Brush Fill = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255));

    private readonly DrawingContext _dc;
    private readonly int _size;
    private readonly Pen _pen;
    private readonly double _pixelOffset;

    public IconPen(DrawingContext dc, int size)
    {
        _dc = dc;
        _size = size;
        var thickness = Math.Max(1, Math.Round(size / 16.0));
        _pen = new Pen(Brushes.White, thickness);
        _pixelOffset = thickness % 2 == 1 ? 0.5 : 0;
    }

    public void Line(double x1, double y1, double x2, double y2) => _dc.DrawLine(_pen, P(x1, y1), P(x2, y2));

    /// <summary>Architectural dimension tick (short 45° slash) centered on a point.</summary>
    public void Tick(double x, double y)
    {
        const double half = 0.07;
        Line(x - half, y + half, x + half, y - half);
    }

    public void Circle(double x, double y, double radius) =>
        _dc.DrawEllipse(null, _pen, P(x, y), radius * _size, radius * _size);

    /// <summary>Semi-transparent filled rectangle, e.g. a beam or wall in plan.</summary>
    public void Box(double x1, double y1, double x2, double y2) =>
        _dc.DrawRectangle(Fill, _pen, new Rect(P(x1, y1), P(x2, y2)));

    private Point P(double x, double y) => new(Snap(x), Snap(y));

    private double Snap(double value) => Math.Round(value * _size - _pixelOffset) + _pixelOffset;
}
