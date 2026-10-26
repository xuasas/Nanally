using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows;

/// <summary>Measured opaque silhouette and actual shoe-sole pixels of one immutable, fixed turn pose.</summary>
internal sealed class GravityCornerProfile
{
    public string SourcePath { get; }
    public string SourceSha256 { get; }
    public Point Anchor { get; } = new(250, 468.75);
    public IReadOnlyList<Point> Hull { get; }
    public IReadOnlyList<Point> ShoeHull { get; }
    public double RotationRadius { get; }
    public int AlphaThreshold => 128;
    // Source pixels, not Core coordinates. The selected standing frame's final ten sole rows live here.
    public Rect ShoeSourceRegion { get; private init; } = new(195, 470, 121, 42);

    private GravityCornerProfile(string sourcePath, string sha256, Point[] hull, Point[] shoes)
    {
        SourcePath = sourcePath; SourceSha256 = sha256;
        Hull = Array.AsReadOnly(hull); ShoeHull = Array.AsReadOnly(shoes);
        RotationRadius = hull.Max(point => (point - new Point(250, 250)).Length);
    }

    public static GravityCornerProfile Load(string sourcePath) => Load(sourcePath, false);
    public static GravityCornerProfile LoadWalk(string sourcePath) => Load(sourcePath, true);
    private static GravityCornerProfile Load(string sourcePath, bool walking)
    {
        string full = Path.GetFullPath(sourcePath);
        byte[] bytes = File.ReadAllBytes(full);
        if (bytes.Length < 26 || !bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || bytes[25] != 6)
            throw new InvalidDataException("The fixed corner pose must be a transparent RGBA PNG.");
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(full); bitmap.EndInit(); bitmap.Freeze();
        if (bitmap.PixelWidth != 512 || bitmap.PixelHeight != 512)
            throw new InvalidDataException("The fixed corner pose must use the accepted 512px canvas.");
        byte[] pixels = new byte[512 * 512 * 4];
        new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 512 * 4, 0);
        var fullPoints = new List<Point>(); var shoePoints = new List<Point>(); int transparent = 0;
        for (int y = 0; y < 512; y++)
        {
            int left = 512, right = -1, shoeLeft = 512, shoeRight = -1;
            for (int x = 0; x < 512; x++)
            {
                byte alpha = pixels[(y * 512 + x) * 4 + 3];
                if (alpha == 0) transparent++;
                if (alpha < 128) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                if (y >= 470 && (walking || x >= 195 && x <= 315))
                { shoeLeft = Math.Min(shoeLeft, x); shoeRight = Math.Max(shoeRight, x); }
            }
            AddRow(fullPoints, left, right, y); AddRow(shoePoints, shoeLeft, shoeRight, y);
        }
        if (transparent < 512 * 512 / 10 || fullPoints.Count == 0 || shoePoints.Count == 0)
            throw new InvalidDataException("The fixed turn pose needs transparent margins and measured shoe-sole pixels.");
        return new GravityCornerProfile(full, Convert.ToHexString(SHA256.HashData(bytes)),
            ConvexHull(fullPoints), ConvexHull(shoePoints))
            { ShoeSourceRegion = walking ? new Rect(0, 470, 512, 42) : new Rect(195, 470, 121, 42) };
    }

    private static void AddRow(List<Point> points, int left, int right, int y)
    {
        if (right < left) return;
        const double scale = 500.0 / 512;
        // Pixel cells have area: their outer edges preserve the accepted sole baseline y=480.
        points.Add(new Point(left * scale, y * scale));
        points.Add(new Point((right + 1) * scale, y * scale));
        points.Add(new Point(left * scale, (y + 1) * scale));
        points.Add(new Point((right + 1) * scale, (y + 1) * scale));
    }

    private static Point[] ConvexHull(IEnumerable<Point> input)
    {
        Point[] points = input.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (points.Length < 3) throw new InvalidDataException("Insufficient measured silhouette points.");
        static double Cross(Point a, Point b, Point c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var hull = new List<Point>();
        foreach (Point point in points)
        { while (hull.Count >= 2 && Cross(hull[^2], hull[^1], point) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(point); }
        int lower = hull.Count;
        for (int i = points.Length - 2; i >= 0; i--)
        { while (hull.Count > lower && Cross(hull[^2], hull[^1], points[i]) <= 0) hull.RemoveAt(hull.Count - 1); hull.Add(points[i]); }
        hull.RemoveAt(hull.Count - 1); return hull.ToArray();
    }
}

/// <summary>Foot is a reference anchor; Contact is an actual measured shoe point on a real screen edge.</summary>
internal sealed record GravityCornerPose(Point Foot, double Angle, string Stage, Point Contact,
    SupportEdge ContactEdge, double ContactGap, Rect VisibleBounds, Point LocalContact, Rect CanvasBounds);

/// <summary>Constant-speed supported-silhouette walk-through around a real inner-screen corner:
/// a short run-in, the contact turn, and a short run-out at the same walking pace.</summary>
internal sealed class GravityCornerPlan
{
    private const int TurnSamples = 900;
    private const double Epsilon = 0.00001;
    private readonly GravityCornerProfile profile;
    private readonly Rect bounds;
    private readonly double zoom;
    private readonly Vector incomingNormal, outgoingNormal;
    private readonly Point corner;
    public SupportEdge IncomingEdge { get; }
    public SupportEdge OutgoingEdge { get; }
    public int Direction { get; }
    public Point StartFoot { get; }
    public Point EntryFoot { get; }
    public Point ExitFoot { get; }
    public Point EndFoot { get; }
    public double StartAngle { get; }
    public double EndAngle { get; }
    public double ApproachSeconds { get; }
    public double TurnSeconds { get; }
    public double DepartSeconds { get; }
    public double DurationSeconds => ApproachSeconds + TurnSeconds + DepartSeconds;
    public double TurnArcLength { get; }

    private GravityCornerPlan(GravityCornerProfile profile, Rect bounds, double margin,
        SupportEdge incoming, int direction, double zoom)
    {
        this.profile = profile; this.bounds = bounds; this.zoom = zoom;
        IncomingEdge = incoming; Direction = direction;
        OutgoingEdge = (SupportEdge)(((int)incoming + (direction > 0 ? 1 : 3)) % 4);
        incomingNormal = Normal(incoming); outgoingNormal = Normal(OutgoingEdge);
        StartAngle = incoming switch { SupportEdge.Right => -90, SupportEdge.Top => 180, SupportEdge.Left => 90, _ => 0 };
        EndAngle = StartAngle - direction * 90;
        StartFoot = Endpoint(incoming, direction, bounds, margin);
        EndFoot = Endpoint(OutgoingEdge, -direction, bounds, margin);
        corner = new Point(incomingNormal.X > 0 || outgoingNormal.X > 0 ? bounds.Right : bounds.Left,
            incomingNormal.Y > 0 || outgoingNormal.Y > 0 ? bounds.Bottom : bounds.Top);
        EntryFoot = TightFoot(StartAngle); ExitFoot = TightFoot(EndAngle);
        // One constant walking speed carries the pet through run-in, turn and run-out;
        // there is no eased approach or departure.
        double speed = 70 * zoom;
        ApproachSeconds = (EntryFoot - StartFoot).Length / speed;
        DepartSeconds = (EndFoot - ExitFoot).Length / speed;
        Point previous = EntryFoot; double length = 0;
        for (int i = 1; i <= TurnSamples; i++)
        { Point current = TightFoot(StartAngle + (EndAngle - StartAngle) * i / TurnSamples); length += (current - previous).Length; previous = current; }
        TurnArcLength = length; TurnSeconds = Math.Max(0.2, length / speed);
    }

    public static bool TryCreate(GravityCornerProfile profile, Rect bounds, double margin,
        SupportEdge incoming, int direction, double zoom, out GravityCornerPlan? plan, out string reason)
    {
        plan = null; reason = "";
        if (profile == null || bounds.IsEmpty || !Finite(bounds.X, bounds.Y, bounds.Width, bounds.Height, margin, zoom) ||
            zoom <= 0 || margin < 0 || bounds.Width <= 2 * margin || bounds.Height <= 2 * margin ||
            !Enum.IsDefined(incoming) || (direction != 1 && direction != -1))
        { reason = "invalid-or-insufficient-support-area"; return false; }
        // Conservative all-angle viewport guarantee, using opaque hull rather than the transparent 512px rectangle.
        if (profile.RotationRadius > 250 + Epsilon)
        { reason = "fixed-silhouette-cannot-rotate-inside-the-500px-player"; return false; }
        var candidate = new GravityCornerPlan(profile, bounds, margin, incoming, direction, zoom);
        for (int i = 0; i <= TurnSamples; i++)
        {
            double angle = candidate.StartAngle + (candidate.EndAngle - candidate.StartAngle) * i / TurnSamples;
            GravityCornerPose pose = candidate.Pose(candidate.TightFoot(angle), angle, "Turn");
            if (!candidate.Valid(pose, out reason)) return false;
        }
        foreach (double time in new[] { 0.0, candidate.ApproachSeconds / 2, candidate.ApproachSeconds,
            candidate.ApproachSeconds + candidate.TurnSeconds, candidate.DurationSeconds - candidate.DepartSeconds / 2,
            candidate.DurationSeconds })
            if (!candidate.Valid(candidate.Evaluate(time), out reason)) return false;
        plan = candidate; return true;
    }

    public GravityCornerPose Evaluate(double timelineSeconds)
    {
        if (!double.IsFinite(timelineSeconds)) throw new ArgumentOutOfRangeException(nameof(timelineSeconds));
        double time = Math.Clamp(timelineSeconds, 0, DurationSeconds);
        if (ApproachSeconds > 0 && time < ApproachSeconds)
        {
            double t = time / ApproachSeconds;
            return Pose(Lerp(StartFoot, EntryFoot, t), StartAngle, "Approach");
        }
        if (time < ApproachSeconds + TurnSeconds)
        {
            double t = Math.Clamp((time - ApproachSeconds) / TurnSeconds, 0, 1);
            double angle = StartAngle + (EndAngle - StartAngle) * t;
            return Pose(TightFoot(angle), angle, "Turn");
        }
        double u = DepartSeconds > 0 ? Math.Clamp((time - ApproachSeconds - TurnSeconds) / DepartSeconds, 0, 1) : 1;
        return Pose(Lerp(ExitFoot, EndFoot, u), EndAngle, "Depart");
    }

    private Point TightFoot(double angle)
    {
        double first = Support(profile.Hull, incomingNormal, angle).height;
        double second = Support(profile.Hull, outgoingNormal, angle).height;
        return corner - incomingNormal * first - outgoingNormal * second;
    }

    private (double height, Point local) Support(IReadOnlyList<Point> hull, Vector normal, double angle)
    {
        double height = double.NegativeInfinity; Point local = default;
        foreach (Point point in hull)
        {
            Vector relative = RotatedRelative(point, angle) * zoom;
            double dot = Vector.Multiply(relative, normal);
            if (dot > height) { height = dot; local = point; }
        }
        return (height, local);
    }

    private GravityCornerPose Pose(Point foot, double angle, string stage)
    {
        (double height, Point local) first = Support(profile.ShoeHull, incomingNormal, angle);
        (double height, Point local) second = Support(profile.ShoeHull, outgoingNormal, angle);
        Point c0 = foot + RotatedRelative(first.local, angle) * zoom;
        Point c1 = foot + RotatedRelative(second.local, angle) * zoom;
        double gap0 = Math.Abs(Vector.Multiply(corner - c0, incomingNormal));
        double gap1 = Math.Abs(Vector.Multiply(corner - c1, outgoingNormal));
        bool useFirst = gap0 <= gap1;
        Rect visible = Rect.Empty, canvas = Rect.Empty;
        foreach (Point point in profile.Hull)
        {
            visible.Union(foot + RotatedRelative(point, angle) * zoom);
            canvas.Union(GravityPath.Rotate(point, angle));
        }
        return new GravityCornerPose(foot, angle, stage, useFirst ? c0 : c1,
            useFirst ? IncomingEdge : OutgoingEdge, useFirst ? gap0 : gap1, visible,
            useFirst ? first.local : second.local, canvas);
    }

    private Vector RotatedRelative(Point point, double angle)
    {
        double radians = angle * Math.PI / 180, c = Math.Cos(radians), s = Math.Sin(radians);
        Vector relative = point - profile.Anchor;
        return new Vector(relative.X * c - relative.Y * s, relative.X * s + relative.Y * c);
    }

    private bool Valid(GravityCornerPose pose, out string reason)
    {
        reason = "";
        if (pose.ContactGap > Epsilon)
        { reason = "fixed-pose-has-no-shoe-contact-on-either-real-edge"; return false; }
        if (!Contains(bounds, pose.VisibleBounds))
        { reason = "visible-silhouette-would-leave-the-support-screen"; return false; }
        if (!Contains(new Rect(0, 0, 500, 500), pose.CanvasBounds))
        { reason = "rotated-silhouette-would-be-clipped-by-the-player"; return false; }
        return true;
    }

    private static bool Contains(Rect outer, Rect inner) => !inner.IsEmpty && inner.Left >= outer.Left - Epsilon &&
        inner.Top >= outer.Top - Epsilon && inner.Right <= outer.Right + Epsilon && inner.Bottom <= outer.Bottom + Epsilon;
    private static bool Finite(params double[] values) => values.All(double.IsFinite);
    private static Point Lerp(Point a, Point b, double t) => a + (b - a) * t;
    internal static Vector Normal(SupportEdge edge) => edge switch
    { SupportEdge.Bottom => new Vector(0, 1), SupportEdge.Right => new Vector(1, 0), SupportEdge.Top => new Vector(0, -1), _ => new Vector(-1, 0) };
    private static Point Endpoint(SupportEdge edge, int direction, Rect bounds, double margin) => edge switch
    {
        SupportEdge.Bottom => new Point(direction > 0 ? bounds.Right - margin : bounds.Left + margin, bounds.Bottom),
        SupportEdge.Right => new Point(bounds.Right, direction > 0 ? bounds.Top + margin : bounds.Bottom - margin),
        SupportEdge.Top => new Point(direction > 0 ? bounds.Left + margin : bounds.Right - margin, bounds.Top),
        _ => new Point(bounds.Left, direction > 0 ? bounds.Bottom - margin : bounds.Top + margin)
    };
}
