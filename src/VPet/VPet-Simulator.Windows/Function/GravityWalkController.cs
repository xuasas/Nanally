using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows;

/// <summary>Opt-in gravity prototype using character walk loops when available.</summary>
internal sealed class GravityWalkController : IDisposable
{
    private readonly MainWindow window;
    private readonly Main main;
    private readonly DispatcherTimer timer;
    private readonly RotateTransform rotation = new(0, 250, 250);
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private bool pointerDown, dragged, disposed;
    private int animationGeneration;
    private Point pointerStart, originStart, clickLocal;
    private readonly Dictionary<string, GravityCornerProfile> cornerProfiles = new(StringComparer.OrdinalIgnoreCase);
    private GravityCornerPlan? corner;
    private WindowOuterCorner? outerCorner;
    private WindowEdgeSurface? surface;
    private readonly Dictionary<string, GravityCornerProfile[]> walkProfiles = new();
    private readonly List<Image> observedImages = new();
    private readonly DependencyPropertyDescriptor frameDescriptor = DependencyPropertyDescriptor.FromProperty(Image.SourceProperty, typeof(Image));
    private WindowEdgePicker? picker;
    internal int ObservedFrameImageCount => observedImages.Count;
    private bool previousTopmost;
    public bool WindowAttached => surface != null && Enabled;
    public string? TargetWindowTitle => surface?.Title;
    public string? LastWindowStopReason { get; private set; }
    private GravityCornerPose? cornerPose;
    private double cornerSeconds;
    private Action? deferredInteraction;
    public GravityPath Path { get; } = new();
    public bool Enabled { get; private set; }
    public bool PresentationActive { get; set; }
    public bool CornerActive => corner != null || outerCorner != null;
    public string CornerStage => cornerPose?.Stage ?? "Idle";
    public double CornerElapsedSeconds => cornerSeconds;
    public double CornerDurationSeconds => outerCorner?.Duration ?? corner?.DurationSeconds ?? 0;
    public double CornerTurnDurationSeconds => outerCorner?.TurnDuration ?? corner?.TurnSeconds ?? 0;
    public GravityCornerPose? CornerPose => cornerPose;
    public int CompletedCornerCount { get; private set; }
    public int RejectedCornerCount { get; private set; }
    public string? LastCornerRejection { get; private set; }
    public bool HasDeferredInteraction => deferredInteraction != null;
    public bool CharacterWalk => FindCharacterWalk() != null;
    public Point Anchor => CharacterWalk ? new(250, 468.75) : Path.Direction < 0 ? new(220, 472) : new(280, 472);

