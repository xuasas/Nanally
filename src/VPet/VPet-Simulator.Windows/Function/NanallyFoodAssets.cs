using System;
using System.IO;
using System.Linq;
using VPet_Simulator.Core;
using static VPet_Simulator.Core.GraphInfo;

namespace VPet_Simulator.Windows;

/// <summary>Compatibility lookups share canonical Nanally graphs; they do not register a second owner.</summary>
internal static class NanallyFoodAssets
{
    internal static readonly AnimatType[] Phases =
        { AnimatType.Single, AnimatType.A_Start, AnimatType.B_Loop, AnimatType.C_End };

    internal static int FrameCount(AnimatType phase) => phase switch
    {
        AnimatType.Single => 10,
        AnimatType.B_Loop => 4,
        _ => 3
    };

    internal static void RegisterAliases(GraphCore graph)
    {
        foreach (string action in new[] { "eat", "gift", "drink" })
        {
            string canonical = "nanally." + action;
            if (!graph.GraphsList.TryGetValue(canonical, out var segments)) continue;
            foreach (IGameSave.ModeType mode in Enum.GetValues<IGameSave.ModeType>())
                foreach (AnimatType phase in Phases)
                {
                    var roots = graph.GraphsALL.Where(item => item.GraphInfo.Name == canonical &&
                        item.GraphInfo.ModeType == mode && item.GraphInfo.Animat == phase).ToArray();
                    if (roots.Length != 1 || roots[0] is not FoodAnimation food || food.IsLoop ||
                        food.Front_Lay != canonical + ".front" || food.Back_Lay != canonical + ".back" ||
                        food.Animations.Count != FrameCount(phase) || food.Animations.Any(frame => frame.Time != 120))
                        throw new InvalidDataException($"娜娜莉夹层动画缺少完整定义：{canonical}/{mode}/{phase}。");
                    foreach (string layer in new[] { food.Front_Lay, food.Back_Lay })
                    {
                        var layers = graph.GraphsALL.Where(item => item.GraphInfo.Name == layer &&
                            item.GraphInfo.ModeType == mode && item.GraphInfo.Animat == phase).ToArray();
                        if (layers.Length != 1 || layers[0] is not PNGAnimation || layers[0].IsLoop)
                            throw new InvalidDataException($"娜娜莉夹层图形缺少完整定义：{layer}/{mode}/{phase}。");
                    }
                }

            if (graph.GraphsList.TryGetValue(action, out var existing) && !ReferenceEquals(existing, segments))
                throw new InvalidDataException("娜娜莉物品动画别名冲突：" + action);
            // Food.GetGraph and DisplayFoodAnimation speak eat/gift/drink. Point those
            // dictionary keys to the same canonical objects without AddGraph, so
            // GraphsALL retains only nanally names and disposes each owner once.
            graph.GraphsList[action] = segments;
        }
    }
}
