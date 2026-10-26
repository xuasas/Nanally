using LinePutScript;
using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Win32;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// winGameSetting.xaml 的交互逻辑
    /// </summary>
    public partial class winGameSetting : WindowX
    {
        MainWindow mw;
        private bool AllowChange = false;
        private void ButtonRestart_Click(object sender, RoutedEventArgs e) => mw.Restart();

        public winGameSetting(MainWindow mw)
        {
            this.mw = mw;
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            InitializeComponent();
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            //var bit = new BitmapImage(new Uri("pack://application:,,,/Res/TopLogo2019.png"));
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));
            ////ImageWHY.Source = bit;
            //Console.WriteLine(DateTime.Now.ToString("mm:ss.fff"));



            Title = "设置".Translate() + ' ' + mw.PrefixSave;
            SettingMenuWidth.Width = new GridLength(LocalizeCore.GetDouble("SettingMenuWidth", 150));
            TopMostBox.IsChecked = mw.Set.TopMost;
            if (mw.Set.IsBiggerScreen)
            {
                FullScreenBox.IsChecked = true;
                ZoomSlider.Maximum = 8;
            }
            else
            {
                FullScreenBox.IsChecked = false;
                ZoomSlider.Maximum = 3;
            }
            ZoomSlider.Value = mw.Set.ZoomLevel * 2;
            //this.Width = 400 * Math.Sqrt(ZoomSlider.Value);
            //this.Height = 450 * Math.Sqrt(ZoomSlider.Value);

            CalFunctionBox.IsChecked = mw.Set.EnableFunction;
            CalSlider.Value = mw.Set.LogicInterval;
            InteractionSlider.Value = mw.Set.InteractionCycle;
            MoveEventBox.IsChecked = mw.Set.AllowMove;
            SmartMoveEventBox.IsChecked = mw.Set.SmartMove;
            AutoChangeWindowEvent.IsChecked = mw.Set.AutoChangeWindow;
            PressLengthSlider.Value = mw.Set.PressLength / 1000.0;
            SwitchMsgOut.IsChecked = mw.Set.MessageBarOutside;
            SwitchHideFromTaskControl.IsChecked = mw.Set.HideFromTaskControl;
            ConsoleBox.IsChecked = mw.Set.DeBug;

            StartUpBox.IsChecked = mw.Set.StartUPBoot;
            Title = ApplicationPaths.ProductName + " - 设置";
            TextBoxPetName.Text = mw.Core.Save!.Name;
            TextBoxStartUpX.Text = mw.Set.StartRecordPoint.X.ToString();
            TextBoxStartUpY.Text = mw.Set.StartRecordPoint.Y.ToString();
            numBackupSaveMaxNum.Value = mw.Set.BackupSaveMaxNum;
            combCalFunState.SelectedIndex = (int)mw.Set.CalFunState;
            combCalFunState.IsEnabled = !mw.Set.EnableFunction;

            TextBoxHostBDay.MaxDateTime = DateTime.Now;
            TextBoxHostBDay.SelectedDateTime = mw.GameSavesData.GetDateTime("HostBDay", mw.GameSavesData[(gdat)"birthday"]);
            TextBoxHostName.Text = mw.GameSavesData.GameSave.HostName;

            CalTimeInteraction();



            LoadMutiUI();

            LanguageBox.Items.Add("null");
            foreach (string v in LocalizeCore.AvailableCultures)
            {
                LanguageBox.Items.Add(v);
            }
            LanguageBox.SelectedItem = LocalizeCore.CurrentCulture;

            HitThroughBox.IsChecked = mw.Set.HitThrough;
            PetHelperBox.IsChecked = mw.Set.PetHelper;

            if (mw.Set.StartRecordLast == true)
            {
                StartPlace.IsChecked = true;
                TextBoxStartUpX.IsEnabled = false;
                TextBoxStartUpY.IsEnabled = false;
                BtnStartUpGet.IsEnabled = false;
            }
            else
            {
                StartPlace.IsChecked = false;
                TextBoxStartUpX.IsEnabled = true;
                TextBoxStartUpY.IsEnabled = true;
                BtnStartUpGet.IsEnabled = true;
            }

            foreach (ComboBoxItem v in CBAutoSave.Items)
            {
                if ((int)v.Tag == mw.Set.AutoSaveInterval)
                {
                    CBAutoSave.SelectedItem = v;
                    break;
                }
            }
            foreach (ComboBoxItem v in CBSmartMove.Items)
            {
                if ((int)v.Tag == mw.Set.SmartMoveInterval)
                {
                    CBSmartMove.SelectedItem = v;
                    break;
                }
            }

            foreach (var v in mw.Fonts)
            {
                FontBox.Items.Add(v.TranslateName);
            }
            FontBox.SelectedIndex = mw.Fonts.FindIndex(x => x.Name == mw.Set.Font);

            foreach (var v in mw.Themes)
            {
                ThemeBox.Items.Add(v.TranslateName);
            }
            if (mw.Theme != null)
                ThemeBox.SelectedItem = mw.Theme.TranslateName;



            foreach (Sub sub in mw.Set["diy"])
                StackDIY.Children.Add(new DIYViewer(sub));

            SliderResolution.Maximum = Math.Min(System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width,
                System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height);
            SliderResolution.Value = mw.Set.Resolution;

            SwitchOpacityHitThrough.IsChecked = mw.Set.OpacityHitThrough;
            SwitchOpacity.IsChecked = mw.Set.OpacityMain;
            OpacitySlider.Value = mw.Set.Opacity;

