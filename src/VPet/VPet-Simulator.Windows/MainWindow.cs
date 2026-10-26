using LinePutScript;
using LinePutScript.Converter;
using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphHelper;
using static VPet_Simulator.Core.GraphInfo;
using static VPet_Simulator.Windows.Interface.Food;
using static VPet_Simulator.Windows.Interface.Photo.UnlockCondition;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ContextMenu = System.Windows.Forms.ContextMenuStrip;
using Image = System.Windows.Controls.Image;
using Line = LinePutScript.Line;
using MenuItem = System.Windows.Forms.ToolStripMenuItem;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Timers.Timer;
using ToolBar = VPet_Simulator.Core.ToolBar;

namespace VPet_Simulator.Windows
{
    public partial class MainWindow : IMainWindow
    {

        /// <summary>
        /// 加载主题
        /// </summary>
        /// <param name="themename">主题名称</param>
        public void LoadTheme(string themename)
        {
            Theme? ctheme = Themes.Find(x => x.xName == themename);
            if (ctheme == null)
            {
                return;
            }
            Theme = ctheme;

            //加载图片包
            ImageSources.AddSources(ctheme.Images);

            //阴影颜色
            Application.Current.Resources["ShadowColor"] = Function.HEXToColor('#' + ctheme.ThemeColor[(gstr)"ShadowColor"]);

            foreach (ILine lin in ctheme.ThemeColor.Assemblage.FindAll(x => !x.Name.Contains("Color")))
                Application.Current.Resources[lin.Name] = new SolidColorBrush(Function.HEXToColor('#' + lin.info));

            //系统生成部分颜色
            Color c = Function.HEXToColor('#' + ctheme.ThemeColor["Primary"].info);
            c.A = 204;
            Application.Current.Resources["PrimaryTrans"] = new SolidColorBrush(c);
            c.A = 44;
            Application.Current.Resources["PrimaryTrans4"] = new SolidColorBrush(c);
            c.A = 170;
            Application.Current.Resources["PrimaryTransA"] = new SolidColorBrush(c);
            c.A = 238;
            Application.Current.Resources["PrimaryTransE"] = new SolidColorBrush(c);

            c = Function.HEXToColor('#' + ctheme.ThemeColor["Secondary"].info);
            c.A = 204;
            Application.Current.Resources["SecondaryTrans"] = new SolidColorBrush(c);
            c.A = 44;
            Application.Current.Resources["SecondaryTrans4"] = new SolidColorBrush(c);
            c.A = 170;
            Application.Current.Resources["SecondaryTransA"] = new SolidColorBrush(c);
            c.A = 238;
            Application.Current.Resources["SecondaryTransE"] = new SolidColorBrush(c);


            c = Function.HEXToColor('#' + ctheme.ThemeColor["DARKPrimary"].info);
            c.A = 204;
            Application.Current.Resources["DARKPrimaryTrans"] = new SolidColorBrush(c);
            c.A = 44;
            Application.Current.Resources["DARKPrimaryTrans4"] = new SolidColorBrush(c);
            c.A = 170;
            Application.Current.Resources["DARKPrimaryTransA"] = new SolidColorBrush(c);
            c.A = 238;
            Application.Current.Resources["DARKPrimaryTransE"] = new SolidColorBrush(c);
        }

        public void LoadFont(string fontname)
        {
            IFont? cfont = Fonts.Find(x => x.Name == fontname);
            if (cfont == null)
            {
                return;
            }
            var font = cfont.Font;
            Application.Current.Resources["MainFont"] = font;
            Panuon.WPF.UI.GlobalSettings.Setting.FontFamily = font;
        }


        /// <summary>
        /// 获得自动点击的文本
        /// </summary>
        /// <returns>说话内容</returns>
        public ClickText? GetClickText()
        {
            ClickText.DayTime dt;
            var now = DateTime.Now.Hour;
            if (now < 6)
                dt = ClickText.DayTime.Midnight;
            else if (now < 12)
                dt = ClickText.DayTime.Morning;
            else if (now < 18)
                dt = ClickText.DayTime.Afternoon;
            else
                dt = ClickText.DayTime.Night;

            ClickText.ModeType mt;
            switch (Core.Save!.Mode)
            {
                case IGameSave.ModeType.PoorCondition:
                    mt = ClickText.ModeType.PoorCondition;
                    break;
                default:
                case IGameSave.ModeType.Nomal:
                    mt = ClickText.ModeType.Nomal;
                    break;
                case IGameSave.ModeType.Happy:
                    mt = ClickText.ModeType.Happy;
                    break;
                case IGameSave.ModeType.Ill:
                    mt = ClickText.ModeType.Ill;
                    break;
            }
            var list = ClickTexts.FindAll(x => x.DaiTime.HasFlag(dt) && x.Mode.HasFlag(mt) && x.CheckState(Main));
            if (list.Count == 0)
                return null;
            return list[Function.Rnd.Next(list.Count)];
        }
        private Image? hashcheckimg;
        private BuffViewer? buffViewer;

        /// <summary>
        /// 关闭该玩家的HashCheck检查
        /// 如果你的mod属于作弊mod/有作弊内容,请在作弊前调用这个方法
        /// </summary>
        public void HashCheckOff()
        {
            HashCheck = false;
        }
        /// <summary>
        /// 存档 Hash检查 是否通过
        /// </summary>
        public bool HashCheck
        {
            get => GameSavesData.HashCheck;
            set
            {
                if (!value)
                {
                    GameSavesData.HashCheckOff();
                }
                Main?.Dispatcher.Invoke(() =>
                {
                    if (Main.ToolBar != null && buffViewer == null)
                    {
                        buffViewer = new BuffViewer(this);
                        Main.ToolBar.gdPanel.Children.Add(buffViewer.Entry);
                    }
                    // HashCheck 图标隐藏后，增减益移到面板最右侧。
                    if (buffViewer != null)
                        buffViewer.Entry.Margin = new Thickness(0, 0, GameSavesData.HashCheck ? 70 : 0, 0);
                    if (GameSavesData.HashCheck)
                    {
                        if (hashcheckimg == null)
                        {
                            hashcheckimg = new Image();
                            int hours = GameSavesData.Statistics[(gint)"stat_total_time"] / 3600;

                            if (hours < 10)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash.png");
                            else if (hours < 50)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash0.png");
                            else if (hours < 100)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash1.png");
                            else if (hours < 200)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash2.png");
                            else if (hours < 500)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash3.png");
                            else if (hours < 1000)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash4.png");
                            else if (hours < 2000)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash5.png");
                            else if (hours < 5000)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash6.png");
                            else if (hours < 10000)
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash7.png");
                            else
                                hashcheckimg.Source = ImageResources.NewSafeBitmapImage("pack://application:,,,/Res/hash8.png");


                            hashcheckimg.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                            hashcheckimg.ToolTip = "是没有修改过存档/使用超模MOD的玩家专属标志".Translate() + ' ' + ((int)(Math.Sqrt(hours))).ToString("X");
                            hashcheckimg.Width = 64;
                            hashcheckimg.Height = 64;
                            Grid.SetColumn(hashcheckimg, 4);
                            Grid.SetRowSpan(hashcheckimg, 2);
                            if (Main.ToolBar != null)
                                Main.ToolBar.gdPanel.Children.Add(hashcheckimg);
                        }
                    }
                    else
                    {
                        if (hashcheckimg != null)
                        {
                            if (Main.ToolBar != null)
                                Main.ToolBar.gdPanel.Children.Remove(hashcheckimg);
                            hashcheckimg = null;
                        }
                    }
                });

            }
        }
        public void SetZoomLevel(double zl)
        {
            Set.ZoomLevel = zl;
            //this.Height = 500 * zl;
            MGrid.Width = 500 * zl;
            if (petHelper != null)
            {
                petHelper.Width = 50 * zl;
                petHelper.Height = 50 * zl;
                petHelper.ReloadLocation();
            }
        }



        //private DateTime timecount = DateTime.Now;
        /// <summary>
        /// 保存设置
        /// </summary>
        public void Save()
        {
            if (Set == null || GameSavesData == null) return;
            // Read mutable game/UI state only on the dispatcher. Commit never holds a lock while dispatching.
            var snapshot = Dispatcher.CheckAccess() ? CaptureSave() : Dispatcher.Invoke(CaptureSave);
            persistence.Commit(snapshot);
        }
        private SaveSnapshot CaptureSave()
        {
            ScheduleTask?.Save();
            Buff?.Save();
            foreach (var key in GameSavesData.Data.Assemblage.Keys.Where(x => x.StartsWith("item")).ToArray())
                GameSavesData.Data.Remove(key);
            for (int i = 0; i < Items.Count; i++)
                GameSavesData.Data.Add(LPSConvert.SerializeObjectToLine<Line>(Items[i], "item" + i));
            int number = Set.SaveTimesPP;
            Set["betterbuy"]["star"].info = string.Join(",", Foods.Where(x => x.Star).Select(x => x.Name));
            Set.StartRecordLastPoint = new Point(Left, Top);
            var game = Core?.Save == null ? null : GameSavesData.ToLPS();
            return new SaveSnapshot(++saveSequence, ExtensionValue.BaseDirectory, PrefixSave, number,
                Set.BackupSaveMaxNum, Set.ToString(), game?.ToString(), game == null ? 0 : Math.Abs(game.GetHashCode() % 255));
        }
        /// <summary>
        /// 重载DIY按钮区域
        /// </summary>
        public void LoadDIY()
        {
            Main.ToolBar!.MenuDIY.Items.Clear();

            if (App.MutiSaves.Count > 1)
            {
                var list = App.MutiSaves.ToList();
                foreach (var win in App.MainWindows)
                {
                    list.Remove(win.PrefixSave);
                }
                list.Remove(PrefixSave);
                if (list.Count > 0)
                {
                    var menuItem = new System.Windows.Controls.MenuItem()
                    {
                        Header = "桌宠多开".Translate(),
                        HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                    };
                    foreach (var win in list)
                    {
                        var mo = new System.Windows.Controls.MenuItem()
                        {
                            Header = win.Translate(),
                            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                        };
                        mo.Click += (s, e) =>
                        {
                            if (App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == win) == null)
                            {
                                new MainWindow(win, this).Show();
                            }
                            menuItem.Items.Remove(s);
                        };
                        menuItem.Items.Add(mo);
                    }
                    Main.ToolBar.MenuDIY.Items.Add(menuItem);
                }
            }

