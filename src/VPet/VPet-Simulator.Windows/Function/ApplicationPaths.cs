using System;
using System.IO;
using System.Linq;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>Read-only application resources and writable per-user data are separate.</summary>
internal static class ApplicationPaths
{
    public const string ProductName = "Nanally Desktop Pet";
    public static string InstallDirectory { get; } = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    public static string ResourceDirectory => Path.Combine(InstallDirectory, "mod");
    public static string UserDirectory { get; private set; } = string.Empty;
    public static string DataDirectory => Path.Combine(UserDirectory, "Data");
    public static string CacheDirectory => Path.Combine(UserDirectory, "Cache");
    public static string LogDirectory => Path.Combine(UserDirectory, "Logs");

    public static void Initialize(string[] arguments)
    {
        // An explicit root allows isolated development and smoke tests without touching user saves.
        string? argument = arguments.SingleOrDefault(x => x.StartsWith("--data-root=", StringComparison.Ordinal));
        string root = argument == null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NanallyDesktopPet")
            : argument["--data-root=".Length..];
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("--data-root must be an absolute directory.");
        UserDirectory = Path.GetFullPath(root);
        string installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDirectory)) + Path.DirectorySeparatorChar;
        if ((Path.TrimEndingDirectorySeparator(UserDirectory) + Path.DirectorySeparatorChar)
            .StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("User data must be outside the application directory.");
        foreach (string directory in new[] { DataDirectory, CacheDirectory, LogDirectory })
            Directory.CreateDirectory(directory);

        // Keep the upstream storage API compatible, but point it at user data.
        ExtensionValue.BaseDirectory = DataDirectory;
        GraphCore.CachePath = CacheDirectory;
        Environment.CurrentDirectory = InstallDirectory;
    }
}
