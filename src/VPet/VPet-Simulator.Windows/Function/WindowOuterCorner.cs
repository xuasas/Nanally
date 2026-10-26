using System;
using System.Linq;
using System.Windows;

namespace VPet_Simulator.Windows;

/// <summary>A convex outer corner pivots on an actual sole point, rather than an inner-screen turn.</summary>
internal sealed class WindowOuterCorner
{
    private readonly GravityCornerProfile profile;
    private readonly Point vertex, pivot;
    private readonly double zoom, startAngle, endAngle, approach, turn, depart;
    private readonly Point entry, exit;
    public SupportEdge Incoming { get; }
    public SupportEdge Outgoing { get; }
    public int Direction { get; }
    public Point Start { get; }
    public Point End { get; }
    public double Duration => approach + turn + depart;
    public double TurnDuration => turn;

    public WindowOuterCorner(GravityCornerProfile profile, GravityPath path, double zoom)
    {
        this.profile = profile; this.zoom = zoom;
        Incoming = path.Edge; Direction = path.Direction;
        Outgoing = (SupportEdge)(((int)Incoming + (Direction > 0 ? 1 : 3)) % 4);
        startAngle = path.Angle; endAngle = startAngle - Direction * 90;
        var b = path.Bounds;
        vertex = Incoming switch
        {
            SupportEdge.Top => new(Direction > 0 ? b.Left : b.Right, b.Top),
            SupportEdge.Bottom => new(Direction > 0 ? b.Right : b.Left, b.Bottom),
            SupportEdge.Right => new(b.Right, Direction > 0 ? b.Top : b.Bottom),
            _ => new(b.Left, Direction > 0 ? b.Bottom : b.Top)
        };
        double soleY = profile.ShoeHull.Max(p => p.Y);
        var sole = profile.ShoeHull.Where(p => Math.Abs(p.Y - soleY) < 0.00001).ToArray();
        pivot = new(Direction > 0 ? sole.Min(p => p.X) : sole.Max(p => p.X), soleY);
        if (Math.Abs(soleY - profile.Anchor.Y) > 0.00001 || Math.Abs(pivot.X - profile.Anchor.X) * zoom > path.Margin)
            throw new ArgumentException("Sole pivot does not fit the outside support margin.");
        Start = path.Foot;
        var destination = new GravityPath { Direction = Direction };
        destination.Attach(b, path.Margin, vertex, true); destination.SetEdge(Outgoing, vertex);
        End = destination.Foot;
        entry = PivotFoot(startAngle); exit = PivotFoot(endAngle);
        double speed = 70 * zoom;
        approach = (entry - Start).Length / speed;
        turn = Math.Max(0.05, (pivot - profile.Anchor).Length * zoom * Math.PI / 2 / speed);
        depart = (End - exit).Length / speed;
    }

    private Vector Relative(double angle) => GravityPath.Rotate(pivot, angle) - GravityPath.Rotate(profile.Anchor, angle);
    private Point PivotFoot(double angle) => vertex - Relative(angle) * zoom;
    private static Point Lerp(Point a, Point b, double amount) => a + (b - a) * amount;

    public double RebaseTime(WindowOuterCorner previous, double seconds)
    {
        // Individual gait frames have different sole pivots and phase durations.
        // Preserve progress within the current phase, rather than total-time fraction.
        if (seconds < previous.approach) return approach * seconds / previous.approach;
        if (seconds < previous.approach + previous.turn)
            return approach + turn * (seconds - previous.approach) / previous.turn;
        return approach + turn + (previous.depart > 0 ? depart * (seconds - previous.approach - previous.turn) / previous.depart : depart);
    }

    public GravityCornerPose Evaluate(double seconds, GravityCornerProfile? visibleFrame = null)
    {
        var shown = visibleFrame ?? profile;
        double soleY = shown.ShoeHull.Max(p => p.Y);
        var sole = shown.ShoeHull.Where(p => Math.Abs(p.Y - soleY) < 0.00001).ToArray();
        Point contactPivot = new(Direction > 0 ? sole.Min(p => p.X) : sole.Max(p => p.X), soleY);
        Vector RelativeContact(double a) => GravityPath.Rotate(contactPivot, a) - GravityPath.Rotate(shown.Anchor, a);
        Point currentEntry = vertex - RelativeContact(startAngle) * zoom;
        Point currentExit = vertex - RelativeContact(endAngle) * zoom;
        double time = Math.Clamp(seconds, 0, Duration), angle; Point foot; string stage;
        if (time < approach) { angle = startAngle; foot = Lerp(Start, currentEntry, time / approach); stage = "Approach"; }
        else if (time < approach + turn)
        { angle = startAngle + (endAngle - startAngle) * (time - approach) / turn; foot = vertex - RelativeContact(angle) * zoom; stage = "Turn"; }
        else { angle = endAngle; foot = Lerp(currentExit, End, depart > 0 ? (time - approach - turn) / depart : 1); stage = "Depart"; }
        Point contact = foot + RelativeContact(angle) * zoom;
        var visible = Rect.Empty; var canvas = Rect.Empty;
        foreach (var point in shown.Hull)
        {
            var rotated = GravityPath.Rotate(point, angle);
            visible.Union(foot + (rotated - GravityPath.Rotate(shown.Anchor, angle)) * zoom);
            canvas.Union(rotated);
        }
        var support = stage == "Depart" ? Outgoing : Incoming;
        double gap = support is SupportEdge.Top or SupportEdge.Bottom ? Math.Abs(contact.Y - vertex.Y) : Math.Abs(contact.X - vertex.X);
        return new(foot, angle, stage, contact, support, gap, visible, contactPivot, canvas);
    }
}