#if X64
            GameVerison.Content = "娜娜莉桌宠 · 本地开发版 · x64";
#else
            GameVerison.Content = "娜娜莉桌宠 · 本地开发版 · x86";
#endif
            //为侧边添加目录           
            ListMenuItems.Add(listmenuswith("置于顶层", 0, TopMostBox));
            ListMenuItems.Add(listmenuswith("开机启动", 0, StartUpBox));

            ListMenuItems.Add(listmenuswith("隐藏窗口", 0, SwitchHideFromTaskControl));

            ListMenuItems.Add(listmenuswith("自动保存频率", 1, CBAutoSave));
            ListMenuItems.Add(listmenuswith("从备份中还原", 1, numBackupSaveMaxNum));

            ListMenuItems.Add(listmenuswith("数据与缓存", 1, btn_cleancache));
            ListMenuItems.Add(listmenuswith("桌宠多开", 1, btn_mutidel));

            ListMenuItems.Add(listmenuswith("互动设置", 2, CalFunctionBox));
            ListMenuItems.Add(listmenuswith("计算间隔", 2, CalSlider));
            ListMenuItems.Add(listmenuswith("桌宠移动", 2, MoveEventBox));
            ListMenuItems.Add(listmenuswith("操作设置", 2, PressLengthSlider));
            ListMenuItems.Add(listmenuswith("桌宠名字", 2, TextBoxPetName));


            ListMenuItems.Add(listmenuswith("自定义链接", 3, btn_DIY));






            ListMenuItems.Add(listmenuswith("关于娜娜莉", MainTab.Items.IndexOf(AboutTab), AboutContent));
            ListMenuItems.Add(listmenuswith("AI / API", MainTab.Items.IndexOf(CompanionSettingsTab), CompanionSettingsHost));

            foreach (var v in ListMenuItems)
                ListMenu.Items.Add(v);

            AllowChange = true;

            UpdateMoveAreaText();



        }

        internal void AttachCompanionSettings(Companion.CompanionController controller)
        {
            CompanionSettingsHost.Content = new Companion.CompanionSettingsPanel(controller);
        }

        internal void ShowCompanionSettings()
        {
            tb_seach_menu.Clear();
            MainTab.SelectedItem = CompanionSettingsTab;
            Show();
            Activate();
        }

        public List<ListBoxItem> ListMenuItems = new List<ListBoxItem>();
        private void tb_seach_menu_textchange(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            ListMenu.Items.Clear();
            if (string.IsNullOrEmpty(tb_seach_menu.Text))
            {
                foreach (var v in ListMenuItems)
                    ListMenu.Items.Add(v);
                return;
            }
            foreach (var v in ListMenuItems)
            {
                if (((string)v.Content).Contains(tb_seach_menu.Text))
                    ListMenu.Items.Add(v);
            }
        }

        private ListBoxItem listmenuswith(string content, int page, FrameworkElement element)
        {
            var lbi = new ListBoxItem() { Content = content.Translate() };
            lbi.PreviewMouseLeftButtonDown += (_, _) =>
            {
                if (page >= 0 && page < MainTab.Items.Count)
                    MainTab.SelectedIndex = page;

                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    Dispatcher.Invoke(element.BringIntoView);
                });

            };
            return lbi;
        }


        private void FullScreenBox_Check(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (FullScreenBox.IsChecked == true)
            {
                mw.Set.IsBiggerScreen = true;
                ZoomSlider.Maximum = 8;
            }
            else
            {
                mw.Set.IsBiggerScreen = false;
                ZoomSlider.Maximum = 3;
            }
        }

        private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.LoadTheme(mw.Themes[ThemeBox.SelectedIndex].xName);
            mw.Set.Theme = mw.Theme?.xName ?? string.Empty;
        }

        private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange || FontBox.SelectedIndex < 0 || FontBox.SelectedIndex >= mw.Fonts.Count)
                return;
            string str = mw.Fonts[FontBox.SelectedIndex].Name;
            mw.LoadFont(str);
            mw.Set.Font = str;
        }

        private void CBAutoSave_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetAutoSaveInterval((int)((ComboBoxItem)CBAutoSave.SelectedItem).Tag);
        }


        public void UpdateMoveAreaText()
        {
            var mwCtrl = mw.Core.Controller as MWController;
            if (mwCtrl!.IsPrimaryScreen && !mwCtrl.AutoChangeWindow)
            {
                textMoveArea.Text = "主屏幕".Translate();
                return;
            }
            else if (mwCtrl.AutoChangeWindow)
            {
                textMoveArea.Text = "自动选择窗口".Translate();
                return;
            }
            var rect = mwCtrl.ScreenBorder;
            textMoveArea.Text = $"X:{rect.X};Y:{rect.Y};W:{rect.Width};H:{rect.Height}";
        }

        private void BtnSetMoveArea_Default_Click(object sender, RoutedEventArgs e)
        {
            var mwCtrl = mw.Core.Controller as MWController;
            mwCtrl!.ResetScreenBorder();
            UpdateMoveAreaText();
        }

        private void BtnSetMoveArea_DetectScreen_Click(object sender, RoutedEventArgs e)
        {
            var mwCtrl = mw.Core.Controller as MWController;
            mwCtrl!.SetNowScreenActivate();
            UpdateMoveAreaText();
        }

        internal static System.Reflection.FieldInfo? leftGetter, topGetter;
        private void BtnSetMoveArea_Window_Click(object sender, RoutedEventArgs e)
        {
            var wma = new winMoveArea(mw);
            var mwCtrl = mw.Core.Controller as MWController;
            if (!mwCtrl!.IsPrimaryScreen)
            {
                var rect = mwCtrl.ScreenBorder;
                wma.Width = rect.Width;
                wma.Height = rect.Height;
                wma.Left = rect.X;
                wma.Top = rect.Y;
            }
            wma.ShowDialog();
        }



        public new void Show()
        {

            base.Show();
        }

        private void WindowX_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            mw.Topmost = mw.Set.TopMost;
            e.Cancel = mw.CloseConfirm;

            Hide();
        }

        private void TopMostBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.TopMost = true;
            (mw.notifyIcon.ContextMenuStrip!.Items.Find("NotifyIcon_TopMost", false).First() as System.Windows.Forms.ToolStripMenuItem)!.Checked = true;
        }

        private void TopMostBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.TopMost = false;
            (mw.notifyIcon.ContextMenuStrip!.Items.Find("NotifyIcon_TopMost", false).First() as System.Windows.Forms.ToolStripMenuItem)!.Checked = false;
        }

        private void ZoomSlider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.SetZoomLevel(ZoomSlider.Value / 2);
        }

        private void PressLengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.PressLength = (int)(PressLengthSlider.Value * 1000);
        }

        private void InteractionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.InteractionCycle = (int)(InteractionSlider.Value);
            CalTimeInteraction();
        }
        private void CalTimeInteraction()
        {
            var interact = (60 / mw.Set.LogicInterval);
            rTimeMinute.Text = interact.ToString("f2");
            RInter.Text = ((0.08 * mw.Set.InteractionCycle - 0.9) / interact * 2).ToString("f2");
        }
        private void CalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetLogicInterval(CalSlider.Value);
            CalTimeInteraction();
        }

        private void MoveEventBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetAllowMove(MoveEventBox.IsChecked == true);
        }

        private void SmartMoveEventBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetSmartMove(SmartMoveEventBox.IsChecked == true);
        }

        private void CBSmartMove_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetSmartMoveInterval((int)((ComboBoxItem)CBSmartMove.SelectedItem).Tag);
        }


        public void GenStartUP()
        {
            mw.Set["v"][(gbol)"newverstartup"] = true;
            var path = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\NanallyDesktopPet.lnk";
            if (mw.Set.StartUPBoot)
            {
                if (File.Exists(path))
                    File.Delete(path);
                var link = (IShellLink)new ShellLink();
                link.SetPath(Environment.ProcessPath!);
                link.SetWorkingDirectory(ApplicationPaths.InstallDirectory);
                link.SetDescription(ApplicationPaths.ProductName);
                link.SetIconLocation(System.IO.Path.Combine(ApplicationPaths.InstallDirectory, "Nanally.ico"), 0);
                try
                {
                    var file = (IPersistFile)link;
                    file.Save(path, false);
                }
                catch
                {
                    MessageBox.Show("创建快捷方式失败,权限不足\n请以管理员身份运行后重试".Translate(), "权限不足".Translate());
                }
            }
            else
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
        private void StartUpBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (StartUpBox.IsChecked == true)
                if (MessageBoxX.Show("该游戏随着开机启动该程序\r如需卸载游戏\r请关闭该选项".Translate() + "\n------\n" + "我已确认,并在卸载游戏前会关闭该功能".Translate(), "开机启动重要消息".Translate(),
                    MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
                {
                    StartUpBox.IsChecked = false;
                    return;
                }
            //else
            //{
            //    mf.Set["SingleTips"][(gint)"open"] = 1;
            //    MessageBoxX.Show("游戏开机启动的实现方式是创建快捷方式,不是注册表,更健康,所以游戏卸了也不知道\n如果游戏打不开,可以去这里手动删除游戏开机启动快捷方式:\n%appdata%\\Microsoft\\Windows\\Start Menu\\Programs\\Startup\\".Translate()
            //        , "关于卸载不掉的问题是因为开启了开机启动".Translate(), MessageBoxIcon.Info);
            //}

            mw.Set.StartUPBoot = StartUpBox.IsChecked == true;
            GenStartUP();
        }




        private void LoadMutiUI()
        {
            LBHave.Items.Clear();
            foreach (var str in App.MutiSaves)
            {
                var rn = str.Translate();
                if (str == "")
                    rn = "默认存档".Translate();
                if (str == mw.PrefixSave.Trim('-'))
                    rn += " (" + "当前存档".Translate() + ')';
                LBHave.Items.Add(rn);
            }
        }


        private void TextBoxPetName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Core.Save!.Name = TextBoxPetName.Text;

        }

        private void DIY_ADD_Click(object sender, RoutedEventArgs e)
        {
            StackDIY.Children.Add(new DIYViewer());
        }

        private void DIY_Save_Click(object sender, RoutedEventArgs e)
        {
            mw.Set["diy"].Clear();
            foreach (DIYViewer dv in StackDIY.Children)
            {
                mw.Set["diy"].Add(dv.ToSub());
            }
            mw.LoadDIY();
        }


        private void StartPlace_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (StartPlace.IsChecked == true)
            {
                mw.Set.StartRecordLast = true;
                TextBoxStartUpX.IsEnabled = false;
                TextBoxStartUpY.IsEnabled = false;
                BtnStartUpGet.IsEnabled = false;
            }
            else
            {
                mw.Set.StartRecordLast = false;
                TextBoxStartUpX.IsEnabled = true;
                TextBoxStartUpY.IsEnabled = true;
                BtnStartUpGet.IsEnabled = true;
            }
        }

        private void TextBoxStartUp_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (double.TryParse(TextBoxStartUpX.Text, out double x) && double.TryParse(TextBoxStartUpY.Text, out double y))
                mw.Set.StartRecordPoint = new Point(x, y);
        }

        private void BtnStartUpGet_Click(object sender, RoutedEventArgs e)
        {
            AllowChange = false;
            TextBoxStartUpX.Text = mw.Left.ToString();
            TextBoxStartUpY.Text = mw.Top.ToString();
            mw.Set.StartRecordPoint = new Point(mw.Left, mw.Top);
            AllowChange = true;
        }

        private void CalFunctionBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            //MessageBoxX.Show("由于没做完,暂不支持数据计算\n敬请期待后续更新", "没做完!", MessageBoxButton.OK, MessageBoxIcon.Warning);
            if (CalFunctionBox.IsChecked == true)
            {
                mw.Set.EnableFunction = true;
                combCalFunState.IsEnabled = false;
            }
            else
            {
                mw.Set.EnableFunction = false;
                combCalFunState.IsEnabled = true;
                if (mw.Main.State != Main.WorkingState.Nomal)
                {
                    mw.Main.WorkTimer!.Visibility = Visibility.Collapsed;
                    mw.Main.State = Main.WorkingState.Nomal;
                }
            }
        }

        private void SwitchMsgOut_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.MessageBarOutside = SwitchMsgOut?.IsChecked ?? false;
            if (SwitchMsgOut?.IsChecked ?? false)
                mw.Main.MsgBar?.SetPlaceOUT();
            else
                mw.Main.MsgBar?.SetPlaceIN();
        }

        private void numBackupSaveMaxNum_ValueChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<double?> e)
        {
            if (!AllowChange)
                return;
            mw.Set.BackupSaveMaxNum = (int)(numBackupSaveMaxNum?.Value ?? 0);
        }
        private void BtnOpenSaveManager_Click(object sender, RoutedEventArgs e)
        {
            new winSaveManager(mw).ShowDialog();
        }





        private void combCalFunState_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.CalFunState = (IGameSave.ModeType)combCalFunState.SelectedIndex;
            mw.Main.NoFunctionMOD = (IGameSave.ModeType)combCalFunState.SelectedIndex;
            mw.Main.EventTimer_Elapsed();
        }

        private void HitThroughBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set["v"][(gbol)"HitThrough"] = true;
            mw.Set.HitThrough = HitThroughBox.IsChecked ?? false;
            if (HitThroughBox.IsChecked ?? false != mw.HitThrough)
                mw.SetTransparentHitThrough();
            if (HitThroughBox.IsChecked ?? false)
                PetHelperBox.IsChecked = true;
        }

        private void PetHelperBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (PetHelperBox.IsChecked == true)
            {
                mw.Set.PetHelper = true;
                mw.LoadPetHelper();
            }
            else
            {
                mw.Set.PetHelper = false;
                mw.petHelper?.Close();
                mw.petHelper = null;
            }
        }

        private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.SetLanguage((string)LanguageBox.SelectedItem);
            TextBoxPetName.Text = mw.Core.Save!.Name;
            ButtonRestartGraph.Visibility = Visibility.Visible;

        }






        private void cleancache_click(object sender, RoutedEventArgs e)
        {
            mw.Set.LastCacheDate = DateTime.MinValue;
            MessageBoxX.Show("清理指令已下达,下次启动桌宠时生效".Translate());
        }

        private void SliderResolution_MouseUp(object sender, MouseButtonEventArgs e)
        {
            mw.Set.Resolution = (int)SliderResolution.Value;
            ButtonRestartGraph.Visibility = Visibility.Visible;
        }

        private void save_click(object sender, RoutedEventArgs e)
        {
            mw.Save();
            MessageBoxX.Show("保存成功".Translate());
        }



        private void restart_click(object sender, RoutedEventArgs e)
        {
            if (MessageBoxX.Show("是否重置游戏数据重新开始?".Translate(), "重新开始".Translate(), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var oldsave = mw.GameSavesData;
                mw.GameSavesData = new GameSave_v2(mw.Core.Save!.Name);
                mw.Core.Save = mw.GameSavesData.GameSave;
                mw.GameSavesData.GameSave.Event_LevelUp += mw.LevelUP;

                if (oldsave.HashCheck) // 对于重开无作弊的玩家保留统计
                {
                    mw.GameSavesData.Statistics = oldsave.Statistics;
                    if (oldsave.GameSave.Money > 10000000 || oldsave.GameSave.Money < -1000000000 || oldsave.GameSave.Exp > 100000000 || oldsave.GameSave.Exp < -10000000000)
                    {
                        mw.Core.Save!.Money = 10000;
                        mw.Core.Save!.Exp = 10000;
                    }
                }
                mw.HashCheck = true;
                MessageBoxX.Show("重置成功".Translate());
            }
        }



        private void btn_muti_open_click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            if (str.EndsWith(")") || App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == str) != null)
            {
                MessageBoxX.Show("当前多开已经加载".Translate());
                return;
            }
            new MainWindow(str, mw).Show();
        }

        private void btn_mutinew_click(object sender, RoutedEventArgs e)
        {
            var savename = TBNew.Text;
            foreach (var c in @"()#:|/\?*<>-")
                if (savename.Contains(c))
                {
                    MessageBoxX.Show("存档名不能包括特殊符号".Translate());
                    return;
                }
            if (App.MutiSaves.FirstOrDefault(x => x.ToLowerInvariant() == savename.ToLowerInvariant()) != null)
            {
                MessageBoxX.Show("存档名重复".Translate());
                return;
            }

            var lps = new LPS(mw.Set);
            lps.SetInt("savetimes", 0);
            File.WriteAllText(ExtensionValue.BaseDirectory + @$"\Setting-{savename}.lps", lps.ToString());
            App.MutiSaves.Add(savename);
            new MainWindow(savename, mw).Show();
        }

        private void btn_mutidel_Click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            if (str == "默认存档".Translate())
            {
                MessageBoxX.Show("默认存档无法删除,请使用重新开始功能重新开始游戏".Translate());
                return;
            }
            if (str.EndsWith(")") || App.MainWindows.FirstOrDefault(x => x.PrefixSave.Trim('-') == str) != null)
            {
                MessageBoxX.Show("当前多开已经加载,请先关闭改多开后重试".Translate());
                return;
            }
            if (!App.MutiSaves.Contains(str))
            {
                LoadMutiUI();
                return;
            }
            if (MessageBoxX.Show("是否删除当前选择({0})的多开存档?".Translate(str), "删除前确认".Translate(), MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                File.Delete(ExtensionValue.BaseDirectory + @$"\Setting-{str}.lps");
                App.MutiSaves.Remove(str);
                LoadMutiUI();
            }
        }

        private void btn_muti_master_click(object sender, RoutedEventArgs e)
        {
            if (LBHave.SelectedIndex == -1)
                return;
            var str = App.MutiSaves[LBHave.SelectedIndex];
            foreach (var sp in new DirectoryInfo(ExtensionValue.BaseDirectory).GetFiles("startup_*"))
            {
                sp.Delete();
            }
            if (str != "")
                File.Create(ExtensionValue.BaseDirectory + @"\startup_" + str).Close();
            MessageBoxX.Show("已将当前选择 {0} 设为默认启动存档".Translate(str.Translate()));
        }



        private void ConsoleBox_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.DeBug = ConsoleBox.IsChecked ?? false;
        }

        private void TextBoxHostName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.GameSavesData.GameSave.HostName = TextBoxHostName.Text;
        }

        private void TextBoxHostBDay_SelectedDateTimeChanged(object sender, Panuon.WPF.SelectedValueChangedRoutedEventArgs<DateTime?> e)
        {
            if (!AllowChange || TextBoxHostBDay.SelectedDateTime == null)
                return;
            mw.GameSavesData.SetDateTime("HostBDay", TextBoxHostBDay.SelectedDateTime.Value);
        }

        private void SwitchOpacityHitThrough_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.OpacityHitThrough = SwitchOpacityHitThrough.IsChecked == true;
        }

        private void SwitchOpacity_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (SwitchOpacity.IsChecked == true)
            {
                mw.Set.OpacityHitThrough = true;
                mw.Set.OpacityMain = true;
                SwitchOpacityHitThrough.IsChecked = true;
                mw.Opacity = mw.Set.Opacity;
            }
            else
            {
                mw.Set.OpacityMain = false;
                mw.Opacity = 1;
            }
        }

        private void OpacitySlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            mw.Set.Opacity = OpacitySlider.Value;
            if (mw.Set.OpacityMain)
                mw.Opacity = mw.Set.Opacity;
        }

        private void AutoChangeWindowEvent_Checked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            if (AutoChangeWindowEvent.IsChecked == true)
            {
                mw.Set.AutoChangeWindow = true;
            }
            else
            {
                mw.Set.AutoChangeWindow = false;
            }
            UpdateMoveAreaText();
        }

        private void SwitchHideFromTaskControl_OnChecked(object sender, RoutedEventArgs e)
        {
            if (!AllowChange)
                return;
            mw.Set.HideFromTaskControl = SwitchHideFromTaskControl.IsChecked == true;
            ButtonRestartGraph.Visibility = Visibility.Visible;
        }


    }
}
