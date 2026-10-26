using LinePutScript;
using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;
using static VPet_Simulator.Core.GraphInfo;
using static VPet_Simulator.Windows.Interface.Buff;
using static VPet_Simulator.Windows.Interface.Photo.UnlockCondition;

namespace VPet_Simulator.Windows;

// 一些桌宠功能的实现函数
public partial class MainWindow : IMainWindow
{
    /// <summary>
    /// 场景布局的所有权。统一处理图层顺序、宠物周围留白及桌面位置补偿。
    /// 所有方法在 UI 线程调用；同一个 PetGrid 同时只能挂载一个场景。
    /// </summary>
    public class PetSceneAttachment : IPetSceneAttachment
    {
        private readonly Grid host;
        private readonly Grid petGrid;
        private readonly UIElement?[] layers;
        private double padding;
        public Thickness OriginalMargin { get; }

        public PetSceneAttachment(Grid host, Grid petGrid, UIElement background, UIElement foreground, UIElement? overlay = null)
        {
            layers = new[] { background, foreground, overlay };
            this.host = host;
            this.petGrid = petGrid;
            OriginalMargin = petGrid.Margin;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] is not { } layer) continue;
                host.Children.Add(layer);
                Panel.SetZIndex(layer, Panel.GetZIndex(petGrid) + (i == 0 ? -1 : i));
            }
        }

        /// <summary>设置宠物四周额外的 WPF 像素边距，保持宠物在桌面上的位置。</summary>
        public void SetPadding(double value)
        {
            var margin = petGrid.Margin;
            double delta = value - padding;
            petGrid.Margin = new Thickness(margin.Left + delta, margin.Top + delta, margin.Right + delta, margin.Bottom + delta);
            if (Window.GetWindow(host) is { } window)
            {
                window.Left -= delta;
                window.Top -= delta;
            }
            padding = value;
        }

        public void Dispose()
        {
            SetPadding(0);
            foreach (var layer in layers) if (layer != null) host.Children.Remove(layer);
        }
    }

    /// <summary>
    /// 上次吃食物的时间
    /// </summary>
    public DateTime LastTakeItemTime { get; set; } = DateTime.MinValue;
    /// <summary>
    /// 当前宠物状态
    /// </summary> 
    public PetActivity Activity => Main.State switch
    {
        Main.WorkingState.Nomal => PetActivity.Idle,
        Main.WorkingState.Sleep => PetActivity.Sleep,
        Main.WorkingState.Travel => PetActivity.Travel,
        Main.WorkingState.Work => Main.NowWork?.Type switch
        {
            Work.WorkType.Study => PetActivity.Study,
            Work.WorkType.Work => PetActivity.Work,
            Work.WorkType.Play => PetActivity.Play,
            _ => PetActivity.Other
        },
        _ => PetActivity.Other
    };


    #region 本地评价统计

    private bool evaluationStatisticsInitialized;
    private long evaluationSessionSeconds;

    /// <summary>
    /// 初始化娜娜莉本地陪伴与工作统计。
    /// </summary>
    private void InitializeEvaluationStatistics()
    {
        if (evaluationStatisticsInitialized)
            return;

        evaluationStatisticsInitialized = true;
        ResetEvaluationSessionStatistics();
        Main.FunctionSpendHandle += EvaluationStatisticsCalHandle;
        Main.Event_WorkStart += EvaluationStatistics_WorkStart;
        Main.Event_WorkEnd += EvaluationStatistics_WorkEnd;
    }

    private void ResetEvaluationSessionStatistics()
    {
        evaluationSessionSeconds = 0;
        RegisterEvaluationActiveDay(DateTime.Now.Date);
    }

    /// <summary>
    /// 累计会话时长，以及每日、每月、最近 7/30 天的陪伴时长。
    /// </summary>
    private void EvaluationStatisticsCalHandle()
    {
        var statistics = GameSavesData.Statistics;
        var seconds = Math.Max(1, (int)Set.LogicInterval);
        var today = DateTime.Now.Date;

        RegisterEvaluationActiveDay(today);

        evaluationSessionSeconds += seconds;
        if (evaluationSessionSeconds > statistics[(gi64)"eval_longest_session_seconds"])
            statistics[(gi64)"eval_longest_session_seconds"] = evaluationSessionSeconds;

        statistics[(gi64)($"eval_day_{today:yyyyMMdd}")] += seconds;
        statistics[(gi64)($"eval_month_{today:yyyyMM}")] += seconds;
        statistics[(gi64)"eval_recent_7_days_seconds"] = SumEvaluationDailySeconds(today, 7);
        statistics[(gi64)"eval_recent_30_days_seconds"] = SumEvaluationDailySeconds(today, 30);
    }

    private long SumEvaluationDailySeconds(DateTime today, int days)
    {
        long result = 0;
        for (var i = 0; i < days; i++)
            result += GameSavesData.Statistics[(gi64)($"eval_day_{today.AddDays(-i):yyyyMMdd}")];
        return result;
    }

    private void RegisterEvaluationActiveDay(DateTime today)
    {
        var statistics = GameSavesData.Statistics;
        var todayValue = int.Parse($"{today:yyyyMMdd}");
        if (statistics[(gint)"eval_last_active_day"] == todayValue)
            return;

        var yesterdayValue = int.Parse($"{today.AddDays(-1):yyyyMMdd}");
        var streak = statistics[(gint)"eval_last_active_day"] == yesterdayValue
            ? statistics[(gint)"eval_active_streak"] + 1
            : 1;

        statistics[(gint)"eval_last_active_day"] = todayValue;
        statistics[(gint)"eval_active_days"]++;
        statistics[(gint)"eval_active_streak"] = streak;
        statistics[(gint)"eval_longest_active_streak"] = Math.Max(
            statistics[(gint)"eval_longest_active_streak"],
            streak
        );

        // 日统计只用于最近 7/30 天，月统计则长期保留用于年度趋势。
        var cutoff = int.Parse($"{today.AddDays(-30):yyyyMMdd}");
        foreach (var key in statistics.Data.Keys
            .Where(x => x.StartsWith("eval_day_", StringComparison.Ordinal))
            .ToList())
        {
            if (int.TryParse(key.Substring("eval_day_".Length), out var date) && date < cutoff)
                statistics.Data.Remove(key);
        }
    }

    private void EvaluationStatistics_WorkStart(Work work)
    {
        var statistics = GameSavesData.Statistics;
        var type = work.Type == Work.WorkType.Work ? "work" : "study";
        statistics[(gint)($"eval_{type}_started")]++;
        statistics[(gint)($"eval_{type}_project_{Uri.EscapeDataString(work.Name)}")]++;
        UpdateEvaluationCompletionRate(type);
    }

    private void EvaluationStatistics_WorkEnd(WorkTimer.FinishWorkInfo info)
    {
        var statistics = GameSavesData.Statistics;
        var type = info.work.Type == Work.WorkType.Work ? "work" : "study";

        if (info.Reason == WorkTimer.FinishWorkInfo.StopReason.TimeFinish)
            statistics[(gint)($"eval_{type}_completed")]++;
        UpdateEvaluationCompletionRate(type);

        var totalYield = info.count;
        if (info.Reason != WorkTimer.FinishWorkInfo.StopReason.TimeFinish)
        {
            var finishMultiplier = 1 + info.work.FinishBonus;
            if (finishMultiplier > 0)
                totalYield /= finishMultiplier;
        }

        var yieldName = type == "work" ? "eval_work_total_money" : "eval_study_total_exp";
        statistics[(gdbe)yieldName] += totalYield;
    }

    private void UpdateEvaluationCompletionRate(string type)
    {
        var statistics = GameSavesData.Statistics;
        var started = statistics[(gint)($"eval_{type}_started")];
        var completed = statistics[(gint)($"eval_{type}_completed")];
        statistics[(gdbe)($"eval_{type}_completion_rate")] = started > 0
            ? (double)completed / started
            : 0;
    }

    #endregion

    public void CheckGalleryUnlock()
    {
        var ps = Photos.FindAll(x => !x.IsUnlock && !x.UnlockAble.SellBoth && x.UnlockAble.Check(GameSavesData));
        if (ps.Count == 0) return;
        StringBuilder sb = new StringBuilder();
        foreach (Photo p in ps)
        {
            sb.Append(", ");
            p.Unlock(this);
            sb.Append(p.TranslateName);
        }
        ActivityLogs.Add(new ActivityLog("photo_unlock", sb.ToString().AsSpan(2).ToString()));
        Dispatcher.Invoke(() =>
        NoticeBox.Show(string.Concat(sb.ToString().AsSpan(2), "\n", "以上照片已解锁".Translate()), "新的照片已解锁".Translate()
        , Panuon.WPF.UI.MessageBoxIcon.Info, true, 5000));
    }
    /// <summary>
    /// 添加物品到物品栏 (自动合并)
    /// </summary>
    /// <param name="item">物品</param>
    public void ItemsAdd(Item item)
    {
        if (NanallyCharacter.IsRetiredItem(item.Name)) return;
        var sameitem = Items.Find(x => x.Name == item.Name);
        if (sameitem != null)
        {
            sameitem.Count += item.Count;
        }
        else
        {
            Items.Add(item);
        }
    }

    /// <summary>
    /// 添加效果；同一 SourceKey 只保留到期时间最晚的效果。
    /// </summary>
    /// <param name="sourcekey">效果来源的稳定标识，使用逗号添加多个效果</param>
    /// <param name="time">持续时间（小时）；小于等于 0 时不添加</param>
    /// <param name="source">效果来源描述</param>
    public void AddEffect(string sourcekey, int time, string? source)
    {
        if (string.IsNullOrWhiteSpace(sourcekey) || time <= 0) return;

        var endTime = DateTime.Now.AddHours(time);
        var effects = new List<Buff.Effect>();
        foreach (var key in sourcekey.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct())
        {
            var template = BuffEffects.Find(effect => effect.SourceKey == key);
            if (template == null) continue;

            Buff.Effect? existing = null;
            int count = 0;
            for (int i = 0; i < Buff.Effects.Count; i++)
            {
                var effect = Buff.Effects[i];
                if (effect.SourceKey != key) continue;
                count++;
                if (existing == null || effect.EndTime > existing.EndTime)
                    existing = effect;
            }
            if (existing != null && existing.EndTime >= endTime)
            {
                if (count > 1) effects.Add(existing);
                continue;
            }

            effects.Add(new Buff.Effect
            {
                SourceKey = template.SourceKey,
                EndTime = endTime,
                Description = template.Description,
                Source = source ?? template.Source,
                Name = template.Name,
                Icon = template.Icon,
                IsDebuff = template.IsDebuff,
                Activity = template.Activity,
                Reward = template.Reward,
                Strength = template.Strength,
                Food = template.Food,
                Drink = template.Drink,
                Feeling = template.Feeling,
                Health = template.Health,
                Money = template.Money,
                Experience = template.Experience,
                Likability = template.Likability,
                StrengthCost = template.StrengthCost,
                FoodCost = template.FoodCost,
                DrinkCost = template.DrinkCost,
                FeelingCost = template.FeelingCost,
                HealthCost = template.HealthCost,
                StrengthMax = template.StrengthMax,
                FeelingMax = template.FeelingMax,
                EmptyString = template.EmptyString
            });
        }
        if (effects.Count > 0) Buff.Update(effects);
    }
}
