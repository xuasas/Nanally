using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows.Companion;

/// <summary>Owns one short expression sequence. Never changes persistent life state or support rotation.</summary>
internal sealed class EmotionAnimation : IDisposable
{
    private readonly MainWindow window;
    private readonly Main main;
    private readonly DispatcherTimer expiry;
    private readonly MouseButtonEventHandler pointerHandler;
    private int generation;
    private bool active, disposed;
    public string LastGraph { get; private set; } = "";
    private IGraph? playingGraph;
    public bool ActualPlayerBound => playingGraph != null &&
        ((main.PetGrid.Visibility == Visibility.Visible && playingGraph.Equals(main.PetGrid.Tag)) ||
         (main.PetGrid2.Visibility == Visibility.Visible && playingGraph.Equals(main.PetGrid2.Tag)));
    public static string Alias(CompanionEmotion emotion) => "nanally.say." + emotion;

    public EmotionAnimation(MainWindow window)
    {
        this.window = window;
        main = window.Main;
        expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        expiry.Tick += Expire;
        pointerHandler = (_, _) => Stop();
        main.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, pointerHandler, true);
        main.GraphDisplayHandler += OnOtherGraph;
        // Prefer the character's static expression bases. Core owns their lifetime.
        // Common keeps these explicit AI choices out of the original random Say pool.
        string[] sources = { "self", "shining", "serious", "bubbles", "shy", "yawning" };
        foreach (CompanionEmotion emotion in Enum.GetValues<CompanionEmotion>())
        {
            string alias = Alias(emotion);
            if (window.Core.Graph!.GraphsList.ContainsKey(alias)) continue;
            string asset = Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally", "expressions-512-v1", emotion + ".png");
            if (File.Exists(asset))
            {
                foreach (IGameSave.ModeType mode in Enum.GetValues<IGameSave.ModeType>())
                    window.Core.Graph.AddGraph(new Picture(window.Core.Graph, asset,
                        new GraphInfo(alias, GraphType.Common, AnimatType.Single, mode), 250, true));
                continue;
            }
            if (window.Core.Graph.GraphsList.TryGetValue(sources[(int)emotion], out var segments))
                foreach (var graph in segments.Values.SelectMany(x => x).ToArray())
                    window.Core.Graph.AddGraph(new AliasGraph(graph, alias));
        }
    }

    internal string? CharacterAsset => playingGraph is Picture picture ? picture.Path :
        playingGraph is PNGAnimation && playingGraph.GraphInfo.Name.StartsWith("nanally.say.", StringComparison.Ordinal) ?
            NanallyMotionAssets.EmotionFirstFrame(playingGraph.GraphInfo.Name["nanally.say.".Length..]) : null;

    public void Show(CompanionReply reply, int? holdSeconds = null)
    {
        Stop();
        main.MsgBar?.Show("娜娜莉", reply.Text);
        if (main.State != Main.WorkingState.Nomal || main.isPress || main.IsMouseCaptured) return;
        int deferredGeneration = generation;
        if (window.GravityWalk?.TryDeferInteraction(() =>
            {
                if (!disposed && generation == deferredGeneration) Show(reply, holdSeconds);
            }) == true) return;
        string alias = Alias(reply.Emotion);
        IGraph? graph = Find(alias, AnimatType.A_Start) ?? Find(alias, AnimatType.B_Loop) ?? Find(alias, AnimatType.Single);
        graph ??= Find(Alias(CompanionEmotion.normal), AnimatType.A_Start) ??
            Find(Alias(CompanionEmotion.normal), AnimatType.B_Loop) ??
            Find(Alias(CompanionEmotion.normal), AnimatType.Single) ??
            window.Core.Graph!.FindGraph(window.Core.Graph.FindName(GraphType.Default), AnimatType.Single, window.Core.Save!.Mode);
        if (graph == null) return;
        active = main.ConversationAnimationActive = true;
        main.CleanState();
        if (window.GravityWalk != null) window.GravityWalk.PresentationActive = true;
        int current = ++generation;
        LastGraph = graph.GraphInfo.Name;
        Play(graph, current);
        expiry.Interval = TimeSpan.FromSeconds(holdSeconds ?? Math.Clamp(reply.Text.Length * 0.07, 2, 10));
        expiry.Start();
    }

    private IGraph? Find(string name, AnimatType type) => window.Core.Graph!.FindGraph(name, type, window.Core.Save!.Mode);
    private void Play(IGraph graph, int current)
    {
        playingGraph = graph;
        main.Display(graph, () => main.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!active || disposed || current != generation) return;
            IGraph? loop = Find(LastGraph, AnimatType.B_Loop);
            if (loop != null) Play(loop, current);
        })));
    }
    private void Expire(object? sender, EventArgs e)
    {
        expiry.Stop();
        if (!active) return;
        int current = ++generation;
        IGraph? end = Find(LastGraph, AnimatType.C_End);
        if (end == null) Stop();
        else main.Display(end, () => main.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (active && current == generation && !disposed) Stop();
        })));
    }
    private void OnOtherGraph(GraphInfo graph)
    {
        if (!active || graph.Name == LastGraph) return;
        int current = generation;
        main.Dispatcher.BeginInvoke(new Action(() => { if (current == generation) Stop(false); }));
    }
    public void Stop(bool restore = true)
    {
        generation++;
        expiry.Stop();
        if (!active) return;
        active = main.ConversationAnimationActive = false;
        if (window.GravityWalk != null) window.GravityWalk.PresentationActive = false;
        if (restore && !disposed && main.State == Main.WorkingState.Nomal) main.DisplayToNomal();
    }
    public void Dispose()
    {
        Stop();
        disposed = true;
        expiry.Tick -= Expire;
        main.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, pointerHandler);
        main.GraphDisplayHandler -= OnOtherGraph;
    }

    private sealed class AliasGraph : IGraph
    {
        private readonly IGraph source;
        public AliasGraph(IGraph source, string alias)
        {
            this.source = source;
            GraphInfo = new(alias, GraphType.Say, source.GraphInfo.Animat, source.GraphInfo.ModeType);
        }
        public GraphInfo GraphInfo { get; }
        public bool IsLoop { get => source.IsLoop; set => source.IsLoop = value; }
        public bool IsReady => source.IsReady;
        public bool IsFail => source.IsFail;
        public string FailMessage => source.FailMessage;
        public IGraph.TaskControl? Control => source.Control;
        public string? Path => source.Path;
        public void Run(Decorator parent, Action? endAction = null) => source.Run(parent, endAction);
        // Main.Display compares the requested graph to Decorator.Tag. Source.Run stores the source
        // there, so aliases must identify the same player or two layers cancel its restart callback.
        public override bool Equals(object? other) => ReferenceEquals(this, other) || ReferenceEquals(source, other) ||
            other is AliasGraph alias && ReferenceEquals(source, alias.source);
        public override int GetHashCode() => source.GetHashCode();
        public void Dispose() { } // Source graphs are owned and disposed once by GraphCore.
    }
}