            foreach (ISub sub in Set["diy"])
                Main.ToolBar.AddMenuButton(ToolBar.MenuType.DIY, sub.Name, () =>
                {
                    Main.ToolBar.Visibility = Visibility.Collapsed;
                    RunDIY(sub.Info);
                });

            Main.ToolBar.AddMenuButton(ToolBar.MenuType.DIY, "打开陪伴空间", ShowNanallyPanel);
            Main.ToolBar.MenuDIY.Header = "空间";
            Main.ToolBar.LoadDIY();
        }
        /// <summary>
        /// 加载帮助器
        /// </summary>
        public void LoadPetHelper()
        {
            petHelper = new PetHelper(this);
            petHelper.Show();
        }

        public void RunDIY(string content)
        {
            if (content.Contains(@":\"))
            {
                try
                {
                    if (!Set["v"][(gbol)"rundiy"])
                    {
                        MessageBoxX.Show("由于操作系统的设计，通过我们软件启动的程序可能会在任务管理器中归类为我们软件的子进程，这可能导致CPU/内存占用显示较高".Translate(),
                            "关于CPU/内存占用显示较高的一次性提示".Translate());
                        Set["v"][(gbol)"rundiy"] = true;
                    }
                    ProcessStartInfo startInfo = new ProcessStartInfo();
                    startInfo.FileName = content;
                    startInfo.UseShellExecute = false;
                    Process.Start(startInfo);
                }
                catch
                {
                    try
                    {
                        try
                        {
                            Process.Start(content);
                        }
                        catch
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = content,
                                UseShellExecute = true
                            };
                            Process.Start(psi);
                        }
                    }
                    catch (Exception e)
                    {
                        MessageBoxX.Show("快捷键运行失败:无法运行指定内容".Translate() + '\n' + e.Message);
                    }
                }
            }
            else if (content.Contains("://"))
            {
                try
                {
                    ExtensionFunction.StartURL(content);
                }
                catch (Exception e)
                {
                    MessageBoxX.Show("快捷键运行失败:无法运行指定内容".Translate() + '\n' + e.Message);
                }
            }
            else
            {
                try
                {
                    SendKeys.SendWait(content);
                }
                catch (Exception e)
                {
                    MessageBoxX.Show("快捷键运行失败:无法运行指定内容".Translate() + '\n' + e.Message);
                }
            }
        }

        public void ShowSetting(int page = -1)
        {
            if (page >= 0 && page < winSetting!.MainTab.Items.Count)
                winSetting!.MainTab.SelectedIndex = page;
            winSetting!.Show();
        }
        public void ShowWorkMenu(Work.WorkType type)
        {
            if (winWorkMenu == null)
            {
                winWorkMenu = new winWorkMenu(this, type);
                winWorkMenu.Show();
            }
            else
            {
                winWorkMenu.LsbCategory.SelectedIndex = (int)type;
                winWorkMenu.Focus();
                winWorkMenu.Topmost = true;
            }
        }
        public void ShowBetterBuy(Food.FoodType type)
        {
            winBetterBuy!.Show(type);
        }
        
