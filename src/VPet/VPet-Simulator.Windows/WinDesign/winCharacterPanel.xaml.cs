using LinePutScript;
using LinePutScript.Localization.WPF;
using Microsoft.Win32;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using Color = System.Windows.Media.Color;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// winCharacterPanel.xaml 的交互逻辑
    /// </summary>
    public partial class winCharacterPanel : WindowX
    {
        /// <summary>
        /// 提供给 AI 生成评价的统计摘要。
        /// 仅包含经过筛选的游戏统计和派生值，不包含完整存档或用户的应用使用记录。
        /// </summary>
        public sealed class EvaluationSummary
        {
            public EvaluationSummary(MainWindow mw)
            {
                ArgumentNullException.ThrowIfNull(mw);

                var gameSavesData = mw.GameSavesData;
                var save = gameSavesData.GameSave;
                var statistics = gameSavesData.Statistics;
                GeneratedAt = DateTime.Now;

                PetName = save.Name;
                IsSaveVerified = gameSavesData.HashCheck;
                Level = save.Level;
                LevelMax = save.LevelMax;
                TotalExpGained = save.TotalExpGained();
                Money = save.Money;
                Likability = save.Likability;
                LikabilityMax = save.LikabilityMax;
                LikabilityShare = SafeRatio(Likability, LikabilityMax);

                var birthday = gameSavesData[(gdat)"birthday"];
                if (birthday != default)
                {
                    Birthday = birthday;
                    DaysTogether = Math.Max(0, (GeneratedAt - birthday).TotalDays);
                }

                var totalSeconds = statistics[(gi64)"stat_total_time"];
                var workSeconds = statistics[(gi64)"stat_work_time"];
                var studySeconds = statistics[(gi64)"stat_study_time"];
                var sleepSeconds = statistics[(gi64)"stat_sleep_time"];
                var otherSeconds = Math.Max(0, totalSeconds - workSeconds - studySeconds - sleepSeconds);

                TotalHours = totalSeconds / 3600.0;
                WorkHours = workSeconds / 3600.0;
                StudyHours = studySeconds / 3600.0;
                SleepHours = sleepSeconds / 3600.0;
                OtherHours = otherSeconds / 3600.0;
                WorkShare = SafeRatio(workSeconds, totalSeconds);
                StudyShare = SafeRatio(studySeconds, totalSeconds);
                SleepShare = SafeRatio(sleepSeconds, totalSeconds);
                OtherShare = SafeRatio(otherSeconds, totalSeconds);
                AverageDailyHours = SafeRatio(TotalHours, DaysTogether);

                OpenCount = statistics[(gint)"stat_open_times"];
                AverageSessionMinutes = SafeRatio(totalSeconds / 60.0, OpenCount);

                TouchHeadCount = statistics[(gint)"stat_touch_head"];
                TouchBodyCount = statistics[(gint)"stat_touch_body"];
                TouchCount = TouchHeadCount + TouchBodyCount;
                TouchHeadShare = SafeRatio(TouchHeadCount, TouchCount);
                SayCount = statistics[(gint)"stat_say_times"];
                DanceCount = statistics[(gint)"stat_music"];
                MoveDistanceMeters = statistics[(gi64)"stat_move_length"] * 2.54 / 9600.0;

                PositiveExpDialogueCount = statistics[(gint)"stat_say_exp_p"];
                NegativeExpDialogueCount = statistics[(gint)"stat_say_exp_d"];
                PositiveLikabilityDialogueCount = statistics[(gint)"stat_say_like_p"];
                NegativeLikabilityDialogueCount = statistics[(gint)"stat_say_like_d"];
                PositiveMoneyDialogueCount = statistics[(gint)"stat_say_money_p"];
                NegativeMoneyDialogueCount = statistics[(gint)"stat_say_money_d"];
                PositiveDialogueCount = PositiveExpDialogueCount
                    + PositiveLikabilityDialogueCount
                    + PositiveMoneyDialogueCount;
                NegativeDialogueCount = NegativeExpDialogueCount
                    + NegativeLikabilityDialogueCount
                    + NegativeMoneyDialogueCount;
                DialogueOutcomeCount = PositiveDialogueCount + NegativeDialogueCount;
                PositiveDialogueShare = SafeRatio(PositiveDialogueCount, DialogueOutcomeCount);
                DialogueOutcomeShare = SafeRatio(DialogueOutcomeCount, SayCount);

                PurchaseCount = statistics[(gint)"stat_buytimes"];
                TotalPurchaseAmount = statistics[(gdbe)"stat_betterbuy"];
                AveragePurchaseAmount = SafeRatio(TotalPurchaseAmount, PurchaseCount);
                AutoBuyCount = statistics[(gint)"stat_autobuy"];
                AutoGiftCount = statistics[(gint)"stat_autogift"];
                AutoPurchaseShare = SafeRatio(AutoBuyCount + AutoGiftCount, PurchaseCount);
                GiftLikabilityGained = statistics[(gdbe)"stat_bb_gift_like"];
                DrugExpGained = statistics[(gdbe)"stat_bb_drug_exp"];

                SpendingByCategory = new Dictionary<string, double>
                {
                    ["special"] = statistics[(gdbe)"stat_bb_food"],
                    ["drink"] = statistics[(gdbe)"stat_bb_drink"],
                    ["drug"] = statistics[(gdbe)"stat_bb_drug"],
                    ["functional"] = statistics[(gdbe)"stat_bb_functional"],
                    ["gift"] = statistics[(gdbe)"stat_bb_gift"],
                    ["meal"] = statistics[(gdbe)"stat_bb_meal"],
                    ["snack"] = statistics[(gdbe)"stat_bb_snack"],
                };
                SpendingShareByCategory = SpendingByCategory.ToDictionary(
                    x => x.Key,
                    x => SafeRatio(x.Value, TotalPurchaseAmount)
                );
                var favoriteSpendingCategory = SpendingByCategory.MaxBy(x => x.Value);
                if (favoriteSpendingCategory.Value > 0)
                {
                    FavoriteSpendingCategory = favoriteSpendingCategory.Key;
                    FavoriteSpendingCategoryShare = SafeRatio(
                        favoriteSpendingCategory.Value,
                        TotalPurchaseAmount
                    );
                }

                var mostUsedItemCount = 0;
                foreach (var pair in statistics.Data.Where(x =>
                    x.Key.StartsWith("buy_", StringComparison.Ordinal) && x.Value != null
                ))
                {
                    var count = pair.Value!.GetInteger();
                    if (count <= 0)
                        continue;

                    var itemName = pair.Key.Substring(4);
                    var food = mw.Foods.FirstOrDefault(x => x.Name == itemName);
                    if (food == null)
                        continue;

                    DistinctPurchasedItemCount++;
                    if (count > mostUsedItemCount)
                    {
                        mostUsedItemCount = count;
                        MostPurchasedItem = food.TranslateName;
                    }
                }
                MostPurchasedItemCount = mostUsedItemCount;

                
                    SingleWorkMoneyMax = statistics[(gint)"stat_single_profit_money"];
                    SingleStudyExpMax = statistics[(gint)"stat_single_profit_exp"];
                

                LogicIntervalSeconds = mw.Set.LogicInterval;
                FullStateHours = statistics[(gint)"stat_100_all"] * LogicIntervalSeconds / 3600.0;
                FullStateShare = SafeRatio(FullStateHours, TotalHours);
                EverFeelingEmpty = statistics[(gbol)"stat_0_feel"];
                EverHungry = statistics[(gbol)"stat_0_strengthfood"];
                EverThirsty = statistics[(gbol)"stat_0_strengthdrink"];
                EverHungryAndThirsty = statistics[(gbol)"stat_0_sd_sf"];
                EverAllNeedsEmpty = statistics[(gbol)"stat_0_all"];
                EverIllWithoutMoney = statistics[(gbol)"stat_ill_nomoney"];

                UnlockedPhotoCount = mw.Photos.Count(x => x.IsUnlock);
                FavoritePhotoCount = mw.Photos.Count(x => x.IsUnlock && x.IsStar);

            }

            private static double SafeRatio(double numerator, double denominator)
            {
                return denominator > 0 ? numerator / denominator : 0;
            }

            public DateTime GeneratedAt { get; }
            public string PetName { get; }
            public bool IsSaveVerified { get; }
            public DateTime? Birthday { get; }
            public double DaysTogether { get; }

            public int Level { get; }
            public int LevelMax { get; }
            public double TotalExpGained { get; }
            public double Money { get; }
            public double Likability { get; }
            public double LikabilityMax { get; }
            public double LikabilityShare { get; }

            public double TotalHours { get; }
            public double AverageDailyHours { get; }
            public double WorkHours { get; }
            public double StudyHours { get; }
            public double SleepHours { get; }
            public double OtherHours { get; }
            public double WorkShare { get; }
            public double StudyShare { get; }
            public double SleepShare { get; }
            public double OtherShare { get; }
            public int OpenCount { get; }
            public double AverageSessionMinutes { get; }

            public int TouchHeadCount { get; }
            public int TouchBodyCount { get; }
            public int TouchCount { get; }
            public double TouchHeadShare { get; }
            public int SayCount { get; }
            public int DanceCount { get; }
            public double MoveDistanceMeters { get; }

            public int PositiveExpDialogueCount { get; }
            public int NegativeExpDialogueCount { get; }
            public int PositiveLikabilityDialogueCount { get; }
            public int NegativeLikabilityDialogueCount { get; }
            public int PositiveMoneyDialogueCount { get; }
            public int NegativeMoneyDialogueCount { get; }
            public int PositiveDialogueCount { get; }
            public int NegativeDialogueCount { get; }
            public int DialogueOutcomeCount { get; }
            public double PositiveDialogueShare { get; }
            public double DialogueOutcomeShare { get; }

            public int PurchaseCount { get; }
            public double TotalPurchaseAmount { get; }
            public double AveragePurchaseAmount { get; }
            public int AutoBuyCount { get; }
            public int AutoGiftCount { get; }
            public double AutoPurchaseShare { get; }
            public double GiftLikabilityGained { get; }
            public double DrugExpGained { get; }
            public Dictionary<string, double> SpendingByCategory { get; }
            public Dictionary<string, double> SpendingShareByCategory { get; }
            public string? FavoriteSpendingCategory { get; }
            public double FavoriteSpendingCategoryShare { get; }
            public int DistinctPurchasedItemCount { get; private set; }
            public string? MostPurchasedItem { get; private set; }
            public int MostPurchasedItemCount { get; }

            public int SingleWorkMoneyMax { get; }
            public int SingleStudyExpMax { get; }

            public double LogicIntervalSeconds { get; }
            public double FullStateHours { get; }
            public double FullStateShare { get; }
            public bool EverFeelingEmpty { get; }
            public bool EverHungry { get; }
            public bool EverThirsty { get; }
            public bool EverHungryAndThirsty { get; }
            public bool EverAllNeedsEmpty { get; }
            public bool EverIllWithoutMoney { get; }

            public int UnlockedPhotoCount { get; }
            public int FavoritePhotoCount { get; }
        }

        MainWindow mw;

        public winCharacterPanel(MainWindow mw)
        {
            this.mw = mw;
            InitializeComponent();
            Title = "面板".Translate() + ' ' + mw.PrefixSave;
            mw.Windows.Add(this);
            foreach (var v in mw.GameSavesData!.Statistics.Data)
            {
                StatList.Add(new StatInfo(v.Key, v.Value!.GetDouble()));
            }
            DataGridStatic.ItemsSource = StatList;
            mw.GameSavesData.Statistics.StatisticChanged += Statistics_StatisticChanged;


            Task.Run(Load_Log);
        }

        private void Statistics_StatisticChanged(Statistics sender, string name, SetObject? value)
        {
            if (value != null)
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var v = StatList.FirstOrDefault(x => x.StatId == name);
                        if (v != null)
                        {
                            v.StatCount = value.GetDouble();
                        }
                        else
                        {
                            StatList.Add(new StatInfo(name, value.GetDouble()));
                        }
                    }
                    catch { }
                });
        }

        private ObservableCollection<StatInfo> StatList { get; set; } = new();

        private class StatInfo : INotifyPropertyChanged
        {
            public StatInfo(string statId, double statCount)
            {
                StatId = statId;
                StatCount = statCount;
                if (statId.StartsWith("eval_day_", StringComparison.Ordinal))
                {
                    StatName = "eval_day".Translate() + '_' + statId.Substring(9);
                }
                else if (statId.StartsWith("eval_month_", StringComparison.Ordinal))
                {
                    StatName = "eval_month".Translate() + '_' + statId.Substring(11);
                }
                else if (statId.StartsWith("eval_work_project_", StringComparison.Ordinal))
                {
                    StatName = "eval_work_project".Translate() + '_'
                        + Uri.UnescapeDataString(statId.Substring(18)).Translate();
                }
                else if (statId.StartsWith("eval_study_project_", StringComparison.Ordinal))
                {
                    StatName = "eval_study_project".Translate() + '_'
                        + Uri.UnescapeDataString(statId.Substring(19)).Translate();
                }
                else if (statId.StartsWith("buy_"))
                {
                    StatName = "购买次数".Translate() + '_' + statId.Substring(4).Translate();
                }
                else if (statId.StartsWith("stat_"))
                {
                    StatName = "统计".Translate() + '_' + statId.Substring(5).Translate();
                }
                else
                {
                    StatName = statId.Translate();
                }
            }

            /// <summary>
            /// 统计ID
            /// </summary>
            public string StatId { get; set; }

            /// <summary>
            /// 统计显示名称
            /// </summary>
            public string StatName { get; set; }

            private double _statCount;
            /// <summary>
            /// 统计内容
            /// </summary>
            public double StatCount
            {
                get { return Math.Round(_statCount, 2); }
                set
                {
                    if (_statCount != value)
                    {
                        _statCount = value;
                        OnPropertyChanged(nameof(StatCount));
                    }
                }
            }
            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }
        private void TextBox_Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                DataGridStatic.ItemsSource = StatList;
            }
            else
            {
                DataGridStatic.ItemsSource = StatList.Where(
                    i =>
                        i.StatName.IndexOf(
                            textBox.Text,
                            StringComparison.InvariantCultureIgnoreCase
                        ) >= 0 || i.StatId.IndexOf(
                            textBox.Text,
                            StringComparison.InvariantCultureIgnoreCase
                        ) >= 0
                );
            }
        }

        private void WindowX_Closed(object sender, EventArgs e)
        {
            mw.GameSavesData.Statistics!.StatisticChanged -= Statistics_StatisticChanged;
            mw.Windows.Remove(this);
        }

        private void btn_r_save_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog()
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Nanally_Report.png"),
                Filter = "PNG Image File|*.png"
            };
            if (saveFileDialog.ShowDialog() != true)
                return;
            r_viewbox.ScrollToTop();
            FrameworkElement outputbox;
            if (r_output.ActualWidth > r_output_base.ActualWidth)
                outputbox = r_output;
            else
                outputbox = r_output_base;
            RenderTargetBitmap image = new RenderTargetBitmap((int)outputbox.ActualWidth, (int)outputbox.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            image.Render(outputbox);
            var path = saveFileDialog.FileName;
            using (MemoryStream ms = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(ms);
                File.WriteAllBytes(path, ms.ToArray());


                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }





        private void btn_r_genRank_Click(object sender, RoutedEventArgs e)
        {
            btn_r_genRank.IsEnabled = false;
            pb_r_genRank.Value = 0;
            pb_r_genRank.Visibility = Visibility.Visible;
            Task.Run(GenRank);
        }
        private async void GenRank()
        {
            mw.Set["v"][(gint)"rank"] = DateTime.Now.Year;


            string petname = mw.GameSavesData.GameSave.Name;
            string username = Environment.UserName;

            int timelength = mw.GameSavesData.Statistics![(gint)"stat_total_time"];
            double timelength_h = (timelength / 3600.0);
            double startdatelength = Math.Max(1.0 / 24, (DateTime.Now - mw.GameSavesData[(gdat)"birthday"]).TotalDays);
            double timelengthph = timelength_h / startdatelength;
            string timelengthphtext;
            string timelengthtext;
            int timelength_i;
            if (timelengthph < 2)
            {
                timelengthphtext = "同学".Translate();
                timelengthtext = '"' + "学长~前辈~".Translate() + '"';
                timelength_i = 1;
            }
            else if (timelengthph < 4)
            {
                timelengthphtext = "朋友".Translate();
                timelengthtext = '"' + "兄弟!".Translate() + '"';
                timelength_i = 2;
            }
            else if (timelengthph < 7)
            {
                timelengthphtext = "挚友".Translate();
                timelengthtext = '"' + "不求同年同月同日生，但求同年同月同日打开娜娜莉桌宠".Translate() + '"';
                timelength_i = 3;
            }
            else if (timelengthph < 10)
            {
                timelengthphtext = "家人".Translate();
                timelengthtext = '"' + "We are 伐木累~".Translate() + '"';
                timelength_i = 4;
            }
            else
            {
                timelengthphtext = "女鹅".Translate();
                timelengthtext = '"' + "爸妈~ 这么叫好像不太好".Translate() + '"';
                timelength_i = 5;
            }

            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 10);
            string studytext;
            int study_i;
            if (mw.GameSavesData.GameSave.Level < 20)
            {
                studytext = "相当于桌宠的小学学历哦\n\"肃清! {0}的安魂曲☆\"".Translate(petname);
                study_i = 1;
            }
            else if (mw.GameSavesData.GameSave.Level < 40)
            {
                studytext = "相当于桌宠的中学学历哦\n<高考桌宠100天>".Translate();
                study_i = 2;
            }
            else if (mw.GameSavesData.GameSave.Level < 60)
            {
                studytext = "相当于桌宠的大学学历哦\n\"大学生上课吃饭睡觉, {0}学习吃饭睡觉, {0}＝大学生\"".Translate(petname);
                study_i = 3;
            }
            else if (mw.GameSavesData.GameSave.Level < 80)
            {
                studytext = "相当于桌宠的博士学历哦\n\"大学生上课吃饭睡觉, 人家和那个带兜帽的没关系啦\"".Translate();
                study_i = 4;
            }
            else
            {
                studytext = "<娜娜莉桌宠砖家>\n\"一定是{0}干的!\"".Translate(username);
                study_i = 5;
            }

            int studyexpmax, studymoneymax;

            
                studyexpmax = mw.GameSavesData.Statistics[(gint)"stat_single_profit_exp"];
                studymoneymax = mw.GameSavesData.Statistics[(gint)"stat_single_profit_money"];
            
            double studyexpmaxrank = Math.Clamp(studyexpmax / 1000.0, 0, 1);
            double studymoneymaxrank = Math.Clamp(studymoneymax / 1000.0, 0, 1);
            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 20);
            
            string studyexptext, workmoneytext;
            int studyexp_i, workmoney_i;
            if (studyexpmaxrank < 0.25)
            {
                studyexptext = '"' + "在你这个年纪,你怎么睡得着觉的?".Translate() + '"';
                studyexp_i = 5;
            }
            else if (studyexpmaxrank < 0.4)
            {
                studyexptext = '"' + "孩子学习老不好，多半是废了，快来试试思维驰学习机".Translate() + '"';
                studyexp_i = 4;
            }
            else if (studyexpmaxrank < 0.55)
            {
                studyexptext = '"' + "孩子学习老不好，多半是废了，快来试试思维驰学习机".Translate() + '"';
                studyexp_i = 3;
            }
            else if (studyexpmaxrank < 0.75)
            {
                studyexptext = '"' + "学而不思则罔，思而不学则die".Translate() + '"';
                studyexp_i = 2;
            }
            else
            {
                studyexptext = '"' + "看我量子速读法!".Translate() + '"';
                studyexp_i = 1;
            }

            if (studymoneymaxrank < 0.25)
            {
                workmoneytext = '"' + "钱钱乃身外之物".Translate() + '"';
                workmoney_i = 4;
            }
            else if (studymoneymaxrank < 0.5)
            {
                workmoneytext = '"' + "风声雨声读书声声声入耳，日结月结次次结钱钱入账".Translate() + '"';
                workmoney_i = 3;
            }
            else if (studymoneymaxrank < 0.75)
            {
                workmoneytext = '"' + "有钱能使磨推鬼".Translate() + '"';
                workmoney_i = 2;
            }
            else
            {
                workmoneytext = '"' + "可是，我真的很需要那些钱钱!".Translate() + '"';
                workmoney_i = 1;
            }

            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 40);

            int worktime = mw.GameSavesData.Statistics[(gint)"stat_work_time"];
            double worktimeph = (double)worktime / timelength;
            double worktimephrank = double.IsFinite(worktimeph) ? Math.Clamp(worktimeph, 0, 1) : 0;
            
            string worktimephtext;
            int worktime_i;
            if (worktimephrank < 0.25)
            {
                worktimephtext = '"' + "干一天来歇一天, 能混一天是一天".Translate() + '"';
                worktime_i = 1;
            }
            else if (worktimephrank < 0.35)
            {
                worktimephtext = '"' + "早8晚5，快乐回家".Translate() + '"';
                worktime_i = 2;
            }
            else if (worktimephrank < 0.45)
            {
                worktimephtext = '"' + "早8晚5，快乐回家".Translate() + '"';
                worktime_i = 3;
            }
            else if (worktimephrank < 0.55)
            {
                worktimephtext = '"' + "早8晚5，快乐回家".Translate() + '"';
                worktime_i = 4;
            }
            else if (worktimephrank < 0.75)
            {
                worktimephtext = '"' + "加班没有加班费不是基本常识吗?".Translate() + '"';
                worktime_i = 5;
            }
            else
            {
                worktimephtext = '"' + "老板! 路灯已经准备好了!".Translate() + '"';
                worktime_i = 6;
            }

            int betterbuytimes = mw.GameSavesData.Statistics[(gint)"stat_buytimes"];
            int betterbuycount = (int)mw.GameSavesData.Statistics[(gdbe)"stat_betterbuy"];

            Food mostfood = new Food()
            {
                Name = "None",
            };

            foreach (var pair in mw.GameSavesData.Statistics.Data.Where(x => x.Key.StartsWith("buy_")).OrderByDescending(x => ((int)x.Value!)))
            {
                var fn = pair.Key.Substring(4);
                var f = mw.Foods.FirstOrDefault(x => x.Name == fn);
                if (f != null)
                {
                    mostfood = f;
                    break;
                }
            }

            string foodtext = "啥也没吃,{0}都饿坏了".Translate(petname);
            switch (mostfood.Type)
            {
                case Food.FoodType.Meal:
                    foodtext = '"' + "人是铁饭是钢, 四菜一汤吃得香".Translate() + '"';
                    break;
                case Food.FoodType.Drug:
                    foodtext = '"' + "自动购买又忘开了吧?".Translate() + '"';
                    break;
                case Food.FoodType.Drink:
                    foodtext = '"' + "多喝热水".Translate() + '"';
                    break;
                case Food.FoodType.Functional:
                    foodtext = '"' + "不是正餐买不起, 而是功能性更有性价比".Translate() + '"';
                    break;
                case Food.FoodType.Snack:
                    foodtext = '"' + "多吃零食有益心理健康".Translate() + '"';
                    break;
                case Food.FoodType.Gift:
                    foodtext = '"' + "公若不弃，{0}愿拜为义父!".Translate(petname) + '"';
                    break;
            }

            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 60);

            int autobuytimes = mw.GameSavesData.Statistics[(gint)"stat_autobuy"];
            double autobuytimesph = (double)autobuytimes / betterbuytimes;

            
            string autobuytext;
            int autobuy_i;
            if (autobuytimesph < 0.25)
            {
                autobuytext = '"' + "主人, 是担心我乱买东西嘛".Translate() + '"';
                autobuy_i = 4;
            }
            else if (autobuytimesph < 0.5)
            {
                autobuytext = '"' + "自己赚的钱自己花".Translate() + '"';
                autobuy_i = 3;
            }
            else if (autobuytimesph < 0.75)
            {
                autobuytext = '"' + "不要小看我的情报网! 你自动购买礼物没关,对不对?".Translate() + '"';
                autobuy_i = 2;
            }
            else
            {
                autobuytext = '"' + "诚招保姆,工资面议".Translate() + '"';
                autobuy_i = 1;
            }

            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 70);

            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 80);

            int like = (int)mw.GameSavesData.GameSave.Likability;
            string liketext = "";
            while (like > 100)
            {
                like -= 100;
                liketext += '\uEE0E';
            }
            while (like > 50)
            {
                like -= 50;
                liketext += '\uEE0F';
            }
            if (liketext.Length == 0)
            {
                liketext = "\uEECA";
            }

            
            await Dispatcher.InvokeAsync(() => pb_r_genRank.Value = 88);

            await Dispatcher.InvokeAsync(() =>
            {
                r_r_startday.Text = mw.GameSavesData[(gdat)"birthday"].ToLongDateString();
                r_r_startlength.Text = startdatelength.ToString("f1");
                r_r_length_h.Text = timelength_h.ToString("f1");
                r_r_lenghranktext.Text = "今天也谢谢你陪着我。";

                r_r_lengthph.Text = timelengthph.ToString("f1");
                r_r_lengthphtext.Text = timelengthphtext;
                r_r_lenghtext.Text = timelengthtext;
                r_i_timelength.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_timelength_{timelength_i}.png"));

                r_r_level.Text = mw.GameSavesData.GameSave.Level.ToString();
                r_r_exp.Text = mw.GameSavesData.GameSave.TotalExpGained().ToString("f0");
                r_r_studytime.Text = (mw.GameSavesData.Statistics[(gint)"stat_study_time"] / 60).ToString();
                r_r_studytext.Text = studytext;
                r_i_exp.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_level_{study_i}.png"));

                r_r_studyexpmax.Text = studyexpmax.ToString();
                r_r_studyexptext.Text = studyexptext;
                r_i_singleexp.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_singleexp_{studyexp_i}.png"));

                r_r_worktime.Text = (worktime / 60).ToString();
                r_r_worktimeps.Text = worktimeph.ToString("p1");
                r_r_worktext.Text = worktimephtext;
                r_i_money.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_worktime_{worktime_i}.png"));

                r_r_workmoneymax.Text = studymoneymax.ToString();
                r_r_workmoneytext.Text = workmoneytext;
                r_i_singlemoney.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_singlemoney_{workmoney_i}.png"));

                r_r_username.Text = username;
                r_r_petname.Text = r_r_petname_2.Text = r_r_petname_3.Text = r_r_petname_4.Text = petname;
                r_r_now.Text = DateTime.Now.ToShortDateString();

                r_r_betterbuytimes.Text = betterbuytimes.ToString();
                r_r_betterbuycount.Text = betterbuycount.ToString();
                r_r_betterbuymosttype.Text = mostfood.Type.ToString().Translate();
                r_r_betterbuymostitem.Text = mostfood.TranslateName;
                r_r_betterbuymosttext.Text = foodtext;
                r_i_mostfood.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_mostfood_{mostfood.Type}.png"));

                r_r_autobuy.Text = autobuytimes.ToString();
                r_r_autobuypres.Text = autobuytimesph.ToString("p1");
                r_r_autobuytext.Text = autobuytext;
                r_i_autobuy.Source = new BitmapImage(new Uri($"pack://application:,,,/Res/img/r_autobuy_{autobuy_i}.png"));


                r_r_sleeplength.Text = (mw.GameSavesData.Statistics[(gint)"stat_sleep_time"] / 3600.0).ToString("f1");
                r_r_movelength.Text = px_tocm(mw.GameSavesData.Statistics[(gi64)"stat_move_length"], out string cm);
                r_r_movelengthcm.Text = cm;
                r_r_saycount.Text = mw.GameSavesData.Statistics[(gint)"stat_say_times"].ToString();
                r_r_musiccount.Text = mw.GameSavesData.Statistics[(gint)"stat_music"].ToString();
                r_r_touchtotal.Text = (mw.GameSavesData.Statistics[(gint)"stat_touch_body"] + mw.GameSavesData.Statistics[(gint)"stat_touch_head"]).ToString();

                if (mw.GameSavesData.GameSave.Likability > 100)
                    r_i_like.Visibility = Visibility.Visible;
                else
                    r_i_like.Visibility = Visibility.Collapsed;

                r_r_opencount.Text = mw.GameSavesData.Statistics[(gint)"stat_open_times"].ToString();
                r_r_bettercount.Text = mw.GameSavesData.Statistics[(gint)"stat_100_all"].ToString();
                r_r_likecount.Text = liketext;

                r_viewbox.Visibility = Visibility.Visible;
                btn_r_genRank.IsEnabled = true;
                btn_r_save.IsEnabled = true;
                pb_r_genRank.Visibility = Visibility.Collapsed;
                Width = 800;
                Height = 800;
            });
        }
        public static string px_tocm(long px)
        {
            return px_tocm(px, out string cm) + cm;
        }
        public static string px_tocm(long px, out string cm)
        {
            if (px < 37795)
            {
                cm = "px";
                return px.ToString();
            }
            else if (px < 3779527)
            {
                cm = "cm";
                return (px * 2.54 / 96).ToString("f1");
            }
            else if (px < 377952755)
            {
                cm = "m";
                return (px * 2.54 / 9600).ToString("f1");
            }
            else
            {
                cm = "km";
                return (px * 2.54 / 9600000).ToString("f1");
            }
        }

        private void Load_Log()
        {
            string text = "";
            if (mw.Set.DeBug)
                text = string.Join('\n', mw.ActivityLogs.Select(x => x.ToString(mw.Main)).ToList());
            else
                text = string.Join('\n', mw.ActivityLogs.Where(x => x.IsDebug == false).Select(x => x.ToString(mw.Main)).ToList());
            Dispatcher.Invoke(() =>
            {
                tb_log.Text = text;
            });
            mw.ActivityLogs.CollectionChanged += ActivityLogs_CollectionChanged;
        }

        private void ActivityLogs_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e != null)
                    if (e.NewItems != null)
                    {
                        foreach (ActivityLog log in e.NewItems)
                        {
                            if (mw.Set.DeBug || log.IsDebug == false)
                            {
                                tb_log.AppendText("\n" + log.ToString(mw.Main));
                            }
                        }
                    }
            });
        }
    }
}
