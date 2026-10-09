using System.Runtime.InteropServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace CTTools.Features.Topo;

internal static class TopoPointer
{
    public readonly struct Aim
    {
        public bool Vertical { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public XYZ Origin { get; init; }
        public XYZ Direction { get; init; }
    }

    public static bool TryAim(UIDocument uidoc, int x, int y, out Aim aim)
    {
        aim = default;
        var view = uidoc.ActiveView;
        if (view is null)
            return false;
        var uiView = uidoc.GetOpenUIViews().FirstOrDefault(item => item.ViewId == view.Id);
        if (uiView is null)
            return false;

        var rect = uiView.GetWindowRectangle();
        if (x < rect.Left || x > rect.Right || y < rect.Top || y > rect.Bottom)
            return false;

        var widthPx = Math.Max(1, rect.Right - rect.Left);
        var heightPx = Math.Max(1, rect.Bottom - rect.Top);
        var fx = (x - rect.Left) / (double)widthPx;
        var fy = (y - rect.Top) / (double)heightPx;
        var corners = uiView.GetZoomCorners();
        if (corners.Count < 2)
            return false;

        var right = view.RightDirection;
        var up = view.UpDirection;
        var span = corners[1] - corners[0];
        var across = span.DotProduct(right);
        var along = span.DotProduct(up);
        var plane = corners[0] + right * (across * fx) + up * (along * (1 - fy));
        if (Math.Abs(view.ViewDirection.Z) > 0.85)
        {
            aim = new Aim { Vertical = true, X = plane.X, Y = plane.Y, Origin = plane, Direction = view.ViewDirection };
            return true;
        }

        if (view is View3D view3d && view3d.IsPerspective)
        {
            var eye = view3d.GetOrientation().EyePosition;
            var direction = plane - eye;
            if (direction.GetLength() < 1e-6)
                return false;
            aim = new Aim { Origin = eye, Direction = direction.Normalize() };
            return true;
        }

        aim = new Aim { Origin = plane - view.ViewDirection * 3000, Direction = view.ViewDirection.Normalize() };
        return true;
    }

    public static (int X, int Y) Cursor()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static bool LeftDown() => (GetAsyncKeyState(0x01) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
