using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows;

/// <summary>Production startup, failure reporting and local lifecycle diagnostics.</summary>
internal static class ApplicationLifecycle
{
    private static string? failure;
    public static bool IsSmokeTest => false;
    public static string ReportPath => Path.Combine(ApplicationPaths.LogDirectory, "runtime-state.json");

    public static async Task RunAsync(MainWindow window, Func<Task> load)
    {
        try { await load(); }
        catch (Exception exception)
        {
            failure = exception.ToString();
            try { Write("failed", window); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            await window.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(exception.Message, ApplicationPaths.ProductName);
                System.Windows.Application.Current.Shutdown(1);
            });
        }
    }

    public static void Write(string phase, MainWindow? window = null)
    {
        var report = new
        {
            checkedAt = DateTimeOffset.Now,
            product = ApplicationPaths.ProductName,
            processId = Environment.ProcessId,
            phase,
            failure,
            installDirectory = ApplicationPaths.InstallDirectory,
            resourceDirectory = ApplicationPaths.ResourceDirectory,
            dataDirectory = ApplicationPaths.DataDirectory,
            cacheDirectory = ApplicationPaths.CacheDirectory,
            logDirectory = ApplicationPaths.LogDirectory,
            selectedPet = window?.Set?.PetGraph,
            animationRunning = window?.Main?.IsWorking ?? false,
            loadedSaveCount = window?.GameSavesData?.Statistics?.GetInt("stat_open_times") ?? 0
        };
        File.WriteAllText(ReportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static Task LoadedAsync(MainWindow window)
    {
        Write("loaded", window);
        if (App.Args.Contains("--ai-setup")) window.ChatCompanion?.Show(true);
        return Task.CompletedTask;
    }
}
