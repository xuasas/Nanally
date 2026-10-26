using System;
using System.Windows;

namespace VPet_Simulator.Windows;

internal enum SupportEdge { Bottom, Right, Top, Left }

/// <summary>Screen-inner-edge geometry in WPF logical units, independent of animation and timers.</summary>
internal sealed class GravityPath
{
    public Rect Bounds { get; private set; }
    public SupportEdge Edge { get; private set; }
    public double Offset { get; private set; }
    public double Margin { get; private set; }
    public int Direction { get; set; } = 1;
    public bool Exterior { get; private set; }
    private Point? poseFoot;
    private double? poseAngle;
    public double Angle => poseAngle ?? (Exterior
        ? Edge switch { SupportEdge.Right => 90, SupportEdge.Top => 0, SupportEdge.Left => -90, _ => 180 }
        : Edge switch { SupportEdge.Right => -90, SupportEdge.Top => 180, SupportEdge.Left => 90, _ => 0 });
    public double Length => Edge is SupportEdge.Bottom or SupportEdge.Top ? Bounds.Width - 2 * Margin : Bounds.Height - 2 * Margin;
    // During a corner this is the reference anchor, not the physical shoe contact.
    public Point Foot => poseFoot ?? (Edge switch
    {
        SupportEdge.Bottom => new(Bounds.Left + Margin + Offset, Bounds.Bottom),
        SupportEdge.Right => new(Bounds.Right, Bounds.Bottom - Margin - Offset),
        SupportEdge.Top => new(Bounds.Right - Margin - Offset, Bounds.Top),
        _ => new(Bounds.Left, Bounds.Top + Margin + Offset)
    });

    public void Attach(Rect bounds, double margin, Point foot, bool exterior = false)
    {
        if (bounds.IsEmpty || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) || !double.IsFinite(margin) ||
            margin < 0 || bounds.Width <= 2 * margin || bounds.Height <= 2 * margin)
            throw new ArgumentException("The support area must be larger than the pet canvas.");
        Bounds = bounds;
        Margin = margin;
        Exterior = exterior;
        double[] distances = { Math.Abs(foot.Y - bounds.Bottom), Math.Abs(foot.X - bounds.Right),
            Math.Abs(foot.Y - bounds.Top), Math.Abs(foot.X - bounds.Left) };
        int nearest = 0;
        for (int i = 1; i < distances.Length; i++)
            if (distances[i] < distances[nearest]) nearest = i;
        SetEdge((SupportEdge)nearest, foot);
    }

    public void SetEdge(SupportEdge edge, Point near)
    {
        ClearPose();
        Edge = edge;
        Offset = Math.Clamp(edge switch
        {
            SupportEdge.Bottom => near.X - Bounds.Left - Margin,
            SupportEdge.Right => Bounds.Bottom - Margin - near.Y,
            SupportEdge.Top => Bounds.Right - Margin - near.X,
            _ => near.Y - Bounds.Top - Margin
        }, 0, Length);
    }

    public void Advance(double distance)
    {
        if (!double.IsFinite(distance) || distance < 0) throw new ArgumentOutOfRangeException(nameof(distance));
        if (poseFoot.HasValue) throw new InvalidOperationException("Finish or cancel the corner before straight movement.");
        // A sharp inner corner is not a connected straight segment. The controller
        // must prove a visible, shoe-supported transition before changing edges.
        Offset = Math.Clamp(Offset + distance * (Direction < 0 ? -1 : 1), 0, Length);
    }

    public void SetPose(Point foot, double angle)
    {
        if (!double.IsFinite(foot.X) || !double.IsFinite(foot.Y) || !double.IsFinite(angle))
            throw new ArgumentException("A corner pose must be finite.");
        poseFoot = foot;
        poseAngle = angle;
    }

    public void ClearPose() { poseFoot = null; poseAngle = null; }

    public static Point Rotate(Point point, double angle)
    {
        double radians = angle * Math.PI / 180;
        double x = point.X - 250, y = point.Y - 250;
        return new(250 + x * Math.Cos(radians) - y * Math.Sin(radians),
            250 + x * Math.Sin(radians) + y * Math.Cos(radians));
    }

    public Point WindowOrigin(Point anchor, double zoom)
    {
        Point rotated = Rotate(anchor, Angle);
        return new(Foot.X - rotated.X * zoom, Foot.Y - rotated.Y * zoom);
    }
}
