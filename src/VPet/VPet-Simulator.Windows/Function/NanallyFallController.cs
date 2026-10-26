using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows;

/// <summary>Ordinary raised release owns translation until the feet land; gravity has its own release path.</summary>
internal sealed class NanallyFallController : IDisposable
{
    internal const string FallGraph = "nanally.fall", LandGraph = "nanally.land";
    internal const double FootY = 468.75;
    private enum Phase { Idle, Release, Falling, Landing }
    private readonly MainWindow window;
    private readonly Main main;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new(), phaseClock = new();
    private readonly MouseButtonEventHandler interruptInput;
    private Phase phase;
    private IGraph? playing;
    private int generation;
    private double velocity;
    private bool disposed, ownsMovement, previousReposition;
    public bool Active => phase != Phase.Idle;
    internal string Stage => phase.ToString();
    internal string LastCancellation { get; private set; } = "";

    public NanallyFallController(MainWindow window)
    {
        this.window = window;
        main = window.Main;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Background, Tick, window.Dispatcher);
        timer.Stop();
        interruptInput = (_, _) => CancelCore(true, "pointer-input");
        main.AddHandler(UIElement.PreviewMouseDownEvent, interruptInput, true);
        main.PreviewMouseLeftButtonUp += OnRelease;
        main.PreviewMouseWheel += OnWheel;
        window.PreviewKeyDown += OnKey;
        main.GraphDisplayHandler += OnGraph;
    }

    private bool Available => !disposed && window.Set.PetGraph == NanallyCharacter.Name &&
        window.GravityWalk?.Enabled != true && main.State == Main.WorkingState.Nomal &&
        !main.ConversationAnimationActive;
    internal Rect Area => window.Core.Controller is MWController controller && !controller.IsPrimaryScreen
        ? new(controller.ScreenBorder.X, controller.ScreenBorder.Y, controller.ScreenBorder.Width, controller.ScreenBorder.Height)
        : new(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
    internal double FloorTop => Math.Max(Area.Top, Area.Bottom - FootY * window.Set.ZoomLevel);
    private IGraph? Find(string name) => window.Core.Graph!.FindGraph(name, AnimatType.Single, window.Core.Save!.Mode)
        is { IsReady: true, IsFail: false } graph ? graph : null;

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!Available || Active || main.ExternalMotionActive ||
            main.DisplayType.Type is not (GraphType.Raised_Dynamic or GraphType.Raised_Static)) return;
        int requested = ++generation;
        // Core's bubbling release clears its drag handlers and starts Raised C first.
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            bool releasedGraph = main.DisplayType.Type == GraphType.Default ||
                main.DisplayType.Type == GraphType.Raised_Static && main.DisplayType.Animat == AnimatType.C_End;
            if (requested == generation && releasedGraph && !main.isPress && !main.IsMouseCaptureWithin && Acquire())
            {
                phase = Phase.Release;
                phaseClock.Restart();
                clock.Restart();
                timer.Start();
            }
        }));
    }

    private bool Acquire()
    {
        Rect area = Area;
        double zoom = window.Set.ZoomLevel;
        if (!Available || Active || main.ExternalMotionActive || main.isPress || main.IsMouseCaptureWithin ||
            !double.IsFinite(zoom) || zoom <= 0 || area.Width < 500 * zoom || area.Height < FootY * zoom ||
            Find(FallGraph) == null || Find(LandGraph) == null) return false;
        ownsMovement = main.ExternalMotionActive = true;
        LastCancellation = "";
        previousReposition = window.Core.Controller!.RePositionActive;
        window.Core.Controller.RePositionActive = false;
        main.CleanState();
        return true;
    }


    private void BeginFall()
    {
        phase = Phase.Falling;
        phaseClock.Restart();
        velocity = 0;
        Rect area = Area;
        window.Left = Math.Clamp(window.Left, area.Left, area.Right - 500 * window.Set.ZoomLevel);
        window.Top = Math.Clamp(window.Top, area.Top, FloorTop);
        playing = Find(FallGraph);
        if (playing == null) { Cancel(); return; }
        main.Display(playing);
    }

    private void Tick(object? sender, EventArgs e)
    {
        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.05);
        clock.Restart();
        if (!Active) return;
        if (!Available || main.isPress || main.IsMouseCaptureWithin)
        { CancelCore(false, $"guard:state={main.State},press={main.isPress},capture={main.IsMouseCaptureWithin},conversation={main.ConversationAnimationActive},gravity={window.GravityWalk?.Enabled}"); return; }
        if (phase == Phase.Release)
        {
            if (main.DisplayType.Type == GraphType.Default) BeginFall();
            else if (phaseClock.Elapsed.TotalSeconds > 3) CancelCore(true, "release-timeout");
            return;
        }
        Rect area = Area;
        double zoom = window.Set.ZoomLevel;
        if (!double.IsFinite(zoom) || zoom <= 0 || area.Width < 500 * zoom || area.Height < FootY * zoom)
        { Cancel(); return; }
        window.Left = Math.Clamp(window.Left, area.Left, area.Right - 500 * zoom);
        if (phase == Phase.Landing)
        {
            window.Top = FloorTop;
            if (phaseClock.Elapsed.TotalSeconds > 5) CancelCore(true, "landing-timeout");
            return;
        }
        velocity = Math.Min(velocity + 1600 * zoom * seconds, 1300 * zoom);
        window.Top = Math.Min(FloorTop, Math.Max(area.Top, window.Top + velocity * seconds));
        if (window.Top >= FloorTop - 0.01) Land();
    }

    private void Land()
    {
        phase = Phase.Landing;
        phaseClock.Restart();
        int current = generation;
        playing = Find(LandGraph);
        if (playing == null) { Cancel(); return; }
        main.Display(playing, () => window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!disposed && current == generation && phase == Phase.Landing) CancelCore(true, "landing-completed");
        })));
    }

    private void OnGraph(GraphInfo info)
    {
        if (!Active || (phase == Phase.Release && (info.Type == GraphType.Default ||
            info.Type == GraphType.Raised_Static && info.Animat == AnimatType.C_End)) ||
            phase == Phase.Falling && info.Name == FallGraph || phase == Phase.Landing && info.Name == LandGraph) return;
        int current = generation;
        window.Dispatcher.BeginInvoke(new Action(() => { if (current == generation) CancelCore(false, "graph:" + info.Name + "/" + info.Type + "/" + info.Animat); }));
    }

    private void OnKey(object sender, KeyEventArgs e) => CancelCore(true, "key-input");
    private void OnWheel(object sender, MouseWheelEventArgs e) => CancelCore(true, "wheel-input");

    public void Cancel(bool restore = true) => CancelCore(restore, "cancel");
    private void CancelCore(bool restore, string reason)
    {
        LastCancellation = reason;
        ++generation; // Also invalidates a release queued before the next input.
        if (!Active && !ownsMovement) return;
        phase = Phase.Idle;
        timer.Stop();
        playing?.Stop(true);
        playing = null;
        if (ownsMovement)
        {
            ownsMovement = false;
            if (window.GravityWalk?.Enabled != true) main.ExternalMotionActive = false;
            window.Core.Controller!.RePositionActive = previousReposition;
        }
        if (restore && !disposed && main.State == Main.WorkingState.Nomal && !main.ConversationAnimationActive &&
            window.GravityWalk?.Enabled != true) main.DisplayToNomal();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CancelCore(false, "dispose");
        timer.Tick -= Tick;
        main.RemoveHandler(UIElement.PreviewMouseDownEvent, interruptInput);
        main.PreviewMouseLeftButtonUp -= OnRelease;
        main.PreviewMouseWheel -= OnWheel;
        window.PreviewKeyDown -= OnKey;
        main.GraphDisplayHandler -= OnGraph;
    }
}