        int lowstrengthAskCountFood = 20;
        int lowstrengthAskCountDrink = 20;
        private void lowStrength()
        {
            var strengthMax = Core.Save!.StrengthMax;

            if (Set.AutoBuy && Core.Save!.Money >= 100)
            {
                var havemoney = Core.Save!.Money * 0.8;
                List<Food> food = Foods.FindAll(x => x.Price >= 2 && x.Health >= -5 && x.Exp >= -10 && x.Likability >= 0 && x.Price < havemoney //桌宠不吃负面的食物
                 && !x.IsOverLoad() // 不吃超模食物
                );

                if ((Core.Save!.StrengthFood + Core.Save!.StoreStrengthFood) < strengthMax * 0.70)
                {//饿了就该吃正餐
                    food = food.FindAll(x => x.Type == Food.FoodType.Meal && x.StrengthFood > Math.Min(strengthMax * 0.20, 100));
                    if (food.Count == 0)
                        return;
                    var item = food[Function.Rnd.Next(food.Count)];
                    Core.Save!.Money -= item.Price * 1.2;
                    TakeItemHandle(item, 1, "autofood");
                    TakeItem(item);
                    GameSavesData.Statistics![(gint)"stat_autobuy"]++;
                    Main.Display(item.GetGraph(), item.ImageSource!, Main.DisplayToNomal);
                }
                else if ((Core.Save!.StrengthDrink + Core.Save!.StoreStrengthDrink) < strengthMax * 0.70)
                {
                    food = food.FindAll(x => x.Type == Food.FoodType.Drink && x.StrengthDrink > Math.Min(strengthMax * 0.20, 50));
                    if (food.Count == 0)
                        return;
                    var item = food[Function.Rnd.Next(food.Count)];
                    Core.Save!.Money -= item.Price * 1.2;
                    TakeItemHandle(item, 1, "autodrink");
                    TakeItem(item);
                    GameSavesData.Statistics![(gint)"stat_autobuy"]++;
                    Main.Display(item.GetGraph(), item.ImageSource, Main.DisplayToNomal);
                }
                else if (Core.Save!.Feeling < Core.Save!.FeelingMax * 0.50)
                {
                    if (Set.AutoGift)
                    {
                        food = food.FindAll(x => x.Type == Food.FoodType.Gift && x.Feeling > Math.Min(Core.Save!.FeelingMax * 0.10, 50));
                        if (food.Count == 0)
                            return;
                    }
                    else // 没有自动购买礼物的可以试试自动购买零食能加点是一点
                    {
                        food = food.FindAll(x => x.Type == Food.FoodType.Snack && x.Feeling > Math.Min(Core.Save!.FeelingMax * 0.10, 40));
                        if (food.Count == 0)
                            return;
                    }
                    var item = food[Function.Rnd.Next(food.Count)];
                    Core.Save!.Money -= item.Price * 1.2;
                    TakeItemHandle(item, 1, "autofeel");
                    TakeItem(item);
                    GameSavesData.Statistics![(gint)"stat_autogift"]++;
                    Main.Display(item.GetGraph(), item.ImageSource, Main.DisplayToNomal);
                }
            }
            else if (Core.Save!.Mode == IGameSave.ModeType.Happy || Core.Save!.Mode == IGameSave.ModeType.Nomal)
            {
                if (Core.Save!.StrengthFood < strengthMax * 0.70 && Function.Rnd.Next(lowstrengthAskCountFood--) == 0)
                {
                    lowstrengthAskCountFood = Set.InteractionCycle;
                    var like = Core.Save!.Likability < 40 ? 0 : (Core.Save!.Likability < 70 ? 1 : (Core.Save!.Likability < 100 ? 2 : 3));
                    var txt = LowFoodText.FindAll(x => x.Mode == LowText.ModeType.H && (int)x.Like <= like);
                    if (txt.Count != 0)
                        if (Core.Save!.StrengthFood > strengthMax * 0.60)
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.L);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                        else if (Core.Save!.StrengthFood > strengthMax * 0.40)
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.M);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                        else
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.S);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                    Main.DisplayStopForce(() => Main.Display(GraphType.Switch_Hunger, AnimatType.Single, Main.DisplayToNomal));
                    return;
                }
                if (Core.Save!.StrengthDrink < strengthMax * 0.70 && Function.Rnd.Next(lowstrengthAskCountDrink--) == 0)
                {
                    lowstrengthAskCountDrink = Set.InteractionCycle;
                    var like = Core.Save!.Likability < 40 ? 0 : (Core.Save!.Likability < 70 ? 1 : (Core.Save!.Likability < 100 ? 2 : 3));
                    var txt = LowDrinkText.FindAll(x => x.Mode == LowText.ModeType.H && (int)x.Like <= like);
                    if (txt.Count != 0)
                        if (Core.Save!.StrengthDrink > strengthMax * 0.60)
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.L);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                        else if (Core.Save!.StrengthDrink > strengthMax * 0.40)
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.M);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                        else
                        {
                            txt = txt.FindAll(x => x.Strength == LowText.StrengthType.S);
                            if (txt.Count != 0)
                                Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                        }
                    Main.DisplayStopForce(() => Main.Display(GraphType.Switch_Thirsty, AnimatType.Single, Main.DisplayToNomal));
                    return;
                }
            }
            else
            {

                if (Core.Save!.StrengthFood < strengthMax * 0.60 && Function.Rnd.Next(lowstrengthAskCountFood--) == 0)
                {
                    lowstrengthAskCountFood = Set.InteractionCycle;
                    var like = Core.Save!.Likability < 40 ? 0 : (Core.Save!.Likability < 70 ? 1 : (Core.Save!.Likability < 100 ? 2 : 3));
                    var txt = LowFoodText.FindAll(x => x.Mode == LowText.ModeType.L && (int)x.Like < like);
                    if (Core.Save!.StrengthFood > strengthMax * 0.40)
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.L);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    else if (Core.Save!.StrengthFood > strengthMax * 0.20)
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.M);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    else
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.S);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    Main.DisplayStopForce(() => Main.Display(GraphType.Switch_Hunger, AnimatType.Single, Main.DisplayToNomal));
                    return;
                }
                if (Core.Save!.StrengthDrink < strengthMax * 0.60 && Function.Rnd.Next(lowstrengthAskCountDrink--) == 0)
                {
                    lowstrengthAskCountDrink = Set.InteractionCycle;
                    var like = Core.Save!.Likability < 40 ? 0 : (Core.Save!.Likability < 70 ? 1 : (Core.Save!.Likability < 100 ? 2 : 3));
                    var txt = LowDrinkText.FindAll(x => x.Mode == LowText.ModeType.L && (int)x.Like < like);
                    if (Core.Save!.StrengthDrink > strengthMax * 0.40)
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.L);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    else if (Core.Save!.StrengthDrink > strengthMax * 0.20)
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.M);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    else
                    {
                        txt = txt.FindAll(x => x.Strength == LowText.StrengthType.S);
                        if (txt.Count != 0)
                            Main.Say(txt[Function.Rnd.Next(txt.Count)].TranslateTextConvert(Main));
                    }
                    Main.DisplayStopForce(() => Main.Display(GraphType.Switch_Thirsty, AnimatType.Single, Main.DisplayToNomal));
                    return;
                }
            }


        }
        /// <summary>
        /// 事件:使用东西 (所有使用东西都会触发)
        /// </summary>
        public event Action<Food>? Event_TakeItem;
        /// <summary>
        /// 事件:使用东西 (仅 自动购买/更好买 调用) int: 个数 string: 来源
        /// betterbuy: 更好买手动购买
        /// auto*: 自动购买 (autofood/autodrink/autofeel) 因为饿了/渴了/心情不好
        /// friend: 朋友赠送 (访客表)
        /// *: 其他MOD调用
        /// </summary>
        public event Action<Food, int, string>? Event_TakeItemHandle;
        /// <summary>
        /// 呼叫事件 Event_TakeItemHandle
        /// </summary>
        /// <param name="item">物品</param>
        /// <param name="count">个数</param>
        /// <param name="from">来源</param>
        public void TakeItemHandle(Food item, int count, string from)
        {
            Event_TakeItemHandle?.Invoke(item, count, from);
        }
        /// <summary>
        /// 使用/食用物品 (不包括显示动画)
        /// </summary>
        /// <param name="item">物品</param>
        public void TakeItem(Food item)
        {
            //获取吃腻时间
            Main.LastInteractionTime = DateTime.Now;
            DateTime now = DateTime.Now;
            DateTime eattime = GameSavesData["buytime"].GetDateTime(item.Name, now);
            double eattimes = 0;
            if (eattime > now)
            {
                eattimes = (eattime - now).TotalHours;
            }
            //结算Buff
            AddEffect(item.Buff, item.BuffTime, item.Type.ToString().Translate() + ' ' + item.TranslateName);
            double eatuseps;
            if (item.Type == FoodType.Gift)
                eatuseps = Math.Max(0.5, 1 - eattimes * eattimes * 0.01);
            else
                eatuseps = Math.Max(0.5, 1 - eattimes * eattimes * 0.02);
            //开始加点
            var multipliers = Buff.GetAttributeMultipliers(PetActivity.TakeItem);
            var save = Core.Save!;
            // 食物的负属性保留原值，不参与获取类 Buff；延迟补充部分只在这里计算一次倍率。
            save.Exp += item.Exp * eatuseps * (item.Exp > 0 ? multipliers.Experience : 1);
            var strength = item.Strength / 2 * eatuseps * (item.Strength > 0 ? multipliers.Strength : 1);
            save.StrengthChange(strength);
            save.StoreStrength += strength;
            var food = item.StrengthFood / 2 * eatuseps * (item.StrengthFood > 0 ? multipliers.Food : 1);
            save.StrengthChangeFood(food);
            save.StoreStrengthFood += food;
            var drink = item.StrengthDrink / 2 * eatuseps * (item.StrengthDrink > 0 ? multipliers.Drink : 1);
            save.StrengthChangeDrink(drink);
            save.StoreStrengthDrink += drink;
            save.FeelingChange(item.Feeling * eatuseps * (item.Feeling > 0 ? multipliers.Feeling : 1));
            save.Health += item.Health * eatuseps * (item.Health > 0 ? multipliers.Health : 1);
            save.Likability += item.Likability * eatuseps * (item.Likability > 0 ? multipliers.Likability : 1);
            //吃腻了
            eattimes += Math.Max(0.5, Math.Min(4, 2 - (item.Likability + item.Feeling / 2) / 5));
            GameSavesData["buytime"].SetDateTime(item.Name, now.AddHours(eattimes));
            //通知
            item.LoadEatTimeSource(this);
            item.NotifyOfPropertyChange("Description");

            ////吃完东西记得计算下状态
            //Core.Save!.Mode = Core.Save!.CalMode();
            //统计
            if (GameSavesData.Statistics == null)
                return;
            GameSavesData.Statistics[(gint)"stat_buytimes"]++;
            GameSavesData.Statistics[(gint)("buy_" + item.Name)]++;
            GameSavesData.Statistics[(gdbe)"stat_betterbuy"] += item.Price;
            switch (item.Type)
            {
                case Food.FoodType.Food:
                    GameSavesData.Statistics[(gdbe)"stat_bb_food"] += item.Price;
                    break;
                case Food.FoodType.Drink:
                    GameSavesData.Statistics[(gdbe)"stat_bb_drink"] += item.Price;
                    break;
                case Food.FoodType.Drug:
                    GameSavesData.Statistics[(gdbe)"stat_bb_drug"] += item.Price;
                    GameSavesData.Statistics[(gdbe)"stat_bb_drug_exp"] += item.Exp;
                    break;
                case Food.FoodType.Snack:
                    GameSavesData.Statistics[(gdbe)"stat_bb_snack"] += item.Price;
                    break;
                case Food.FoodType.Functional:
                    GameSavesData.Statistics[(gdbe)"stat_bb_functional"] += item.Price;
                    break;
                case Food.FoodType.Meal:
                    GameSavesData.Statistics[(gdbe)"stat_bb_meal"] += item.Price;
                    break;
                case Food.FoodType.Gift:
                    GameSavesData.Statistics[(gdbe)"stat_bb_gift"] += item.Price;
                    GameSavesData.Statistics[(gdbe)"stat_bb_gift_like"] += item.Likability;
                    break;
            }

            Event_TakeItem?.Invoke(item);
        }

        public void RunAction(string action)
        {
            switch (action)
            {
                case "DisplayNomal":
                    Main.DisplayNomal();
                    break;
                case "DisplayToNomal":
                    Main.DisplayToNomal();
                    break;
                case "DisplayTouchHead":
                    Main.DisplayTouchHead();
                    break;
                case "DisplayTouchBody":
                    Main.DisplayTouchBody();
                    break;
                case "DisplayIdel":
                    Main.DisplayIdel();
                    break;
                case "DisplayIdel_StateONE":
                    Main.DisplayIdel_StateONE();
                    break;
                case "DisplaySleep":
                    Main.DisplaySleep();
                    break;
                case "DisplayRaised":
                    Main.DisplayRaised();
                    break;
                case "DisplayMove":
                    Main.DisplayMove();
                    break;
            }
        }
        /// <summary>
        /// </summary>
        
        /// <summary>
        /// 计算统计数据
        /// </summary>
        private void StatisticsCalHandle()
        {
            var stat = GameSavesData.Statistics;
            if (stat == null) return;
            var save = (IGameSave)Core.Save!;
            stat["stat_money"] = (SetObject)save.Money;
            stat["stat_level"] = save.Level;
            stat["stat_likability"] = save.Likability;

            stat[(gi64)"stat_total_time"] += (int)Set.LogicInterval;
            switch (Main.State)//TODO: 使用
            {
                case Main.WorkingState.Work:
                    if (Main.NowWork?.Type == Work.WorkType.Work)
                        stat[(gi64)"stat_work_time"] += (int)Set.LogicInterval;
                    else
                        stat[(gi64)"stat_study_time"] += (int)Set.LogicInterval;
                    break;
                case Main.WorkingState.Sleep:
                    stat[(gi64)"stat_sleep_time"] += (int)Set.LogicInterval;
                    break;
            }
            if (save.Mode == IGameSave.ModeType.Ill)
            {
                if (save.Money < 100)
                    stat["stat_ill_nomoney"] = 1;
            }
            if (save.Money < save.Level)
            {
                stat["stat_level_g_money"] = 1;
            }
            if (save.Feeling < 1)
            {
                stat["stat_0_feel"] = 1;
                if (save.StrengthDrink < 1)
                    stat["stat_0_f_sd"] = 1;
            }
            if (save.Strength < 1 && save.Feeling < 1 && save.StrengthFood < 1 && save.StrengthDrink < 1)
                stat["stat_0_all"] = 1;
            if (save.StrengthFood < 1)
                stat["stat_0_strengthfood"] = 1;
            if (save.StrengthDrink < 1)
            {
                stat["stat_0_strengthdrink"] = 1;
                if (save.StrengthFood < 1)
                    stat["stat_0_sd_sf"] = 1;
            }
            var smm = save.StrengthMax - 1;
            if (save.Strength > smm && save.Feeling > save.FeelingMax - 1 && save.StrengthFood > save.StrengthMax - 1 && save.StrengthDrink > save.StrengthMax - 1)
                stat[(gint)"stat_100_all"]++;

            
        }
        /// <summary>
        /// 加载游戏存档
        /// </summary>
        public bool SavesLoad(ILPS lps)
        {
            if (lps == null)
                return false;
            if (string.IsNullOrWhiteSpace(lps.ToString()))
                return false;
            var tmp = new GameSave_v2(lps);
            if (tmp.GameSave == null)
                return false;
            if (tmp.GameSave.Money == 0 && tmp.GameSave.Likability == 0 && tmp.GameSave.Exp == 0
                && tmp.GameSave.StrengthDrink == 0 && tmp.GameSave.StrengthFood == 0)//数据全是0,可能是bug
                return false;
            if (tmp.GameSave.Exp < -1000000000)
            {
                tmp.GameSave.Exp = 1000000;
                tmp.Data[(gbol)"round"] = true;
                Dispatcher.Invoke(() => NoticeBox.Show("检测到经验值超过 9,223,372,036 导致算数溢出\n已经自动回正".Translate(), "数据溢出警告".Translate()));

            }
            if (tmp.GameSave.Money < -1000000000)
            {
                tmp.GameSave.Money = 100000;
                Dispatcher.Invoke(() => NoticeBox.Show("检测到金钱超过 9,223,372,036 导致算数溢出\n已经自动回正".Translate(), "数据溢出警告".Translate()));
            }

            if (tmp.Data[(gbol)"round"])
            {//根据游玩时间补偿数据溢出
                Dispatcher.Invoke(() => NoticeBox.Show("您以前遭遇过数据溢出, 已根据游戏时长自动添加进当前数值".Translate(), "数据溢出恢复".Translate()));
                var totalhour = (int)(tmp.Statistics![(gint)"stat_total_time"] / 3600);//总计游玩时间/小时
                if (totalhour < 500)
                {
                    tmp.GameSave.Exp += totalhour * 200;
                }
                else
                {
                    double lm = Math.Sqrt(totalhour / 500);
                    tmp.GameSave.LevelMax += (int)lm;
                    tmp.GameSave.Exp += (totalhour % 500 + (lm - (int)lm) * 500) * 200;

                }
                tmp.GameSave.LikabilityMax += totalhour / 10;
                tmp.Data[(gbol)"round"] = false;
            }
            if (Main.State != Main.WorkingState.Nomal)
            {
                Main.WorkTimer.Dispatcher.Invoke(() => Main.WorkTimer.Visibility = Visibility.Collapsed);
                Main.State = Main.WorkingState.Nomal;
            }
            if (winWorkMenu != null)
            {
                Dispatcher.Invoke(() => winWorkMenu.Close());
                winWorkMenu = null;
            }
            ScheduleTask?.Dispose();
            Buff?.Dispose();
            GameSavesData = tmp;
            if (evaluationStatisticsInitialized)
                ResetEvaluationSessionStatistics();
            Core.Save = tmp.GameSave;
            Buff = new Buff(this);
            ScheduleTask = new ScheduleTask(this);
            Items.Clear();
            foreach (var line in GameSavesData.Data.Assemblage.Where(x => x.Key.StartsWith("item")))
            {
                var itm = Item.CreateItem(this, line.Value);
                if (itm != null && !NanallyCharacter.IsRetiredItem(itm.Name))
                {
                    Dispatcher.Invoke(() => itm.LoadSource(this));
                    ItemsAdd(itm);
                }
            }
            HashCheck = HashCheck;
            GameSavesData.GameSave.Event_LevelUp += LevelUP;
            return true;
        }



        
        /// <summary>
        /// 关闭指示器,默认为true
        /// </summary>
        public bool CloseConfirm { get; private set; } = true;

        Grid IMainWindow.MGHost => MGHost;

        Grid IMainWindow.PetGrid => MGrid;
        internal MWController MWController { get; set; } = null!;
        /// <summary>
        /// 超模工作检查
        /// </summary>
        public bool WorkCheck(Work work)
        {
            //看看是否超模
            if (HashCheck && work.IsOverLoad())
            {
                if (Set["gameconfig"].GetBool("noAutoCal"))
                {
                    if (MessageBoxX.Show("当前工作数据属性超模,是否继续工作?\n超模工作可能会导致游戏发生不可预料的错误\n超模工作不影响大部分成就解锁\n可以在设置中开启自动计算自动为工作设置合理数值"
                        .Translate(), "超模工作提醒".Translate(), MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                    HashCheck = false;
                }
                else
                {
                    MessageBoxX.Show("当前工作数据属性超模,已自动取消".Translate(), "超模工作提醒".Translate());
                    return false;
                }
            }
            return true;
        }
        /// <summary>
        /// 游戏加载
        /// </summary>
        public void GameInitialization()
        {
            Console.SetOut(new ConsoleRedirectWriter(ActivityLogs));

            Console.WriteLine($"Nanally Desktop Pet {Version}");

            App.MainWindows.Add(this);
            try
            {
                //加载游戏设置
                if (new FileInfo(ExtensionValue.BaseDirectory + @$"\Setting{PrefixSave}.lps").Exists)
                {
                    Set = new Setting(this, File.ReadAllText(ExtensionValue.BaseDirectory + @$"\Setting{PrefixSave}.lps"));
                }
                if (PrefixSave == "" && (Set == null || (Set != null && !Set["SingleTips"].GetBool("helloworld"))) && File.Exists(ExtensionValue.BaseDirectory + @"\Setting.bkp"))
                {//如果设置是损坏的, 读取备份设置
                    Set = new Setting(this, File.ReadAllText(ExtensionValue.BaseDirectory + @"\Setting.bkp"));
                }

                Set ??= new Setting(this, "Setting#VPET:|\n");


                var visualTree = new FrameworkElementFactory(typeof(Border));
                visualTree.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
                var childVisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
                childVisualTree.SetValue(ClipToBoundsProperty, true);
                visualTree.AppendChild(childVisualTree);

                Template = new ControlTemplate
                {
                    TargetType = typeof(Window),
                    VisualTree = visualTree,
                };

                InitializeComponent();

                //MGrid.Height = 500 * Set.ZoomLevel;
                MGrid.Width = 500 * Set.ZoomLevel;
                if (Set.OpacityMain)
                    this.Opacity = Set.Opacity;

                double L = 0, T = 0;
                if (Set.StartRecordLast)
                {
                    var point = Set.StartRecordLastPoint;
                    if (point.X != 0 || point.Y != 0)
                    {
                        L = point.X;
                        T = point.Y;
                    }
                }
                else
                {
                    var point = Set.StartRecordPoint;
                    L = point.X; T = point.Y;
                }

                Left = L;
                Top = T;

                // control position inside bounds
                MWController = new MWController(this);
                Core.Controller = MWController;
                _ = ConstrainStartupPositionAsync();
                if (Set.TopMost)
                {
                    Topmost = true;
                }

                //不存在就关掉
                if (!Directory.Exists(ModPath))
                {
                    throw new DirectoryNotFoundException($"缺少运行资源目录：{ModPath}\n请重新部署本项目资源。无需安装原版 VPet。");
                }

            }
            catch (Exception e)
            {
                if (ApplicationLifecycle.IsSmokeTest)
                    throw;
                string errstr = "游戏发生错误,可能是".Translate() + (string.IsNullOrWhiteSpace(CoreMOD.NowLoading) ?
              "游戏或者MOD".Translate() : $"MOD({CoreMOD.NowLoading})") +
              "导致的\n如有可能请发送 错误信息截图和引发错误之前的操作 保留本机日志并反馈给娜娜莉项目维护者\n感谢您对游戏开发的支持\n".Translate()
              + e.ToString();
                MessageBoxX.Show(errstr, "游戏致命性错误".Translate() + ' ' + "启动错误".Translate(), Panuon.WPF.UI.MessageBoxIcon.Error);
                Close();
            }
        }

        /// <summary>
        /// 支持多开的启动方式
        /// </summary>
        /// <param name="prefixsave">存档前缀</param>
        /// <param name="basemw">基础窗口</param>
        public MainWindow(string? prefixsave, MainWindow? basemw = null)
        {
            PrefixSave = prefixsave ?? "";
            if (prefixsave != string.Empty && !PrefixSave.StartsWith("-"))
                PrefixSave = '-' + prefixsave;



            //处理ARGS
            Args = new LPS_D();
            foreach (var str in App.Args)
            {
                Args.Add(new Line(str));
            }
            _dwmEnabled = Win32.Dwmapi.DwmIsCompositionEnabled();
            _hwnd = new WindowInteropHelper(this).EnsureHandle();

            GameInitialization();

            if (basemw != null)
            {

                Set.Resolution = basemw.Set.Resolution;
            }


            //加载所有MOD
            List<(long, long, DirectoryInfo)> Path = new();
            Path.AddRange(new DirectoryInfo(ModPath).EnumerateDirectories().Where(directory => directory.Name is "0000_core" or "Nanally")
                .Select(directory => (-2L, -2L, directory)));

            Task.Run(() => ApplicationLifecycle.RunAsync(this, async () =>
            {
                await GameLoad(Path);
                await Dispatcher.InvokeAsync(() => ApplicationLifecycle.LoadedAsync(this)).Task.Unwrap();
            }));
        }
        /// <summary>
        /// MOD地址
        /// </summary>
        public List<DirectoryInfo> MODPath { get; private set; } = new List<DirectoryInfo>();

        public IEnumerable<IModInfo> ModInfo => CoreMODs;

        public IEnumerable<IModInfo> OnModInfo => CoreMODs.FindAll(x => x.IsOnMOD(this));

        /// <summary>
        /// 加载游戏
        /// </summary>
        /// <param name="Path">MOD地址</param>
        public async Task GameLoad(List<(long, long, DirectoryInfo)> Path)
        {
            await Dispatcher.InvokeAsync(() => { IdleTrial?.Dispose(); IdleTrial = null; ChatCompanion?.Dispose(); ChatCompanion = null; Fall?.Dispose(); Fall = null; GravityWalk?.Dispose(); GravityWalk = null; });
            Path = Path.GroupBy(x => x.Item3.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
            MODPath = Path.Select(x => x.Item3).ToList();
            await Dispatcher.InvokeAsync(new Action(() => LoadingText.Content = "Loading MOD"));


            //加载mod
            foreach (var modPath in Path)
            {
                DirectoryInfo di = modPath.Item3;
                if (!File.Exists(di.FullName + @"\info.lps"))
                    continue;
                await Dispatcher.InvokeAsync(new Action(() => LoadingText.Content = $"Loading MOD: {di.Name}"));

                

                CoreMODs.Add(new CoreMOD(di, this));
            }

            CoreMOD.NowLoading = null;

            if (CoreMODs.Count == 0 || Pets.Count == 0)
                throw new InvalidDataException($"没有找到可加载的角色配置。请检查 {ModPath} 下的 info.lps 和 pet 配置。");

            

            //判断是否需要清空缓存
            if (App.MainWindows.Count == 1 && Set.LastCacheDate < CoreMODs.Max(x => x.CacheDate))
            {//需要清理缓存
                Set.LastCacheDate = DateTime.Now;
                if (Directory.Exists(GraphCore.CachePath))
                {
                    Directory.Delete(GraphCore.CachePath, true);
                    Directory.CreateDirectory(GraphCore.CachePath);
                }
            }



            await Dispatcher.InvokeAsync(() =>
            {
                MessageBoxXSettings.Setting.OKButtonContent = "好的".Translate();
                MessageBoxXSettings.Setting.CancelButtonContent = "取消".Translate();
                MessageBoxXSettings.Setting.YesButtonContent = "是".Translate();
                MessageBoxXSettings.Setting.NoButtonContent = "否".Translate();
                PendingBoxSettings.Setting.CancelButtonContent = "取消".Translate();
                LoadingText.Content = "尝试加载游戏MOD".Translate();
            });

            // Migrate the previous bundled default once; preserve explicit role choices thereafter.
            var petloader = NanallyCharacter.Select(this);
            //去除其他语言内容
            var tag = petloader!.Config.Data.GetString("tag", "all")!.Split(',');
            LowDrinkText.RemoveAll(x => !x.FindTag(tag));
            LowFoodText.RemoveAll(x => !x.FindTag(tag));
            ClickTexts.RemoveAll(x => !x.FindTag(tag));
            SelectTexts.RemoveAll(x => !x.FindTag(tag));

            // 读取存档需要动画中的工作配置与 WorkTimer，但此时还不启动游戏时钟。
            await Dispatcher.InvokeAsync(() => LoadingText.Content = "尝试加载动画和生成缓存\n该步骤可能会耗时比较长\n请耐心等待".Translate());
            Core.Save = GameSavesData.GameSave;
            Core.Graph = petloader.Graph(Set.Resolution, Dispatcher);
            if (NanallyCharacter.IsNanally(petloader))
            {
                NanallyMotionAssets.Register(Core.Graph);
                NanallyCharacter.ValidateGraph(Core.Graph);
            }
            Main = await Dispatcher.InvokeAsync(() => new Main(Core));
            Main.Load_0_BaseConsole();
            Main.Load_2_TouchEvent();
            Main.Load_2_WaitGraph(c => Dispatcher.Invoke(() => LoadingText.Content =
                "尝试加载动画和生成缓存\n该步骤可能会耗时比较长\n请耐心等待".Translate()
                + $"\n  {c} / {petloader.GraphCount}")).Wait();

            await Dispatcher.InvokeAsync(new Action(() => LoadingText.Content = "尝试加载游戏存档".Translate()));
            //加载存档
            if (File.Exists(ExtensionValue.BaseDirectory + @"\Save.lps")) //有老的旧存档,优先旧存档
                try
                {
                    if (!SavesLoad(new LpsDocument(File.ReadAllText(ExtensionValue.BaseDirectory + @"\Save.lps"))))
                    {
                        //如果加载存档失败了,试试加载备份,如果没备份,就新建一个
                        LoadLatestSave(petloader.PetName);
                    }

                }
                catch (Exception ex)
                {
                    MessageBoxX.Show("存档损毁,无法加载该存档\n可能是数据溢出/超模导致的" + '\n' + ex.Message, "存档损毁".Translate());
                    //如果加载存档失败了,试试加载备份,如果没备份,就新建一个
                    LoadLatestSave(petloader.PetName);
                }
            else
                //如果加载存档失败了,试试加载备份,如果没备份,就新建一个
                LoadLatestSave(petloader.PetName);

            //加载数据合理化:文本
            if (!Set["gameconfig"].GetBool("noAutoCal"))
            {
                foreach (var selet in SelectTexts)
                {
                    selet.Exp = Math.Max(Math.Min(selet.Exp, 1000), -1000);
                    selet.Feeling = Math.Max(Math.Min(selet.Feeling, 100), -100);
                    selet.Health = Math.Max(Math.Min(selet.Health, 100), -100);
                    selet.Likability = Math.Max(Math.Min(selet.Likability, 50), -50);
                    selet.Money = Math.Max(Math.Min(selet.Money, 1000), -1000);
                    selet.Strength = Math.Max(Math.Min(selet.Strength, 1000), -1000);
                    selet.StrengthDrink = Math.Max(Math.Min(selet.StrengthDrink, 1000), -1000);
                    selet.StrengthFood = Math.Max(Math.Min(selet.StrengthFood, 1000), -1000);
                }
                foreach (var selet in ClickTexts)
                {
                    selet.Exp = Math.Max(Math.Min(selet.Exp, 1000), -1000);
                    selet.Feeling = Math.Max(Math.Min(selet.Feeling, 1000), -1000);
                    selet.Health = Math.Max(Math.Min(selet.Health, 100), -100);
                    selet.Likability = Math.Max(Math.Min(selet.Likability, 50), -50);
                    selet.Money = Math.Max(Math.Min(selet.Money, 1000), -1000);
                    selet.Strength = Math.Max(Math.Min(selet.Strength, 1000), -1000);
                    selet.StrengthDrink = Math.Max(Math.Min(selet.StrengthDrink, 1000), -1000);
                    selet.StrengthFood = Math.Max(Math.Min(selet.StrengthFood, 1000), -1000);
                }
            }


            //第一次启动日期
            if (GameSavesData.Data.FindLine("birthday") == null)
            {
                var sf = new FileInfo(ExtensionValue.BaseDirectory + @$"\Setting{PrefixSave}.lps");
                if (sf.Exists)
                {
                    GameSavesData[(gdat)"birthday"] = sf.CreationTime.Date;
                }
                else
                    GameSavesData[(gdat)"birthday"] = DateTime.Now.Date;
            }

            //补充数据信息
            if (string.IsNullOrEmpty(GameSavesData.GameSave.HostName))
            {
                GameSavesData.GameSave.HostName = Environment.UserName;
            }

            //if (GameSavesData.Data.FindLine("HostBDay") == null)
            //{
            //    GameSavesData[(gdat)"HostBDay"] = GameSavesData[(gdat)"birthday"];
            //}


            AutoSaveTimer.Elapsed -= AutoSaveTimer_Elapsed;
            AutoSaveTimer.Elapsed += AutoSaveTimer_Elapsed;
            if (GameSavesData.Statistics![(gdbe)"stat_bb_food"] < 0 || GameSavesData.Statistics[(gdbe)"stat_bb_drink"] < 0 || GameSavesData.Statistics[(gdbe)"stat_bb_drug"] < 0
                || GameSavesData.Statistics[(gdbe)"stat_bb_snack"] < 0 || GameSavesData.Statistics[(gdbe)"stat_bb_functional"] < 0 || GameSavesData.Statistics[(gdbe)"stat_bb_meal"] < 0
                || GameSavesData.Statistics[(gdbe)"stat_bb_gift"] < 0)
            {
                HashCheck = false;
            }

            if (Set.AutoSaveInterval > 0)
            {
                AutoSaveTimer.Interval = Set.AutoSaveInterval * 60000;
                AutoSaveTimer.Start();
            }
            ClickTexts.Add(new ClickText("你知道吗? 鼠标右键可以打开菜单栏"));
            ClickTexts.Add(new ClickText("你知道吗? 你可以在设置里面修改游戏的缩放比例"));
            ClickTexts.Add(new ClickText("想要宠物不乱动? 设置里可以设置智能移动或者关闭移动"));

            ClickTexts.Add(new ClickText("长按脑袋拖动桌宠到你喜欢的任意位置"));

            ////临时聊天内容
            //"如果你觉得目前功能太少,那就多挂会机. 宠物会自己动的".Translate(),
            //"你知道吗? 你可以在设置里面修改游戏的缩放比例".Translate(),
            //"你现在乱点说话是说话系统的一部分,不过还没做,在做了在做了ing".Translate(),

            await Dispatcher.InvokeAsync(() => LoadingText.Content = "尝试启动桌宠".Translate());
            Main.NoFunctionMOD = Set.CalFunState;
            Main.Load_3_BindingTimer();
            Main.Load_4_Start();
            await Dispatcher.InvokeAsync(() =>
              {
                  //清空资源
                  Main.Resources = Application.Current.Resources;
                  if (Main.MsgBar != null)
                      Main.MsgBar.This.Resources = Application.Current.Resources;
                  if (Main.ToolBar != null)
                      Main.ToolBar.Resources = Application.Current.Resources;
                  Main.ToolBar?.LoadClean();
                  Main.WorkList(out List<Work> ws, out List<Work> ss, out List<Work> ps);

                  if (ws.Count == 0)
                  {
                      Main.ToolBar!.MenuWork.Visibility = Visibility.Collapsed;
                  }
                  else
                  {
                      Main.ToolBar!.MenuWork.MouseDoubleClick += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          ShowWorkMenu(Work.WorkType.Work);
                      };
                      Main.ToolBar.MenuWork.Click += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          if (Main.ToolBar.MenuWork.Items.Count == 0)
                              ShowWorkMenu(Work.WorkType.Work);
                      };
                  }
                  if (ss.Count == 0)
                  {
                      Main.ToolBar.MenuStudy.Visibility = Visibility.Collapsed;
                  }
                  else
                  {
                      Main.ToolBar.MenuStudy.MouseDoubleClick += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          ShowWorkMenu(Work.WorkType.Study);
                      };
                      Main.ToolBar.MenuStudy.Click += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          if (Main.ToolBar.MenuStudy.Items.Count == 0) ShowWorkMenu(Work.WorkType.Study);
                      };
                  }
                  if (ps.Count == 0)
                  {
                      Main.ToolBar.MenuPlay.Visibility = Visibility.Collapsed;
                  }
                  else
                  {
                      Main.ToolBar.MenuPlay.MouseDoubleClick += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          ShowWorkMenu(Work.WorkType.Play);
                      };
                      Main.ToolBar.MenuPlay.Click += (x, y) =>
                      {
                          Main.ToolBar.Visibility = Visibility.Collapsed;
                          if (Main.ToolBar.MenuPlay.Items.Count == 0) ShowWorkMenu(Work.WorkType.Play);
                      };
                  }
                  WorkStarMenu = new System.Windows.Controls.MenuItem()
                  {
                      Header = "收藏".Translate(),
                      HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                  };
                  foreach (var w in WorkStar())
                  {
                      var mi = new System.Windows.Controls.MenuItem()
                      {
                          Header = w.NameTrans
                      };
                      mi.Click += (s, e) => Main.ToolBar.StartWork(w.Double(Set["workmenu"].GetInt("double_" + w.Name, 1)));
                      WorkStarMenu.Items.Add(mi);
                  }
                  Main.ToolBar.MenuInteract.Items.Add(WorkStarMenu);

                  //加载主题:
                  LoadTheme(Set.Theme);
                  //加载字体
                  LoadFont(Set.Font);

                  LoadingText.Content = "正在加载游戏\n该步骤可能会耗时比较长\n请耐心等待".Translate();


                  //加载数据合理化:工作
                  if (!Set["gameconfig"].GetBool("noAutoCal"))
                  {
                      foreach (var work in Core.Graph!.GraphConfig.Works)
                      {
                          if (work.LevelLimit > 200)//导入的最大合理工作不能超过200级
                              work.LevelLimit = 200;
                          work.FixOverLoad();//导入的工作默认1.2倍
                      }
                  }
                  //加载数据合理化:自动工作
                  foreach (var stp in SchedulePackage)
                      stp.FixOverLoad();


                  Main.FunctionSpendHandle += lowStrength;
                  if (Main.WorkTimer != null)
                      Main.WorkTimer.E_FinishWork += WorkTimer_E_FinishWork;
                  Main.ToolBar.MenuMODConfig.Visibility = Visibility.Collapsed;
                  Foods.ForEach(item => item.LoadImageSource(this));
                  //加载数据合理化:食物
                  if (!Set["gameconfig"].GetBool("noAutoCal"))
                  {
                      foreach (Food f in Foods)
                      {
                          if (f.IsOverLoad())
                          {
                              f.Price = Math.Max((int)f.RealPrice, 1);
                              f.isoverload = false;
                          }
                      }
                  }
                  Photos.ForEach(item => item.LoadUserInfo(this));

                  //每日礼盒
                  everydaygift();







                  var tlv = Main.ToolBar.Tlv;
                  Main.ToolBar.gdPanel.Children.Remove(tlv);
                  var sp = new StackPanel();
                  Grid.SetColumnSpan(sp, 3);
                  sp.Orientation = System.Windows.Controls.Orientation.Horizontal;
                  sp.Children.Add(tlv);
                  tlvplus = new TextBlock();
                  tlvplus.Margin = new Thickness(1);
                  tlvplus.VerticalAlignment = VerticalAlignment.Bottom;
                  tlvplus.FontSize = 18;
                  tlvplus.Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText);
                  sp.Children.Add(tlvplus);
                  Main.ToolBar.gdPanel.Children.Add(sp);
                  Main.TimeUIHandle += MWUIHandle;
                  Main.ToolBar.EventMenuPanelShow += () => MWUIHandle(Main);


                  //窗口部件
                  winSetting = new winGameSetting(this);
                  winBetterBuy = new winBetterBuy(this);

                  Main.DefaultClickAction = () =>
                  {
                      if (new TimeSpan(DateTime.Now.Ticks - lastclicktime).TotalSeconds > 20)
                      {
                          lastclicktime = DateTime.Now.Ticks;
                          var rt = GetClickText();
                          if (rt != null)
                          {
                              //聊天效果
                              if (rt.Exp != 0)
                              {
                                  if (rt.Exp > 0)
                                  {
                                      GameSavesData.Statistics[(gint)"stat_say_exp_p"]++;
                                  }
                                  else
                                      GameSavesData.Statistics[(gint)"stat_say_exp_d"]++;
                              }
                              if (rt.Likability != 0)
                              {
                                  if (rt.Likability > 0)
                                      GameSavesData.Statistics[(gint)"stat_say_like_p"]++;
                                  else
                                      GameSavesData.Statistics[(gint)"stat_say_like_d"]++;
                              }
                              if (rt.Money != 0)
                              {
                                  if (rt.Money > 0)
                                      GameSavesData.Statistics[(gint)"stat_say_money_p"]++;
                                  else
                                      GameSavesData.Statistics[(gint)"stat_say_money_d"]++;
                              }
                              Main.Core.Save!.EatFood(rt);
                              Main.Core.Save!.Money += rt.Money;
                              Main.SayRnd(rt.TranslateTextConvert(Main), desc: rt.FoodToDescription());
                          }
                      }
                  };
                  Main.PlayVoiceVolume = Set.VoiceVolume;
                  Main.FunctionSpendHandle += StatisticsCalHandle;
                  InitializeEvaluationStatistics();
                  DisplayGrid.Child = Main;
                  _ = RevealWhenReadyAsync(Main);

                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Setting, "退出桌宠".Translate(), () => { Main.ToolBar.Visibility = Visibility.Collapsed; Close(); });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Setting, "设置面板".Translate(), () =>
                  {
                      Main.ToolBar.Visibility = Visibility.Collapsed;
                      winSetting.Show();
                      winSetting.Activate();
                  });

                  //this.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Res/TopLogo2019.PNG")));

                  //Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.Feed, "喂食测试", () =>
                  //    {
                  //        Main.ToolBar.Visibility = Visibility.Collapsed;
                  //        IRunImage eat = (IRunImage)Core.Graph!.FindGraph(GraphType.Eat, GameSave.ModeType.Nomal);
                  //        var b = Main.FindDisplayBorder(eat);
                  //        eat.Run(b, new BitmapImage(new Uri("pack://application:,,,/Res/汉堡.png")), Main.DisplayToNomal);
                  //    }
                  //);
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "吃饭".Translate(), () =>
                  {
                      winBetterBuy.Show(Food.FoodType.Meal);
                  });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "喝水".Translate(), () =>
                  {
                      winBetterBuy.Show(Food.FoodType.Drink);
                  });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "收藏".Translate(), () =>
                  {
                      winBetterBuy.Show(Food.FoodType.Star);
                  });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "药品".Translate(), () =>
                  {
                      winBetterBuy.Show(Food.FoodType.Drug);
                  });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "礼品".Translate(), () =>
                  {
                      winBetterBuy.Show(Food.FoodType.Gift);
                  });
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Feed, "背包".Translate(), () =>
                  {
                      if (winInventory != null && !winInventory.IsClosed)
                          winInventory.Show();
                      else
                      {
                          winInventory = new winInventory(this);
                          winInventory.Show();
                      }
                  });
                  GravityWalk?.Dispose();
                  GravityWalk = new GravityWalkController(this);
                  Fall?.Dispose();
                  Fall = new NanallyFallController(this);
                  ChatCompanion = new Companion.CompanionController(this);
                  winSetting.AttachCompanionSettings(ChatCompanion);
                  IdleTrial = new NanallyIdleTrial(this);
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Interact, "娜娜莉待机", () => _ = IdleTrial.ToggleAsync());
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Interact, "和娜娜莉聊聊", () => ChatCompanion.Show());
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Interact, "重力贴边行走", () =>
                      GravityWalk.SetEnabled(!GravityWalk.Enabled));
                  Main.ToolBar.AddMenuButton(ToolBar.MenuType.Interact, "窗口外缘行走", () => GravityWalk.ShowWindowPicker());
                  Main.SetMoveMode(Set.AllowMove, Set.SmartMove, Set.SmartMoveInterval * 1000);
                  Main.SetLogicInterval((int)(Set.LogicInterval * 1000));
                  if (Set.MessageBarOutside)
                      Main.MsgBar?.SetPlaceOUT();

                  Main.WorkCheck = WorkCheck;

                  //加载图标
                  notifyIcon = new NotifyIcon();
                  notifyIcon.Text = ApplicationPaths.ProductName + PrefixSave;
                  ContextMenu m_menu;

                  if (Set.PetHelper)
                      LoadPetHelper();



                  m_menu = new ContextMenu();
                  m_menu.Items.Add(new MenuItem("和娜娜莉聊聊", null, (_, _) => ChatCompanion.Show()));
                  m_menu.Items.Add(new MenuItem("AI 设置", null, (_, _) => ChatCompanion.Show(true)));
                  m_menu.Items.Add(new MenuItem("停止回复", null, (_, _) => ChatCompanion.Stop()));
                  var gravityMenu = new MenuItem("重力贴边行走");
                  var gravityToggle = new MenuItem("启用", null, (_, _) => GravityWalk.SetEnabled(!GravityWalk.Enabled));
                  gravityMenu.DropDownOpening += (_, _) => gravityToggle.Checked = GravityWalk.Enabled;
                  gravityMenu.DropDownItems.Add(gravityToggle);
                  foreach (SupportEdge edge in Enum.GetValues<SupportEdge>())
                  {
                      string label = edge switch { SupportEdge.Bottom => "底边", SupportEdge.Right => "右边", SupportEdge.Top => "顶边（倒置）", _ => "左边" };
                      gravityMenu.DropDownItems.Add(new MenuItem(label, null, (_, _) => GravityWalk.SelectEdge(edge)));
                  }
                  gravityMenu.DropDownItems.Add(new MenuItem("反向行走", null, (_, _) => GravityWalk.Reverse()));
                  m_menu.Items.Add(gravityMenu);
                  m_menu.Items.Add(new MenuItem("窗口外缘行走", null, (_, _) => GravityWalk.ShowWindowPicker()));
                  m_menu.Opening += (x, y) => { if (GameSavesData.Statistics != null) GameSavesData.Statistics[(gint)"stat_menu_pop"]++; };
                  var hitThrough = new MenuItem("鼠标穿透".Translate(), null, (x, y) => { SetTransparentHitThrough(); })
                  {
                      Name = "NotifyIcon_HitThrough",
                      Checked = HitThrough
                  };
                  m_menu.Items.Add(hitThrough);
                  var topmost = new MenuItem("置于顶层".Translate(), null, (x, y) =>
                  {
                      Topmost = ((MenuItem)x!).Checked;
                  })
                  {
                      Name = "NotifyIcon_TopMost",
                      CheckOnClick = true,
                      Checked = Topmost
                  };
                  m_menu.Items.Add(topmost);
                  m_menu.Items.Add(new MenuItem("重置位置与状态".Translate(), null, (x, y) =>
                  {
                      GravityWalk.SetEnabled(false);
                      Main.CleanState();
                      Main.DisplayToNomal();
                      Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                      Top = (SystemParameters.PrimaryScreenHeight - Height) / 2;
                  }));
                  m_menu.Items.Add(new MenuItem("设置面板".Translate(), null, (x, y) =>
                  {
                      winSetting.Show();
                  }));
                  m_menu.Items.Add(new MenuItem("重启桌宠".Translate(), null, (x, y) => Restart()));
                  m_menu.Items.Add(new MenuItem("退出桌宠".Translate(), null, (x, y) => Close()));

                  LoadDIY();

                  notifyIcon.ContextMenuStrip = m_menu;

                  using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Nanally.ico")).Stream)
                  using (var sourceIcon = new System.Drawing.Icon(iconStream, System.Windows.Forms.SystemInformation.SmallIconSize))
                  {
                      var previousIcon = notifyIcon.Icon;
                      notifyIcon.Icon = (System.Drawing.Icon)sourceIcon.Clone();
                      previousIcon?.Dispose();
                  }

                  notifyIcon.Visible = true;
                  notifyIcon.BalloonTipClicked += (a, b) =>
                  {
                      winSetting.Show();
                  };
                  if (Set.StartUPBoot == true && !Set["v"][(gbol)"newverstartup"])
                  {//更新到最新版开机启动方式
                      try
                      {
                          winSetting.GenStartUP();
                          Set["v"][(gbol)"newverstartup"] = true;
                      }
                      catch
                      {

                      }
                  }


                  //成就和统计 
                  GameSavesData.Statistics![(gint)"stat_open_times"]++;
                  Main.MoveTimer.Elapsed += MoveTimer_Elapsed;
                  Main.SayProcess.Add(Main_OnSay);
                  Main.Event_TouchHead += Main_Event_TouchHead;
                  Main.Event_TouchBody += Main_Event_TouchBody;

                  HashCheck = HashCheck;

                  //添加捏脸动画(若有)
                  if (Core.Graph!.GraphConfig.Data.ContainsLine("pinch"))
                  {
                      var pin = Core.Graph!.GraphConfig.Data["pinch"];
                      Main.Core.TouchEvent.Insert(0, new TouchArea(
                          new Point(pin[(gdbe)"px"], pin[(gdbe)"py"]), new Size(pin[(gdbe)"sw"], pin[(gdbe)"sh"])
                          , DisplayPinch, true));
                  }


                  if (Set.HitThrough)
                  {
                      if (!Set["v"][(gbol)"HitThrough"])
                      {
                          Set["v"][(gbol)"HitThrough"] = true;
                          Set.HitThrough = false;
                      }
                      else
                          SetTransparentHitThrough();
                  }

                  if (!Set["SingleTips"].GetBool("helloworld"))
                  {
                      _ = RunReminderAsync(2000, () =>
                      {
                          Set["SingleTips"].SetBool("helloworld", true);
                          NoticeBox.Show("欢迎使用娜娜莉桌宠!\n如果遇到桌宠爬不见了,可以在我这里设置居中或退出桌宠".Translate(),
                             "你好".Translate() + Environment.UserName, Panuon.WPF.UI.MessageBoxIcon.Info, true, 5000);
                          //Thread.Sleep(2000);
                      });
                  }
                  if (Set["v"][(gint)"rank"] != DateTime.Now.Year && GameSavesData.Statistics?[(gint)"stat_total_time"] > 3600)
                  {//年度报告提醒
                      _ = RunReminderAsync(Function.Rnd.Next(200000, 400000), () =>
                      {
                          Set["v"][(gint)"rank"] = DateTime.Now.Year;
                          var btn = Dispatcher.Invoke(() =>
                          {
                              var button = new System.Windows.Controls.Button()
                              {
                                  Content = "点击前往查看".Translate(),
                                  FontSize = 20,
                                  HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                                  Background = Function.ResourcesBrush(Function.BrushType.PrimaryDark),
                                  Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText),
                              };
                              button.Click += (x, y) =>
                              {
                                  var panelWindow = new winCharacterPanel(this);
                                  panelWindow.MainTab.SelectedIndex = 1;
                                  panelWindow.Show();
                                  Main.MsgBar?.ForceClose();
                              };
                              return button;
                          });
                          Main.Say("哼哼~主人，我的考试成绩出炉了哦，快来和我一起看我的成绩单喵".Translate(), btn, "shining");
                      });
                  }
                  //生日设置提醒
                  if (GameSavesData.Data.FindLine("HostBDay") == null)
                  {
                      _ = RunReminderAsync(Function.Rnd.Next(100000, 200000), () =>
                      {
                          var btn = Dispatcher.Invoke(() =>
                          {
                              var button = new System.Windows.Controls.Button()
                              {
                                  Content = "设置".Translate(),
                                  FontSize = 20,
                                  HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                                  Background = Function.ResourcesBrush(Function.BrushType.PrimaryDark),
                                  Foreground = Function.ResourcesBrush(Function.BrushType.PrimaryText),
                              };
                              button.Click += (x, y) =>
                              {
                                  ShowSetting(2);
                              };
                              return button;
                          });
                          Main.Say("不要忘记设置生日时间哦 {0}，我会偷偷给你准备礼物的。".Translate(GameSavesData.GameSave.HostName), btn, "shining");
                      });
                  }
                  else
                  {
                      var bdt = GameSavesData.GetDateTime("HostBDay");
                      if (DateTime.Now.Month == bdt.Month && DateTime.Now.Day == bdt.Day)
                      {
                          _ = RunReminderAsync(Function.Rnd.Next(100000, 200000), () =>
                          {
                              HostBDay();
                          });
                      }
                  }
                  //原角色的三款专属生日蛋糕与四周年生日活动已随角色资源移除；用户的生日庆祝（HostBDay）保留。
                  newday = DateTime.Now.Day;
                  Main.TimeHandle += NewDayHandle;
                  Event_NewDay += () =>
                  {
                      var bdt = GameSavesData.GetDateTime("HostBDay");
                      if (DateTime.Now.Month == bdt.Month && DateTime.Now.Day == bdt.Day)
                      {
                          HostBDay();
                      }
                  };
                  Event_NewDay += everydaygift;

                  //添加购买事件
                  Event_TakeItemHandle += (item, count, from) => ActivityLogs.Add(new ActivityLog("take_" + from, item.TranslateName, count.ToString()));
                  //添加工作事件
                  Main.Event_WorkStart += (work) => ActivityLogs.Add(new ActivityLog("work_start", work.NameTrans));
                  Main.Event_WorkEnd += (workinfo) => ActivityLogs.Add(new ActivityLog("work_end", workinfo.work.NameTrans, workinfo.Reason.ToString(), workinfo.spendtime.ToString("f0"), workinfo.count.ToString("f0")));
                  Main.SayProcess.Add(sayinfo => _ = LogPetSpeechAsync(sayinfo));
                  //if (DateTime.Now.DayOfYear == 1)
                  //{
                  //    Task.Run(() =>
                  //    {
                  //        Thread.Sleep(5000);
                  //        Main.SayRnd("25年都跨过去了, 还有什么是跨不过的呢? {0}这一年辛苦了! 新年请多多指教!".Translate(GameSavesData.GameSave.HostName));
                  //    });
                  //}
                  //修复因为26年元旦bug导致HashCheck失效的问题
                  //简单来讲就是给所有有 2026跨年 这个照片的用户恢复一次HashCheck, 就当福利了(, 因为有这个照片的基本上都在bug周期里
                  //请看到这个代码的人不要外传, 避免滥用
                  //var photo25 = Photos.Find(x => x.Name == "2026跨年");
                  //if (photo25?.IsUnlock == true && GameSavesData.HashCheck == false && GameSavesData.Data["debug"][(gbol)"fix26"] == false)
                  //{
                  //    GameSave_v2 ogs = GameSavesData;
                  //    GameSavesData = new GameSave_v2(ogs.GameSave.Name);
                  //    GameSavesData.Data = ogs.Data;
                  //    GameSavesData.GameSave = ogs.GameSave;
                  //    GameSavesData.Statistics = ogs.Statistics;
                  //    HashCheck = true;
                  //}
                  //GameSavesData.Data["debug"][(gbol)"fix26"] = true;
                  if (GameSavesData.HashCheck == false && GameSavesData["debug"].Find("losthash") == null)
                  {
                      GameSavesData["debug"][(gdat)"losthash"] = DateTime.Now;
                  }

                  //动画错误
                  if (Main.ErrorMessage.Count != 0)
                  {
                      var errstr = string.Join("\n------\n", Main.ErrorMessage);
                      if (errstr.Contains("0000_core"))
                      {
                          MessageBoxX.Show($"动画加载错误。请退出后重新生成缓存，或重新部署角色资源。\n缓存目录：{ApplicationPaths.CacheDirectory}\n资源目录：{ModPath}", "动画加载错误");
                          Console.WriteLine(errstr);
                      }
                      else
                          MessageBoxX.Show("动画加载错误\n娜娜莉桌宠未能成功加载该动画\n请联系MOD作者修复该问题".Translate() + '\n' + errstr, "动画加载错误".Translate());

                      Main.ErrorMessage.Clear();
                  }
                  //这里写的都是共通的功能, 如果限定第一个MW使用的功能, 请前往

                  if (Set.DeBug)
                      ActivityLogs.CollectionChanged += ActivityLogs_WriteFile;
              });


            ////游戏提示
            //{
            //    await Dispatcher.InvokeAsync(new Action(() =>
            //    {
            //        MessageBoxX.Show("检测到您开启了开机启动, 以下是开机启动相关提示信息: (仅显示一次)".Translate() + "\n------\n" +
            //             "游戏开机启动的实现方式是创建快捷方式,不是注册表,更健康,所以游戏卸了也不知道\n如果游戏打不开,可以去这里手动删除游戏开机启动快捷方式:\n%appdata%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\".Translate()
            //          , "关于卸载不掉的问题是因为开启了开机启动".Translate(), Panuon.WPF.UI.MessageBoxIcon.Info);
            //        Set["SingleTips"][(gint)"open"] = 1;
            //    }));
            //}

        }
        public static object LogsLock = new object();
        private void ActivityLogs_WriteFile(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            if (e != null)
                if (e.NewItems != null)
                {
                    List<string> sb = new List<string>();
                    foreach (ActivityLog log in e.NewItems)
                    {
                        sb.Add(log.ToString(Main));
                    }
                    string logPath = System.IO.Path.Combine(ApplicationPaths.LogDirectory, $"Activity{PrefixSave}.txt");
                    foreach (string line in sb) LocalLogWriter.Write(logPath, line);
                }
        }

        private void everydaygift()
        {
            if (Set["dailydata"][(gint)"everydaygift"] == DateTime.Now.DayOfYear)
            {
                return;
            }
            Set["dailydata"][(gint)"everydaygift"] = DateTime.Now.DayOfYear;
            var itm = new Item
            {
                Name = "每日礼包",
                Desc = "物品系统附赠的每日礼包, 打开后会获得3个随机物品.".Translate(),
                ItemType = "Mail",
                Price = 15,
            };
            Dispatcher.Invoke(() => itm.LoadSource(this));
            ItemsAdd(itm);
        }




        


        TextBlock tlvplus = null!;


        

        private void MWUIHandle(Main main)
        {
            if (Main.ToolBar!.BdrPanel.Visibility == Visibility.Visible)
            {
                int levelMax = GameSavesData.GameSave.LevelMax;
                if (tlvplus.Tag is int previous && previous == levelMax) return;
                tlvplus.Tag = levelMax;
                tlvplus.Text = levelMax == 0 ? "" : $" / {1000 + levelMax * 100} x{levelMax}";
            }
        }

        /// <summary>
        /// 是否显示吃东西动画
        /// </summary>
        bool showeatanm = true;
        /// <summary>
        /// 显示吃东西(夹层)动画
        /// </summary>
        /// <param name="graphName">夹层动画名</param>
        /// <param name="imageSource">被夹在中间的图片</param>
        public void DisplayFoodAnimation(string graphName, ImageSource imageSource)
        {
            if (Core.Graph!.FindGraph(graphName, AnimatType.Single, Core.Save!.Mode) == null)
            {
                //当前角色没有进食/收礼动画: 明确提示并跳过, 不消耗 showeatanm 闩锁, 也不回退到原角色占位图形
                Main.LabelDisplayShow("{0}还没有学会这个动作, 先默默收下了".Translate(GameSavesData.GameSave!.Name), 4000);
                return;
            }
            if (showeatanm)
            {//显示动画
                showeatanm = false;
                Main.Display(graphName, imageSource, () =>
                {
                    showeatanm = true;
                    if (Core.Controller!.EnableFunction)
                    {
                        var newmod = Core.Save!.CalMode();
                        if (Core.Save!.Mode != newmod)
                        {
                            //魔改下参数以免不播放切换动画
                            Main.DisplayType.Type = GraphType.Default;
                            //切换显示动画
                            Main.PlaySwitchAnimat(Core.Save!.Mode, newmod);
                            Core.Save!.Mode = newmod;
                        }
                        else
                            Main.DisplayToNomal();
                    }
                    else
                        Main.DisplayToNomal();
                });
            }
            else
            {//如果不显示动画, 则看看是不是有覆盖
                if (Main.DisplayType.Animat != AnimatType.Single && Main.DisplayType.Name != graphName)
                {
                    showeatanm = true;
                }
            }
        }

        public void HostBDay()
        {
            var petloader = Pets.Find(x => x.Name == Set.PetGraph);
            petloader ??= Pets[0];

            string sbv = "Special_Birthday_Voice_" + petloader.Name;
            string sbv_trans = sbv.Translate(GameSavesData.GameSave.HostName);
            if (sbv == sbv_trans)
            {
                Main.Say("今天是{0}的生日！祝{0}生日快乐！".Translate(GameSavesData.GameSave.HostName), "bday", true);
            }
            else
            {
                Main.Say(sbv_trans, "bday");
                Dispatcher.Invoke(() =>
                {
                    var panelWindow = new winCharacterPanel(this);
                    panelWindow.MainTab.SelectedIndex = 2;
                    panelWindow.Show();
                });
            }
        }



        int newday = 0;
        private void NewDayHandle(Main main)
        {
            if (DateTime.Now.Hour == 0 && newday != DateTime.Now.Day)
            {//跨时间
                newday = DateTime.Now.Day;
                Event_NewDay?.Invoke();
            }
        }
        /// <summary>
        /// 事件:新的一天
        /// </summary>
        public event Action? Event_NewDay;
        /// <summary>
        /// 显示捏脸情况
        /// </summary>
        public bool DisplayPinch()
        {
            if (Core.Graph!.FindGraphs("pinch", AnimatType.A_Start, Core.Save!.Mode) == null)
            {
                return false;
            }
            Main.CountNomal = 0;

            if (Core.Controller!.EnableFunction && Core.Save!.Strength >= 10 && Core.Save!.Feeling < Core.Save!.FeelingMax)
            {
                Core.Save!.StrengthChange(-2);
                Core.Save!.FeelingChange(1);
                Core.Save!.Mode = Core.Save!.CalMode();
                Main.LabelDisplayShowChangeNumber(LocalizeCore.Translate("体力-{0:f0} 心情+{1:f0}"), 2, 1);
            }
            if (Main.DisplayType.Name == "pinch")
            {
                if (Main.DisplayType.Animat == AnimatType.A_Start)
                    return false;
                else if (Main.DisplayType.Animat == AnimatType.B_Loop)
                    if (Dispatcher.Invoke(() => Main.PetGrid.Tag) is IGraph ig && ig.GraphInfo.Name == "pinch" && ig.GraphInfo.Animat == AnimatType.B_Loop)
                    {
                        ig.SetContinue();
                        return true;
                    }
                    else if (Dispatcher.Invoke(() => Main.PetGrid2.Tag) is IGraph ig2 && ig2.GraphInfo.Name == "pinch" && ig2.GraphInfo.Animat == AnimatType.B_Loop)
                    {
                        ig2.SetContinue();
                        return true;
                    }
            }
            Main_Event_TouchHead();
            Main_Event_TouchBody();
            Main.Display("pinch", AnimatType.A_Start, () =>
               Main.Display("pinch", AnimatType.B_Loop, DisplayPinch_loop));
            return true;
        }
        private void DisplayPinch_loop()
        {
            if (Main.isPress && Main.DisplayType.Name == "pinch" && Main.DisplayType.Animat == AnimatType.B_Loop)
            {
                if (Core.Controller!.EnableFunction && Core.Save!.Strength >= 10 && Core.Save!.Feeling < Core.Save!.FeelingMax)
                {
                    Core.Save!.StrengthChange(-2);
                    Core.Save!.FeelingChange(1);
                    Core.Save!.Mode = Core.Save!.CalMode();
                    Main.LabelDisplayShowChangeNumber(LocalizeCore.Translate("体力-{0:f0} 心情+{1:f0}"), 2, 1);
                }
                Main.Display("pinch", AnimatType.B_Loop, DisplayPinch_loop);
            }
            else
            {
                Main.DisplayCEndtoNomal("pinch");
            }
        }
        /// <summary>
        /// 获取收藏的工作
        /// </summary>
        public List<Work> WorkStar()
        {
            List<Work> works = new List<Work>();
            foreach (var work in Core.Graph!.GraphConfig.Works)
            {
                if (Set["work_star"].GetBool(work.Name))
                    works.Add(work);
            }
            return works;
        }
        public System.Windows.Controls.MenuItem WorkStarMenu = null!;

        public void LevelUP(GameSave_VPet.LevelUpEventArgs args)
        {
            var gf = Core.Graph!.FindGraph("levelup", GraphInfo.AnimatType.Single, GameSavesData.GameSave.Mode);
            if (gf != null)
            {
                Main.Say("邦邦咔邦,{0}等级突破了!".Translate(Name), "levelup", true);
            }
            if (args.IsLevelMaxUp)
            {//告知用户上限等级上升
                Task.Run(() =>
                {
                    Thread.Sleep(5000);
                    Dispatcher.Invoke(() =>
                    {
                        MessageBoxX.Show("系统提示\n您的桌宠等级已经突破\nLv{0}→LV{1} x{2}\n已突破为尊贵的x{3}阶".Translate(
                            1000 + args.BeforeLevelMax * 100, 100 * GameSavesData.GameSave.LevelMax, GameSavesData.GameSave.LevelMax),
                            "桌宠等级突破".Translate());
                    });
                });
            }
        }
    }
}
