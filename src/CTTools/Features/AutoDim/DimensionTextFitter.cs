using Autodesk.Revit.DB;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Moves the text of dimension segments that are too short to hold it: end segments get their text pushed
/// beyond the end of the string, middle segments get it lifted off the line. Needs a regenerated document.
/// </summary>
internal static class DimensionTextFitter
{
    /// <summary>Average digit width as a fraction of text height (Arial / MS Gothic are about 0.55-0.6).</summary>
    private const double CharWidthRatio = 0.65;

    private const double DefaultTextSizeFeet = 2.5 / 304.8;

    private sealed record Segment(double Value, string Text, XYZ Origin, XYZ TextPosition, Action<XYZ> Move);

    /// <returns>Number of texts moved.</returns>
    public static int Fit(IEnumerable<Dimension> dimensions, View view)
    {
        var moved = 0;
        foreach (var dimension in dimensions)
        {
            try
            {
                moved += Fit(dimension, view);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Leave the text where Revit put it
            }
        }
        return moved;
    }

    private static int Fit(Dimension dimension, View view)
    {
        if (dimension.Curve is not Line line)
            return 0;

        var type = dimension.DimensionType;
        var textHeight = (type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? DefaultTextSizeFeet) * view.Scale;
        var widthScale = type.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? 1;
        var direction = line.Direction.Normalize();

        var segments = GetSegments(dimension)
            .OrderBy(s => s.Origin.DotProduct(direction))
            .ToList();

        var moved = 0;
        var lift = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var textWidth = segment.Text.Length * textHeight * CharWidthRatio * widthScale;
            if (segment.Value >= textWidth + textHeight)
            {
                lift = 0;
                continue;
            }

            var sideways = segment.Value / 2 + textWidth / 2 + textHeight * 0.5;
            XYZ shift;
            if (i == segments.Count - 1)
            {
                shift = direction * sideways;
            }
            else if (i == 0)
            {
                shift = -direction * sideways;
            }
            else
            {
                // Consecutive short middle segments are stacked so their texts don't collide either
                lift++;
                shift = Perpendicular(segment, direction, view) * textHeight * 1.2 * lift;
            }

            segment.Move(segment.TextPosition + shift);
            moved++;
        }
        return moved;
    }

    private static IEnumerable<Segment> GetSegments(Dimension dimension)
    {
        if (dimension.NumberOfSegments == 0)
        {
            if (dimension.Value is { } value && dimension.IsTextPositionAdjustable())
                yield return new Segment(value, dimension.ValueString ?? "0000", dimension.Origin, dimension.TextPosition,
                    p => dimension.TextPosition = p);
            yield break;
        }

        foreach (DimensionSegment segment in dimension.Segments)
        {
            if (segment.Value is { } value && segment.IsTextPositionAdjustable())
                yield return new Segment(value, segment.ValueString ?? "0000", segment.Origin, segment.TextPosition,
                    p => segment.TextPosition = p);
        }
    }

    /// <summary>Unit vector from the dimension line towards the side its text sits on.</summary>
    private static XYZ Perpendicular(Segment segment, XYZ direction, View view)
    {
        var offset = segment.TextPosition - segment.Origin;
        offset -= direction * offset.DotProduct(direction);
        return offset.GetLength() > 1e-9
            ? offset.Normalize()
            : view.ViewDirection.CrossProduct(direction).Normalize();
    }
}
