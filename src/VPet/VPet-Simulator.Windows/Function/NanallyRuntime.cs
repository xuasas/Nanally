using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

public partial class MainWindow
{
    private readonly CancellationTokenSource lifetime = new();
    private readonly NanallyPersistence persistence = new();
    private long saveSequence;
    private NanallyPanel? companionSpace;
    internal void ShowNanallyPanel()
    {
        if (companionSpace == null)
        {
            companionSpace = new NanallyPanel(this) { Owner = this };
            companionSpace.Closed += (_, _) => companionSpace = null;
        }
        companionSpace.Show(); companionSpace.Activate();
    }

    private async Task RevealWhenReadyAsync(Main main)
    {
        try
        {
            while (!main.IsWorking) await Task.Delay(100, lifetime.Token);
            if (!lifetime.IsCancellationRequested) LoadingText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }

    private async Task ConstrainStartupPositionAsync()
    {
        try
        {
            await Task.Delay(100, lifetime.Token);
            double distance;
            if ((distance = Core.Controller!.GetWindowsDistanceLeft()) < 0) Left -= distance;
            if ((distance = Core.Controller.GetWindowsDistanceRight()) < 0) Left += distance;
            if ((distance = Core.Controller.GetWindowsDistanceUp()) < 0) Top -= distance;
            if ((distance = Core.Controller.GetWindowsDistanceDown()) < 0) Top += distance;
        }
        catch (OperationCanceledException) { }
    }

    private async Task LogPetSpeechAsync(SayInfo speech)
    {
        try
        {
            string text = await speech.GetSayText().WaitAsync(lifetime.Token);
            if (!lifetime.IsCancellationRequested) ActivityLogs.Add(new ActivityLog("petsay", text));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { LocalLogWriter.Write(System.IO.Path.Combine(ApplicationPaths.LogDirectory, "runtime-events.log"), "speech-log " + exception.GetType().Name); }
    }

    private async Task RunReminderAsync(int milliseconds, Action action)
    {
        try
        {
            await RuntimeDelay.RunAsync(milliseconds, lifetime.Token, action);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            LocalLogWriter.Write(System.IO.Path.Combine(ApplicationPaths.LogDirectory, "runtime-events.log"),
                $"{DateTimeOffset.Now:o} reminder {exception.GetType().Name}");
        }
    }

    private void StopRuntimeTasks()
    {
        if (!lifetime.IsCancellationRequested) lifetime.Cancel();
        AutoSaveTimer.Stop();
    }
}

internal static class RuntimeDelay
{
    internal static async Task RunAsync(int milliseconds, CancellationToken cancellation, Action action)
    {
        await Task.Delay(milliseconds, cancellation);
        cancellation.ThrowIfCancellationRequested();
        action();
    }
}