    public GravityWalkController(MainWindow window)
    {
        this.window = window;
        main = window.Main;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, Tick, window.Dispatcher);
        timer.Stop();
        main.PreviewMouseLeftButtonDown += PointerDown;
        main.PreviewMouseLeftButtonUp += PointerUp;
        main.PreviewMouseMove += PointerMove;
        main.LostMouseCapture += LostCapture;
    }

    public bool SetEnabled(bool enabled)
    {
        if (disposed || enabled == Enabled) return Enabled;
        if (enabled)
        {
            if (!window.Set.AllowMove || main.State != Main.WorkingState.Nomal || FindWalk() == null) return false;
            try { Attach(); }
            catch (ArgumentException) { return false; }
            window.Fall?.Cancel(false);
            Enabled = main.ExternalMotionActive = true;
            main.CleanState();
            main.MoveTimer.Stop();
            Place();
            PlayWalk();
            elapsed.Restart();
            timer.Interval = TimeSpan.FromMilliseconds(surface == null ? 33 : 16);
            timer.Start();
        }
        else
        {
            Enabled = false;
            foreach (var image in observedImages) frameDescriptor.RemoveValueChanged(image, OnWalkFrameChanged);
            observedImages.Clear();
            CancelCorner(false);
            timer.Stop();
            pointerDown = false;
            if (main.IsMouseCaptured) main.ReleaseMouseCapture();
            main.ExternalMotionActive = false;
            main.PetGrid.RenderTransform = Transform.Identity;
            main.PetGrid2.RenderTransform = Transform.Identity;
            main.CleanState();
            main.DisplayToNomal();
            if (surface != null) window.Topmost = previousTopmost;
            surface = null;
            Rect bounds = ScreenArea;
            window.Left = Math.Clamp(window.Left, bounds.Left, Math.Max(bounds.Left, bounds.Right - window.ActualWidth));
            window.Top = Math.Clamp(window.Top, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - window.ActualHeight));
        }
        return Enabled;
    }

    // The existing controller's screen rectangle is already expressed in WPF logical units.
    private Rect ScreenArea => window.Core.Controller is MWController c && !c.IsPrimaryScreen
        ? new(c.ScreenBorder.X, c.ScreenBorder.Y, c.ScreenBorder.Width, c.ScreenBorder.Height)
        : new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    private Rect Area => surface?.Bounds ?? ScreenArea;
    private double PathMargin => (surface == null ? 250 : 125) * window.Set.ZoomLevel;

    public bool AttachWindow(WindowEdgeCandidate target, out string reason)
    {
        reason = "";
        if (disposed || !window.Set.AllowMove || main.State != Main.WorkingState.Nomal || PresentationActive)
        { reason = "请先结束工作、睡眠或说话，并开启允许移动"; return false; }
        var candidate = new WindowEdgeSurface(window, target);
        if (!candidate.Refresh(out reason)) return false;
        // Check an available starting edge before disturbing an existing movement mode.
        GravityPath? start = null;
        foreach (var edge in new[] { SupportEdge.Top, SupportEdge.Right, SupportEdge.Left, SupportEdge.Bottom })
        {
            var path = new GravityPath { Direction = Path.Direction };
            path.Attach(candidate.Bounds, 125 * window.Set.ZoomLevel,
                new(candidate.Bounds.Left + candidate.Bounds.Width / 2, candidate.Bounds.Top + candidate.Bounds.Height / 2), true);
            path.SetEdge(edge, new(candidate.Bounds.Left + candidate.Bounds.Width / 2, candidate.Bounds.Top + candidate.Bounds.Height / 2));
            if (candidate.CanOccupy(path.WindowOrigin(Anchor, window.Set.ZoomLevel), 500 * window.Set.ZoomLevel)) { start = path; break; }
        }
        if (start == null) { reason = "窗口外缘空间不足或被其他窗口遮挡，请移动窗口或缩小桌宠后重试"; return false; }
        SetEnabled(false);
        surface = candidate; previousTopmost = window.Topmost;
        try { LoadWindowWalkProfiles(NanallyMotionAssets.Left); LoadWindowWalkProfiles(NanallyMotionAssets.Right); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { surface = null; reason = "无法读取步行素材：" + exception.Message; return false; }
        if (!SetEnabled(true)) { surface = null; reason = "当前无法开始重力行走"; return false; }
        foreach (var image in window.Core.Graph!.CommUIElements.Values.OfType<Image>())
        { frameDescriptor.AddValueChanged(image, OnWalkFrameChanged); observedImages.Add(image); }
        window.Topmost = true;
        Path.SetEdge(start.Edge, start.Foot); Place(); PlayWalk();
        LastWindowStopReason = null;
        return true;
    }

    public void ShowWindowPicker()
    {
        if (picker == null)
        {
            picker = new WindowEdgePicker(window, this);
            picker.Closed += (_, _) => picker = null;
        }
        picker.Show(); picker.Activate();
    }

    private GravityCornerProfile[] LoadWindowWalkProfiles(string name)
    {
        if (walkProfiles.TryGetValue(name, out var profiles)) return profiles;
        string folder = System.IO.Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally", "motion-512-v2",
            name == NanallyMotionAssets.Left ? "walk-left" : "walk-right");
        profiles = Directory.GetFiles(folder, "*.png").OrderBy(path => path, StringComparer.Ordinal).Select(GravityCornerProfile.LoadWalk).ToArray();
        if (profiles.Length != 8) throw new InvalidDataException("Eight frozen walk frames are required.");
        walkProfiles.Add(name, profiles); return profiles;
    }

    private GravityCornerProfile CurrentWindowWalkProfile()
    {
        var visible = main.PetGrid.Visibility == Visibility.Visible ? main.PetGrid : main.PetGrid2;
        if (visible.Tag is PNGAnimation graph && (graph.GraphInfo.Name == NanallyMotionAssets.Left || graph.GraphInfo.Name == NanallyMotionAssets.Right) &&
            visible.Child is Image { Source: CroppedBitmap crop })
            return LoadWindowWalkProfiles(graph.GraphInfo.Name)[Math.Clamp(crop.SourceRect.X / crop.PixelWidth, 0, 7)];
        return LoadWindowWalkProfiles(Path.Direction > 0 ? NanallyMotionAssets.Left : NanallyMotionAssets.Right)[0];
    }

    private void OnWalkFrameChanged(object? sender, EventArgs e)
    {
        if (!Enabled || outerCorner == null || surface == null || pointerDown) return;
        var visible = main.PetGrid.Visibility == Visibility.Visible ? main.PetGrid : main.PetGrid2;
        if (!ReferenceEquals(visible.Child, sender)) return;
        // PNG changes Source synchronously on the UI dispatcher: update the sole pivot in
        // that same turn, so a new frame cannot render against the previous frame's shoe.
        cornerPose = outerCorner.Evaluate(cornerSeconds, CurrentWindowWalkProfile());
        Path.SetPose(cornerPose.Foot, cornerPose.Angle);
        if (!surface.CanOccupy(Path.WindowOrigin(Anchor, window.Set.ZoomLevel), 500 * window.Set.ZoomLevel))
        { StopWindow("当前步态的转角空间不足"); return; }
        Place();
    }

    private void StopWindow(string reason)
    {
        LastWindowStopReason = reason;
        SetEnabled(false);
    }

    private void Attach()
    {
        Point anchor = GravityPath.Rotate(Anchor, Enabled ? Path.Angle : 0);
        Point foot = new(window.Left + anchor.X * window.Set.ZoomLevel, window.Top + anchor.Y * window.Set.ZoomLevel);
        CancelCorner(true);
        Path.Attach(Area, PathMargin, foot, surface != null);
    }

    public void SelectEdge(SupportEdge edge)
    {
        // Existing tray edge commands always refer to screen-inner walking.
        if (surface != null) SetEnabled(false);
        if (!Enabled && !SetEnabled(true)) return;
        CancelCorner(false);
        Path.SetEdge(edge, new(Area.Left + Area.Width / 2, Area.Top + Area.Height / 2));
        Place();
        PlayWalk();
    }

    public void Reverse()
    {
        Path.Direction *= -1;
        // Reverse the existing timeline from its current pose. Reattaching the
        // reference anchor here would snap it to an edge during a tilted pose.
        if (Enabled && !CornerActive && !PresentationActive && !pointerDown) { Place(); PlayWalk(); }
    }

    // The corner's contact proof is tied to a fixed silhouette. Text can appear
    // immediately, but a different body pose must wait for a safe straight edge.
    public bool TryDeferInteraction(Action action)
    {
        if (!CornerActive) return false;
        deferredInteraction = action; // Latest interaction replaces an older request.
        return true;
    }

    private IGraph? FindCharacterWalk()
    {
        bool left = Path.Direction < 0 ^ surface != null;
        var graph = window.Core.Graph!.FindGraph(left ? NanallyMotionAssets.Left : NanallyMotionAssets.Right,
            AnimatType.B_Loop, window.Core.Save!.Mode);
        return graph is { IsReady: true, IsFail: false } ? graph : null;
    }
    private IGraph? FindWalk() => FindCharacterWalk() ?? window.Core.Graph!.FindGraph(
        Path.Direction < 0 ? "walk.left" : "walk.right", AnimatType.B_Loop, window.Core.Save!.Mode);

    private void PlayWalk()
    {
        IGraph? graph = FindWalk();
        if (graph == null) { SetEnabled(false); return; }
        int generation = ++animationGeneration;
        // No Move.Display callback: it would re-enable upstream climbing and translation.
        Action replay = () => window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (generation == animationGeneration && Enabled && !disposed && !pointerDown &&
                !PresentationActive && main.State == Main.WorkingState.Nomal) PlayWalk();
        }));
        var visible = main.PetGrid.Visibility == Visibility.Visible ? main.PetGrid : main.PetGrid2;
        if (ReferenceEquals(visible.Tag, graph) && graph.Control is { PlayState: true } current)
        {
            // Selecting an edge or finishing a corner must not restart the same active PNG.
            // PNG.Run would schedule a delayed stop/restart; a later request can then be
            // overwritten by that older restart, leaving its generation's replay cancelled.
            current.EndAction = replay;
            return;
        }
        main.Display(graph, replay);
    }

    public void Place()
    {
        rotation.Angle = Path.Angle;
        main.PetGrid.RenderTransform = rotation;
        main.PetGrid2.RenderTransform = rotation;
        Point origin = Path.WindowOrigin(Anchor, window.Set.ZoomLevel);
        window.Left = origin.X;
        window.Top = origin.Y;
    }

    private void Tick(object? sender, EventArgs e)
    {
        double seconds = Math.Min(elapsed.Elapsed.TotalSeconds, 0.1);
        elapsed.Restart();
        if (!Enabled) return;
        // Stop requests take priority even while a conversation or pointer pauses motion.
        if (main.State != Main.WorkingState.Nomal) { SetEnabled(false); return; }
        if (!window.Set.AllowMove) { SetEnabled(false); return; }
        if (surface != null)
        {
            if (!surface.Refresh(out string reason)) { StopWindow(reason); return; }
            if (Path.Bounds != Area || Math.Abs(Path.Margin - PathMargin) > 0.01)
            {
                // Keep the same side and normalized progress as the target moves/resizes.
                // Rebuild a corner at its safe incoming endpoint rather than applying an obsolete plan.
                SupportEdge edge = Path.Edge; double progress = Path.Length > 0 ? Path.Offset / Path.Length : 0;
                var old = Path.Bounds; var foot = Path.Foot;
                var priorTurn = outerCorner;
                double priorTurnSeconds = cornerSeconds;
                var afterTurn = deferredInteraction;
                CancelCorner(true);
                Path.Attach(Area, PathMargin, foot, true);
                Path.SetEdge(edge, new(Area.Left + (foot.X - old.Left) / old.Width * Area.Width,
                    Area.Top + (foot.Y - old.Top) / old.Height * Area.Height));
                // Preserve offset exactly for straight segments, including resize along that side.
                Path.SetEdge(edge, edge switch
                {
                    SupportEdge.Bottom => new(Area.Left + Path.Margin + Path.Length * progress, Area.Bottom),
                    SupportEdge.Right => new(Area.Right, Area.Bottom - Path.Margin - Path.Length * progress),
                    SupportEdge.Top => new(Area.Right - Path.Margin - Path.Length * progress, Area.Top),
                    _ => new(Area.Left, Area.Top + Path.Margin + Path.Length * progress)
                });
                if (priorTurn != null)
                {
                    // Preserve the turn's rotation/time progress when the whole target moves.
                    // Its direction is independent of a user's mid-turn reverse request.
                    int direction = Path.Direction;
                    Path.Direction = priorTurn.Direction;
                    // Set the incoming segment's endpoint explicitly for each orientation.
                    Point center = new(Area.Left + Area.Width / 2, Area.Top + Area.Height / 2);
                    Path.SetEdge(priorTurn.Incoming, center);
                    Path.Advance(Path.Length);
                    outerCorner = new WindowOuterCorner(CurrentWindowWalkProfile(), Path, window.Set.ZoomLevel);
                    Path.Direction = direction;
                    cornerSeconds = outerCorner.RebaseTime(priorTurn, priorTurnSeconds);
                    cornerPose = outerCorner.Evaluate(cornerSeconds, CurrentWindowWalkProfile());
                    Path.SetPose(cornerPose.Foot, cornerPose.Angle);
                    deferredInteraction = afterTurn;
                }
                if (!surface.CanOccupy(Path.WindowOrigin(Anchor, window.Set.ZoomLevel), 500 * window.Set.ZoomLevel))
                { StopWindow("窗口移动或缩放后已没有安全站位"); return; }
                Place(); if (!PresentationActive && !pointerDown) PlayWalk();
            }
            if (!pointerDown && !surface.CanOccupy(Path.WindowOrigin(Anchor, window.Set.ZoomLevel), 500 * window.Set.ZoomLevel))
            { StopWindow("目标边缘被遮挡或已移出可用屏幕"); return; }
        }
        if (surface == null && (Path.Bounds != Area || Math.Abs(Path.Margin - PathMargin) > 0.01))
        {
            try { Attach(); Place(); if (!PresentationActive && !pointerDown) PlayWalk(); }
            catch (ArgumentException) { SetEnabled(false); return; }
            return;
        }
        if (pointerDown || PresentationActive || main.ToolBar?.Visibility == Visibility.Visible) return;
        if (CornerActive)
        {
            // The walk loop keeps playing through the corner so the pet visibly walks
            // around it at constant speed instead of gliding on a frozen pose.
            if (main.DisplayType.Type == GraphType.Default) PlayWalk();
            AdvanceCorner(seconds);
            return;
        }
        if (main.DisplayType.Type == GraphType.Default) PlayWalk();
        if (main.DisplayType.Type != GraphType.Move && main.DisplayType.Name != NanallyMotionAssets.Left &&
            main.DisplayType.Name != NanallyMotionAssets.Right) return;
        Point before = Path.Foot;
        Path.Advance(70 * window.Set.ZoomLevel * seconds);
        if (surface != null && !surface.CanOccupy(Path.WindowOrigin(Anchor, window.Set.ZoomLevel), 500 * window.Set.ZoomLevel))
        { Path.SetEdge(Path.Edge, before); Path.Direction *= -1; PlayWalk(); return; }
        Place();
        if ((Path.Direction > 0 && Path.Length - Path.Offset < 0.001) ||
            (Path.Direction < 0 && Path.Offset < 0.001)) BeginCorner();
    }

    private void BeginCorner()
    {
        try
        {
            if (!CharacterWalk) { RejectCorner("The selected character has no verified Nanally shoe profile."); return; }
            var profile = surface == null ? GetCornerProfile() : CurrentWindowWalkProfile();
            if (surface != null)
            {
                var outside = new WindowOuterCorner(profile, Path, window.Set.ZoomLevel);
                for (int i = 0; i <= 90; i++)
                {
                    foreach (var frame in LoadWindowWalkProfiles(Path.Direction > 0 ? NanallyMotionAssets.Left : NanallyMotionAssets.Right))
                    {
                        var pose = outside.Evaluate(outside.Duration * i / 90, frame);
                        Point rotated = GravityPath.Rotate(Anchor, pose.Angle);
                        if (!surface.CanOccupy(pose.Foot - (Vector)rotated * window.Set.ZoomLevel, 500 * window.Set.ZoomLevel))
                        { RejectCorner("窗口转角空间不足或被遮挡"); return; }
                    }
                }
                outerCorner = outside;
                cornerSeconds = 0; cornerPose = outside.Evaluate(0);
                Path.SetPose(cornerPose.Foot, cornerPose.Angle); Place();
                return;
            }
            if (!GravityCornerPlan.TryCreate(profile, Path.Bounds, Path.Margin, Path.Edge, Path.Direction,
                    window.Set.ZoomLevel, out var plan, out string reason) || plan == null)
            { RejectCorner(reason); return; }
            corner = plan;
            cornerSeconds = 0;
            cornerPose = plan.Evaluate(0);
            Path.SetPose(cornerPose.Foot, cornerPose.Angle);
            LastCornerRejection = null;
            Place();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        { RejectCorner(exception.Message); }
    }

    private GravityCornerProfile GetCornerProfile()
    {
        string source = System.IO.Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally",
            window.Core.Save!.Mode == IGameSave.ModeType.PoorCondition
                ? System.IO.Path.Combine("character-512-v1", "poorcondition-idle-v1", "0001_1400.png")
                : System.IO.Path.Combine("idle-512-v1", "0001_1400.png"));
        if (!cornerProfiles.TryGetValue(source, out var profile))
            cornerProfiles.Add(source, profile = GravityCornerProfile.Load(source));
        return profile;
    }

    private void AdvanceCorner(double seconds)
    {
        if (outerCorner != null) { AdvanceOuterCorner(seconds); return; }
        var plan = corner!;
        bool forward = Path.Direction == plan.Direction;
        cornerSeconds = Math.Clamp(cornerSeconds + seconds * (forward ? 1 : -1), 0, plan.DurationSeconds);
        cornerPose = plan.Evaluate(cornerSeconds);
        Path.SetPose(cornerPose.Foot, cornerPose.Angle);
        Place();
        if ((forward && cornerSeconds >= plan.DurationSeconds) || (!forward && cornerSeconds <= 0))
        {
            var edge = forward ? plan.OutgoingEdge : plan.IncomingEdge;
            var foot = forward ? plan.EndFoot : plan.StartFoot;
            Action? after = deferredInteraction;
            deferredInteraction = null;
            if (forward) CompletedCornerCount++;
            CancelCorner(false);
            Path.SetEdge(edge, foot);
            Place();
            PlayWalk();
            after?.Invoke();
        }
    }

    private void AdvanceOuterCorner(double seconds)
    {
        var plan = outerCorner!;
        bool forward = Path.Direction == plan.Direction;
        cornerSeconds = Math.Clamp(cornerSeconds + seconds * (forward ? 1 : -1), 0, plan.Duration);
        cornerPose = plan.Evaluate(cornerSeconds, CurrentWindowWalkProfile());
        Point origin = cornerPose.Foot - (Vector)GravityPath.Rotate(Anchor, cornerPose.Angle) * window.Set.ZoomLevel;
        if (!surface!.CanOccupy(origin, 500 * window.Set.ZoomLevel)) { StopWindow("转角空间已被遮挡"); return; }
        Path.SetPose(cornerPose.Foot, cornerPose.Angle); Place();
        if (forward ? cornerSeconds >= plan.Duration : cornerSeconds <= 0)
        {
            var edge = forward ? plan.Outgoing : plan.Incoming;
            var foot = forward ? plan.End : plan.Start;
            var after = deferredInteraction;
            if (forward) CompletedCornerCount++;
            CancelCorner(false); Path.SetEdge(edge, foot); Place(); PlayWalk(); after?.Invoke();
        }
    }

    private void RejectCorner(string reason)
    {
        CancelCorner(false);
        RejectedCornerCount++;
        LastCornerRejection = reason;
        // All four Nanally walk states share one reference anchor; stay on this
        // safe segment and reverse instead of crossing an unsupported gap.
        Path.Direction *= -1;
        PlayWalk();
    }

    private void CancelCorner(bool keepPose)
    {
        animationGeneration++;
        corner = null;
        outerCorner = null;
        cornerPose = null;
        cornerSeconds = 0;
        deferredInteraction = null;
        if (!keepPose) Path.ClearPose();
    }

    private bool OverPet(DependencyObject? source)
    {
        while (source != null && source != main)
        {
            if (source == main.UIGrid || source == main.UIGrid_Back) return false;
            if (source == main.PetGrid || source == main.PetGrid2) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }

    private void PointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!Enabled || !OverPet(e.OriginalSource as DependencyObject)) return;
        pointerDown = true;
        dragged = false;
        pointerStart = Mouse.GetPosition(window);
        originStart = new(window.Left, window.Top);
        clickLocal = GravityPath.Rotate(e.GetPosition(main.MainGrid), -Path.Angle);
        main.CaptureMouse();
        e.Handled = true;
    }

    private void PointerMove(object sender, MouseEventArgs e)
    {
        if (!Enabled) return;
        if (!pointerDown) { if (OverPet(e.OriginalSource as DependencyObject)) e.Handled = true; return; }
        Point current = Mouse.GetPosition(window);
        Vector delta = new(window.Left + current.X - originStart.X - pointerStart.X,
            window.Top + current.Y - originStart.Y - pointerStart.Y);
        if (delta.Length > 4 && !dragged)
        {
            dragged = true;
            CancelCorner(true);
            PlayDragged(++animationGeneration);
        }
        if (dragged) { window.Left = originStart.X + delta.X; window.Top = originStart.Y + delta.Y; }
        e.Handled = true;
    }

    private void PointerUp(object sender, MouseButtonEventArgs e)
    {
        if (!Enabled || !pointerDown) return;
        FinishPointer();
        e.Handled = true;
    }

    private void LostCapture(object sender, MouseEventArgs e)
    {
        if (Enabled && pointerDown) FinishPointer();
    }

    private void FinishPointer()
    {
        pointerDown = false;
        if (main.IsMouseCaptured) main.ReleaseMouseCapture();
        if (dragged)
        {
            // Dragging deliberately releases the external attachment; do not snap back to a distant window.
            if (surface != null) { StopWindow("拖动已结束窗口附着"); return; }
            try { Attach(); Place(); PlayWalk(); }
            catch (ArgumentException) { SetEnabled(false); }
        }
        else
        {
            var config = window.Core.Graph!.GraphConfig;
            Action? interaction = new Rect(config.TouchHeadLocate, config.TouchHeadSize).Contains(clickLocal)
                ? () => main.DisplayTouchHead()
                : new Rect(config.TouchBodyLocate, config.TouchBodySize).Contains(clickLocal)
                    ? () => main.DisplayTouchBody() : null;
            if (interaction != null) { if (!TryDeferInteraction(interaction)) interaction(); }
            else if (!CornerActive) PlayWalk();
        }
    }

    private void PlayDragged(int current)
    {
        var graph = window.Core.Graph!.FindGraph("nanally.raised", AnimatType.B_Loop, window.Core.Save!.Mode);
        if (graph == null) return;
        main.Display(graph, () => window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!disposed && Enabled && pointerDown && dragged && current == animationGeneration) PlayDragged(current);
        })));
    }

    public void Dispose()
    {
        if (disposed) return;
        picker?.Close();
        SetEnabled(false);
        disposed = true;
        timer.Tick -= Tick;
        main.PreviewMouseLeftButtonDown -= PointerDown;
        main.PreviewMouseLeftButtonUp -= PointerUp;
        main.PreviewMouseMove -= PointerMove;
        main.LostMouseCapture -= LostCapture;
    }
}
