using Autodesk.Revit.DB;

namespace CTTools.Features.Topo;

public enum TopoMode
{
    NewPad,
    NewFromLines,
    AddPoints,
    AddLines,
    ShapeByPoint,
    Smooth,
    InsertMidpoint,
    SlopePath,
    FromEdges,
}

public enum ShapeAction
{
    Raise,
    Lower,
    SmoothOut,
}

/// <summary>Values are in Revit internal units (feet) except angles, which are degrees.</summary>
public sealed class TopoRequest
{
    public required TopoMode Mode { get; init; }
    public required ElementId LevelId { get; init; }
    public required ElementId TypeId { get; init; }
    public double Elevation { get; init; }
    public bool ElevationIsOffset { get; init; }
    public double Radius { get; init; }
    public int Rings { get; init; }
    public double Gain { get; init; }
    public ShapeAction Shape { get; init; }
    public int SmoothPasses { get; init; }
    public bool SlopeByAngle { get; init; }
    public double SlopeDegrees { get; init; }
    public double SlopeEndElevation { get; init; }
    public string Result { get; set; } = "";
}
