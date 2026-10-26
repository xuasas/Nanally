using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace VPet_Simulator.Windows;

internal sealed record WindowEdgeCandidate(nint Handle, uint ProcessId, uint ThreadId, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Read-only Win32 window identity, visible frame and z-order sampling. Never moves the target.</summary>
internal sealed class WindowEdgeSurface
{
    private readonly MainWindow host;
    private readonly WindowEdgeCandidate target;
    private readonly List<Rect> occluders = new();
    private long nextOcclusionScan;
    public nint Handle => target.Handle;
    public string Title => target.Title;
    public Rect Bounds { get; private set; }
    public Rect WorkArea { get; private set; }

    public WindowEdgeSurface(MainWindow host, WindowEdgeCandidate target) { this.host = host; this.target = target; }

    public static IReadOnlyList<WindowEdgeCandidate> Enumerate()
    {
        var result = new List<WindowEdgeCandidate>();
        Native.EnumWindows((handle, _) =>
        {
            if (TryCandidate(handle, out var item)) result.Add(item!);
            return true;
        }, 0);
        return result;
    }

    public static bool TryCandidate(nint handle, out WindowEdgeCandidate? result)
    {
        result = null;
        uint thread = Native.GetWindowThreadProcessId(handle, out uint process);
        if (process == Environment.ProcessId || thread == 0 || !Usable(handle) || Native.IsZoomed(handle) ||
            Native.GetWindow(handle, 4) != 0 || ((long)Native.GetWindowLongPtr(handle, -20) & 0x80) != 0) return false;
        var title = new StringBuilder(512);
        Native.GetWindowText(handle, title, title.Capacity);
        if (string.IsNullOrWhiteSpace(title.ToString()) || ShellWindow(handle)) return false;
        result = new(handle, process, thread, title.ToString());
        return true;
    }

    public bool Refresh(out string reason)
    {
        reason = "";
        uint thread = Native.GetWindowThreadProcessId(Handle, out uint process);
        if (process == Environment.ProcessId || thread != target.ThreadId || process != target.ProcessId || !Usable(Handle))
        { reason = "目标窗口已关闭、最小化或不可见"; return false; }
        if (Native.IsZoomed(Handle)) { reason = "目标窗口已最大化"; return false; }
        if (!TryFrame(Handle, out var pixels)) { reason = "无法读取目标窗口边界"; return false; }
        var source = PresentationSource.FromVisual(host);
        if (source?.CompositionTarget == null) { reason = "桌宠显示尚未就绪"; return false; }
        var fromPixels = source.CompositionTarget.TransformFromDevice;
        Bounds = Rect.Transform(pixels, fromPixels);
        var work = System.Windows.Forms.Screen.FromHandle(Handle).WorkingArea;
        WorkArea = Rect.Transform(new Rect(work.X, work.Y, work.Width, work.Height), fromPixels);
        if (Bounds.Width <= 250 * host.Set.ZoomLevel || Bounds.Height <= 250 * host.Set.ZoomLevel)
        { reason = "目标窗口太小，请放大窗口或缩小桌宠"; return false; }
        // A short cached z-order scan keeps enumeration off the per-render hot path.
        if (Environment.TickCount64 >= nextOcclusionScan)
        {
            nextOcclusionScan = Environment.TickCount64 + 200;
            occluders.Clear();
            Native.EnumWindows((handle, _) =>
            {
                if (handle == Handle) return false;
                Native.GetWindowThreadProcessId(handle, out uint owner);
                if (owner != Environment.ProcessId && Usable(handle) && !ShellWindow(handle) && TryFrame(handle, out var frame))
                    occluders.Add(Rect.Transform(frame, fromPixels));
                return true;
            }, 0);
        }
        return true;
    }

    public bool CanOccupy(Point origin, double size)
    {
        var canvas = new Rect(origin, new Size(size, size));
        var screen = WorkArea; screen.Inflate(0.01, 0.01);
        if (!screen.Contains(canvas)) return false;
        foreach (var cover in occluders)
            if (cover.IntersectsWith(canvas)) return false;
        return true;
    }

    private static bool Usable(nint handle) => Native.IsWindow(handle) && Native.IsWindowVisible(handle) &&
        !Native.IsIconic(handle) && (Native.DwmGetWindowAttribute(handle, 14, out int cloaked, 4) != 0 || cloaked == 0);

    private static bool ShellWindow(nint handle)
    {
        var name = new StringBuilder(128); Native.GetClassName(handle, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static bool TryFrame(nint handle, out Rect rect)
    {
        // DWM uses physical pixels and excludes invisible resize borders.
        int code = Native.DwmGetWindowAttribute(handle, 9, out Native.RECT frame, Marshal.SizeOf<Native.RECT>());
        rect = Rect.Empty;
        if (code != 0 || frame.Right <= frame.Left || frame.Bottom <= frame.Top) return false;
        rect = new(frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
        return true;
    }

    internal static class Native
    {
        internal delegate bool EnumProc(nint handle, nint parameter);
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindow(nint handle);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint handle);
        [DllImport("user32.dll")] internal static extern bool IsIconic(nint handle);
        [DllImport("user32.dll")] internal static extern bool IsZoomed(nint handle);
        [DllImport("user32.dll")] internal static extern nint GetWindow(nint handle, uint command);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint handle, out uint process);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint handle, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint handle, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint handle, StringBuilder text, int count);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, int attribute, out RECT value, int size);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    }
}
