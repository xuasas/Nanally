using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>Core owns the sample graph; this controller owns temporary display and input hooks.</summary>
internal sealed class NanallyIdleTrial : IDisposable
{
    private const string Name = "nanally.default.trial";
    private readonly MainWindow window;
    private readonly MouseButtonEventHandler input;
    private PNGAnimation? graph;
    private bool loading, disposed;
    public bool Active { get; private set; }
    public NanallyIdleTrial(MainWindow window)
    {
        this.window = window;
        input = (_, _) => Stop();
        window.Main.AddHandler(UIElement.PreviewMouseDownEvent, input, true);
        window.Main.GraphDisplayHandler += OnGraph;
    }
    public async Task ToggleAsync()
    {
        if (Active) { Stop(); return; }
        try { await StartAsync(); }
        catch (Exception e) { MessageBox.Show(e.Message, "娜娜莉待机"); }
    }
    public async Task StartAsync()
    {
        if (disposed || loading || Active) return;
        CheckActivity(); loading = true;
        try
        {
            if (graph == null)
            {
                string root = Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally", "idle-512-v1");
                PetLoader.LoadGraph(window.Core.Graph!, new DirectoryInfo(root), root);
                graph = window.Core.Graph!.FindGraph(Name, GraphInfo.AnimatType.Single, IGameSave.ModeType.Nomal) as PNGAnimation
                    ?? throw new InvalidDataException("没有加载娜娜莉待机图形。");
            }
            for (int n = 0; n < 250 && !graph.IsReady && !graph.IsFail; n++) await Task.Delay(100);
            if (disposed) return;
            if (!graph.IsReady || graph.IsFail) throw new InvalidDataException("待机加载失败：" + graph.FailMessage);
            CheckActivity(); window.ChatCompanion?.Stop(); window.Main.CleanState();
            Active = window.Main.ExternalMotionActive = true;
            window.Main.Display(graph);
        }
        finally { loading = false; }
    }
    private void CheckActivity()
    {
        if (window.GravityWalk?.Enabled == true || window.Main.State != Main.WorkingState.Nomal || window.Main.ExternalMotionActive)
            throw new InvalidOperationException("请先停止工作、睡眠或重力行走，再启动待机。");
    }
    private void OnGraph(GraphInfo info)
    {
        if (Active && info.Name != Name) window.Dispatcher.BeginInvoke(new Action(() => Stop(false)));
    }
    public void Stop(bool restore = true)
    {
        if (!Active) return;
        Active = false; ((IGraph?)graph)?.Stop(true); window.Main.ExternalMotionActive = false;
        if (restore) window.Main.DisplayToNomal();
    }
    public void Dispose()
    {
        disposed = true; Stop(false);
        window.Main.RemoveHandler(UIElement.PreviewMouseDownEvent, input);
        window.Main.GraphDisplayHandler -= OnGraph;
        // Core owns disposal; never clear a PNG while its player is sleeping.
    }
}
