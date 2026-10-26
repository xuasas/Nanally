using System.IO;
using System.Collections.Generic;
using VPet_Simulator.Core;

namespace VPet_Simulator.Windows;

/// <summary>Shared character assets for ordinary movement, gravity walking and conversation.</summary>
internal static class NanallyMotionAssets
{
    public const string Say = "nanally.say.normal";
    public const string Left = "nanally.walk.left";
    public const string Right = "nanally.walk.right";
    public static string Root => Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally", "motion-512-v2");
    public static string SayFirstFrame => Path.Combine(Root, "say-normal", "0001_120.png");
    public static string EmotionRoot => Path.Combine(ApplicationPaths.InstallDirectory, "artwork", "Nanally", "emotion-512-v2");
    public static string EmotionFirstFrame(string emotion) => emotion == "normal" ? SayFirstFrame :
        Path.Combine(EmotionRoot, "say-" + emotion, "0001_120.png");
    public static void Register(GraphCore graph)
    {
        if (File.Exists(Path.Combine(Root, "info.lps")))
            PetLoader.LoadGraph(graph, new DirectoryInfo(Root), Root);
        if (File.Exists(Path.Combine(EmotionRoot, "info.lps")))
            PetLoader.LoadGraph(graph, new DirectoryInfo(EmotionRoot), EmotionRoot);

        // Core's ordinary movement timer accepts Move graphs only. Each cycle must
        // finish so Move.Displaying can check the screen boundary before continuing.
        // Gravity walking restarts the same finite cycle in its own callback.
        foreach (string name in new[] { Left, Right })
        {
            if (!graph.GraphsList.TryGetValue(name, out var segments)) continue;
            foreach (var candidates in segments.Values)
                foreach (var candidate in candidates)
                    if (candidate is PNGAnimation png)
                    {
                        png.GraphInfo.Type = GraphInfo.GraphType.Move;
                        png.IsLoop = false;
                    }
            if (!graph.GraphsName.TryGetValue(GraphInfo.GraphType.Move, out var names))
                graph.GraphsName.Add(GraphInfo.GraphType.Move, names = new HashSet<string>());
            names.Add(name);
        }
    }
}
