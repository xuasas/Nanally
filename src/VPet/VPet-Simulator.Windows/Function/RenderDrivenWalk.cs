using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>UI-thread translation at composition cadence. Sprite timing and the Core move timer stay unchanged.</summary>
internal sealed class RenderDrivenWalk
{
    private readonly MainWindow window;
    private readonly Stopwatch clock = new();
    private Main? owner;
    private bool attached;
    private Vector velocity;
    private Point precise, lastApplied;
    private TimeSpan? lastRender;
    internal bool Active => attached;
    public RenderDrivenWalk(MainWindow window) => this.window = window;

    public void Start(double x, double y, double interval)
    {
        Stop();
        if (!double.IsFinite(interval) || interval <= 0 || !double.IsFinite(x) || !double.IsFinite(y)) return;
        owner = window.Main;
        if (!owner.MoveTimer.Enabled || owner.ExternalMotionActive) return;
        velocity = new Vector(x * 1000 / interval, y * 1000 / interval);
        precise = lastApplied = new Point(window.Left, window.Top);
        lastRender = null; clock.Restart();
        CompositionTarget.Rendering += Render;
        attached = true;
    }

    private void Render(object? sender, EventArgs args)
    {
        if (args is RenderingEventArgs rendering)
        {
            if (lastRender == rendering.RenderingTime) return;
            lastRender = rendering.RenderingTime;
        }
        var main = owner;
        if (main == null || !ReferenceEquals(main, window.Main) || !main.MoveTimer.Enabled ||
            !main.MoveTimerSmartMove || !window.Set.AllowMove || main.ExternalMotionActive ||
            main.State != Main.WorkingState.Nomal || main.DisplayType.Type != GraphInfo.GraphType.Move)
        { Stop(); return; }

        double seconds = Math.Min(clock.Elapsed.TotalSeconds, 0.1);
        clock.Restart();
        var current = new Point(window.Left, window.Top);
        if (main.isPress || main.IsMouseCaptured || main.ConversationAnimationActive)
        { precise = lastApplied = current; return; }
        // External repositioning must replace the integration origin, rather than snapping back.
        var dpi = VisualTreeHelper.GetDpi(window);
        if (Math.Abs(current.X - lastApplied.X) > 1 / dpi.DpiScaleX + 0.01 ||
            Math.Abs(current.Y - lastApplied.Y) > 1 / dpi.DpiScaleY + 0.01) precise = current;
        precise += velocity * (seconds * window.Set.ZoomLevel);
        if (velocity.X != 0) window.Left = precise.X;
        if (velocity.Y != 0) window.Top = precise.Y;
        // Keep fractional progress independently of native HWND pixel rounding.
        lastApplied = new Point(window.Left, window.Top);
    }

    public void Stop()
    {
        if (attached) CompositionTarget.Rendering -= Render;
        attached = false; owner = null; clock.Reset(); lastRender = null;
    }
}
