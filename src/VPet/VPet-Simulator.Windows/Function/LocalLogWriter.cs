using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>Only the dispatcher mutates the visible collection; old entries have a fixed memory budget.</summary>
internal sealed class BoundedActivityLogs : ObservableCollection<ActivityLog>
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    internal const int Capacity = 2000;
    private int pending;
    protected override void InsertItem(int index, ActivityLog item)
    {
        if (!dispatcher.CheckAccess())
        {
            if (dispatcher.HasShutdownStarted) return;
            if (Interlocked.Increment(ref pending) > Capacity) { Interlocked.Decrement(ref pending); return; }
            dispatcher.BeginInvoke(() => { try { Add(item); } finally { Interlocked.Decrement(ref pending); } });
            return;
        }
        while (Count >= Capacity) RemoveAt(0);
        base.InsertItem(Math.Min(index, Count), item);
    }
}

/// <summary>Bounded, single disk writer. Queue saturation drops diagnostics rather than blocking the pet.</summary>
internal static class LocalLogWriter
{
    private sealed record Entry(string? Path, string? Text, TaskCompletionSource? Barrier = null);
    private static readonly Channel<Entry> queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(512)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private static readonly Task worker = Task.Run(DrainAsync);
    public static void Write(string path, string text) => queue.Writer.TryWrite(new(path, text.Length > 8192 ? text[..8192] : text));
    public static async Task FlushAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.Writer.WriteAsync(new(null, null, barrier));
        await barrier.Task;
    }
    private static async Task DrainAsync()
    {
        await foreach (var entry in queue.Reader.ReadAllAsync())
        {
            if (entry.Barrier != null) { entry.Barrier.TrySetResult(); continue; }
            try
            {
                if (File.Exists(entry.Path) && new FileInfo(entry.Path!).Length > 1024 * 1024)
                    File.Move(entry.Path!, entry.Path + ".previous", true);
                await File.AppendAllTextAsync(entry.Path!, entry.Text + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
