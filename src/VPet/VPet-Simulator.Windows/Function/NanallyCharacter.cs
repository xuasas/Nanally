using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows;

/// <summary>Chooses the bundled role once and validates its independent animation contract.</summary>
internal static class NanallyCharacter
{
    public const string Name = "nanally";
    private static readonly HashSet<string> RetiredItemNames = new(StringComparer.Ordinal)
    {
        "L徽章", "逗猫棒", "泡泡枪", "球拍", "指南针", "萝莉丝姜饼人",
        "生日蛋糕", "生日蛋糕2", "生日蛋糕3"
    };
    internal static bool IsRetiredItem(string name) => RetiredItemNames.Contains(name);

    public static bool IsNanally(PetLoader pet) => string.Equals(pet.Name, Name, StringComparison.Ordinal);

    public static PetLoader Select(MainWindow window) => Select(window.Set, window.Pets);

    internal static PetLoader Select(Setting setting, List<PetLoader> pets)
    {
        var pet = pets.Find(candidate => string.Equals(candidate.Name, Name, StringComparison.Ordinal));
        if (pet == null)
            throw new InvalidDataException($"没有找到内置娜娜莉角色。请重新部署该角色的 pet 配置与动画资源。");
        if (!pet.path.Any(Directory.Exists))
            throw new DirectoryNotFoundException($"角色 {pet.Name} 的动画目录不存在。请重新部署角色资源。");

        setting.PetGraph = pet.Name;
        setting["nanally"].SetBool("defaultRoleMigrated", true);
        return pet;
    }

    public static void ValidateGraph(GraphCore graph)
    {
        if (graph.GraphsALL.Any(item => !item.GraphInfo.Name.StartsWith("nanally.", StringComparison.Ordinal)))
            throw new InvalidDataException("娜娜莉角色配置中包含其他角色的动画。请检查独立角色资源清单。");

        foreach (IGameSave.ModeType mode in Enum.GetValues<IGameSave.ModeType>())
        {
            Require(graph, GraphType.Default, AnimatType.Single, mode);
            Require(graph, GraphType.StartUP, AnimatType.Single, mode);
            Require(graph, GraphType.Shutdown, AnimatType.Single, mode);
            foreach (GraphType type in new[] { GraphType.Touch_Head, GraphType.Touch_Body, GraphType.Sleep, GraphType.Say })
                foreach (AnimatType phase in new[] { AnimatType.A_Start, AnimatType.B_Loop, AnimatType.C_End })
                    Require(graph, type, phase, mode);

            Require(graph, GraphType.Raised_Dynamic, AnimatType.Single, mode);
            foreach (var dynamicGraph in graph.GraphsALL.Where(item => item.GraphInfo.Type == GraphType.Raised_Dynamic && item.GraphInfo.ModeType == mode))
                foreach (AnimatType phase in new[] { AnimatType.A_Start, AnimatType.B_Loop, AnimatType.C_End })
                    if (!graph.GraphsALL.Any(item => item.GraphInfo.Name == dynamicGraph.GraphInfo.Name &&
                        item.GraphInfo.Type == GraphType.Raised_Static && item.GraphInfo.Animat == phase && item.GraphInfo.ModeType == mode))
                        throw new InvalidDataException($"娜娜莉提起动画缺少 {dynamicGraph.GraphInfo.Name}/{mode}/{phase}。");
        }

        // Do not expose a work/move action whose missing loop would recurse through empty callbacks.
        int works = graph.GraphConfig.Works.RemoveAll(work => !HasPhases(graph, work.Graph,
            AnimatType.A_Start, AnimatType.B_Loop, AnimatType.C_End));
        int moves = graph.GraphConfig.Moves.RemoveAll(move => !HasPhases(graph, move.Graph, AnimatType.B_Loop));
        if (graph.GraphConfig.Data.ContainsLine("pinch") && !HasPhases(graph, "pinch", AnimatType.A_Start, AnimatType.B_Loop, AnimatType.C_End))
            graph.GraphConfig.Data.Assemblage.Remove("pinch");
        if (works != 0 || moves != 0)
            Trace.TraceWarning($"Nanally unavailable actions omitted: work={works}, move={moves}.");
        NanallyFoodAssets.RegisterAliases(graph);
    }

    private static bool HasPhases(GraphCore graph, string name, params AnimatType[] phases) =>
        Enum.GetValues<IGameSave.ModeType>().All(mode => phases.All(phase =>
            graph.GraphsALL.Any(item => item.GraphInfo.Name == name && item.GraphInfo.Animat == phase && item.GraphInfo.ModeType == mode)));

    private static void Require(GraphCore graph, GraphType type, AnimatType phase, IGameSave.ModeType mode)
    {
        if (!graph.GraphsALL.Any(item => item.GraphInfo.Type == type && item.GraphInfo.Animat == phase && item.GraphInfo.ModeType == mode))
            throw new InvalidDataException($"娜娜莉角色缺少基础动画 {type}/{mode}/{phase}。");
    }
}
