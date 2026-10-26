using LinePutScript;
using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using Panuon.WPF.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.Xml;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Core.GraphInfo;
using static VPet_Simulator.Core.Main;
using static VPet_Simulator.Windows.PerformanceDesktopTransparentWindow;
using Line = LinePutScript.Line;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : WindowX
    {
        internal System.Windows.Forms.NotifyIcon notifyIcon = null!;
        public PetHelper? petHelper;
        public System.Timers.Timer AutoSaveTimer = new System.Timers.Timer();

        public MainWindow()
        {
            //处理ARGS
            Args = new LPS_D();
            foreach (var str in App.Args)
            {
                Args.Add(new Line(str));
            }

            //存档前缀
            if (Args.ContainsLine("prefix"))
            {
                PrefixSave = '-' + Args["prefix"].Info;
            }
            if (Args.ContainsLine("linux"))
            {
                AllowsTransparency = true;
                WindowStyle = WindowStyle.None;
            }

            PNGAnimation.MaxLoadMemory = (int)Function.MemoryAvailable() / 2;
#if !X64
            if(PNGAnimation.MaxLoadMemory > 3000)
                PNGAnimation.MaxLoadMemory = 3000;
#endif
            if (PNGAnimation.MaxLoadMemory < 512)
                PNGAnimation.MaxLoadMemory = 512;

            PNGAnimation.MaxLoadMemory += (int)Function.MemoryUsage();

            LocalizeCore.StoreTranslation = true;
            LocalizeCore.TranslateFunc = (str) =>
            {
                var destr = Sub.TextDeReplace(str);
                if (destr != str && LocalizeCore.CurrentLPS != null && LocalizeCore.CurrentLPS.Assemblage.TryGetValue(destr, out ILine? line))
                {
                    return line.GetString();
                }
                if (str.Contains('_') && double.TryParse(str.Split('_').Last(), out double d))
                    return d.ToString();
                return null;
            };

            CultureInfo.CurrentCulture = new CultureInfo(CultureInfo.CurrentCulture.Name);
            CultureInfo.CurrentCulture.NumberFormat = new CultureInfo("en-US").NumberFormat;


            //更新存档系统
            if (Directory.Exists(ExtensionValue.BaseDirectory + @"\BackUP"))
            {
                if (!Directory.Exists(ExtensionValue.BaseDirectory + @"\Saves"))
                    Directory.Move(ExtensionValue.BaseDirectory + @"\BackUP", ExtensionValue.BaseDirectory + @"\Saves");
                else
                {
                    foreach (var file in new DirectoryInfo(ExtensionValue.BaseDirectory + @"\BackUP").GetFiles())
                        if (!File.Exists(ExtensionValue.BaseDirectory + @"\Saves\" + file.Name))
                            file.MoveTo(ExtensionValue.BaseDirectory + @"\Saves\" + file.Name);
                        else
                            file.Delete();
                    Directory.Delete(ExtensionValue.BaseDirectory + @"\BackUP", true);
                }
            }

            _dwmEnabled = Win32.Dwmapi.DwmIsCompositionEnabled();
            _hwnd = new WindowInteropHelper(this).EnsureHandle();

            GameInitialization();

            Task.Run(() => ApplicationLifecycle.RunAsync(this, async () =>
            {
                //加载所有MOD
                List<(long, long, DirectoryInfo)> Path = new();
                Path.AddRange(new DirectoryInfo(ModPath).EnumerateDirectories().Where(directory => directory.Name is "0000_core" or "Nanally")
                    .Select(directory => (-2L, -2L, directory)));

                Dispatcher.InvokeAsync(new Action(() => LoadingText.Content = "Loading Translate")).Wait();
                //加载语言
                LocalizeCore.StoreTranslation = true;
                if (Set.Language == "null")
                {
                    LocalizeCore.LoadDefaultCulture();
                    if (LocalizeCore.CurrentCulture == "null")
                        LocalizeCore.CurrentCulture = "en";
                    Set.Language = LocalizeCore.CurrentCulture;
                }
                else
                    LocalizeCore.LoadCulture(Set.Language);

                await GameLoad(Path);
                //这里写的都是限定第一个MW使用的功能, 如果写共通, 请前往

                //物品初始使用方法
                Item.UseAction.Add("Food", [(imw,Item) =>
                  {//食物: 默认直接吃掉
                      if(Item is Food food)
                      {
                          imw.TakeItem(food);
                          imw.TakeItemHandle(food, 1, "item");
                          imw.DisplayFoodAnimation(food.GetGraph(), food.ImageSource);
                          Item.Consume(imw);
                          return true;
                      }
                      return false;
                  }]);
                Item.UseAction.Add("Mail", [
                //排在前面的方法优先级更高
                (imw,Item) => {
                      switch (Item.Name)
                      {
                          case "每日礼包": //每日随机礼盒: 打开后获得随机3个物品 每天获得一个
                              var moneylimit = Math.Min(20000, (50 * (imw.GameSavesData.GameSave.LevelMax + 1) + imw.GameSavesData.GameSave.Level +1) * 50);
                              var chosenfood = imw.Foods.FindAll(x=>x.Price > 10 && x.Price < moneylimit);
                              if(chosenfood.Count == 0)
                                    return false;
                              imw.ItemsAdd(chosenfood[Function.Rnd.Next(chosenfood.Count)].Clone());
                              imw.ItemsAdd(chosenfood[Function.Rnd.Next(chosenfood.Count)].Clone());
                              imw.ItemsAdd(chosenfood[Function.Rnd.Next(chosenfood.Count)].Clone());
                              Item.Consume(imw);
                              return true;
                      }
                      return false;
                  },
                   (imw,Item) =>
                  {//邮件: 打开后获得物品
                     var lps = new LpsDocument(Item.Data);
                      List<string> itemnames = new List<string>();
                      foreach(var line in lps)
                      {
                          var itm = Item.CreateItem(imw,line);
                          if(itm == null)
                              continue;
                          itm.LoadSource(this);
                          imw.ItemsAdd(itm);
                          itemnames.Add(itm.TranslateName);
                      }
                      if(itemnames.Count != 0)
                      {
                          Main.SayRnd("你打开了{0},获得了物品".Translate(Item.Name) +"\n" + string.Join(',',itemnames));
                      }
                      Item.Consume(this);
                     return true;
                  }]);
                await Dispatcher.InvokeAsync(() => ApplicationLifecycle.LoadedAsync(this)).Task.Unwrap();
            }));
        }

        private bool closeStarted, finalClose, restarting;
        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (finalClose || restarting) return;
            e.Cancel = true;
            Close();
        }
        public new async void Close()
        {
            if (closeStarted) return;
            closeStarted = true;
            StopRuntimeTasks();
            ChatCompanion?.Stop();
            IdleTrial?.Dispose(); IdleTrial = null;
            Fall?.Cancel(); GravityWalk?.SetEnabled(false);
            try
            {
                if (Main != null)
                {
                    var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Main.Display(GraphType.Shutdown, AnimatType.Single, () => shutdown.TrySetResult());
                    await Task.WhenAny(shutdown.Task, Task.Delay(2000));
                }
            }
            finally
            {
                await LocalLogWriter.FlushAsync();
                finalClose = true;
                base.Close();
            }
        }
        public void Restart()
        {
            restarting = true;
            ChatCompanion?.Stop();
            this.Closed -= Window_Closed;
            this.Closed += Restart_Closed;
            base.Close();
        }

        private void Restart_Closed(object? sender, EventArgs? e)
        {
            CloseConfirm = false;

            Save();
            if (App.MainWindows.Count == 1)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = System.IO.Path.ChangeExtension(System.Reflection.Assembly.GetExecutingAssembly().Location, "exe"),
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else
            {
                new MainWindow(PrefixSave, this).Show();
            }
            Exit();
        }
        private void Exit()
        {
            IdleTrial?.Dispose(); IdleTrial = null; ChatCompanion?.Dispose();
            Fall?.Dispose(); Fall = null; GravityWalk?.Dispose();
            if (App.MainWindows.Count <= 1)
            {
                Task.Run(() =>
                {
                    Thread.Sleep(10000);//等待10秒不退出强退
                    Environment.Exit(0);
                });
                try
                {
                    if (Core != null && Core.Graph != null)
                    {
                        foreach (var igs in Core.Graph!.GraphsList.Values)
                        {
                            foreach (var ig2 in igs.Values)
                            {
                                foreach (var ig3 in ig2)
                                {
                                    ig3.Stop(true);
                                }
                            }
                        }
                    }
                    while (Windows.Count != 0)
                    {
                        var w = Windows[0];
                        w.Close();
                        Windows.Remove(w);
                    }
                    buffViewer?.Dispose();
                    ScheduleTask?.Dispose();
                    Buff?.Dispose();
                    Main?.Dispose();
                    AutoSaveTimer?.Stop();
                    petHelper?.Close();
                    winSetting?.Close();
                    winBetterBuy?.Close();
                    winWorkMenu?.Close();
                    

                    
                    if (notifyIcon != null)
                    {
                        notifyIcon.Visible = false;
                        notifyIcon.Dispose();
                    }
                    notifyIcon?.Dispose();
                }
                finally
                {
                    Environment.Exit(0);
                }
                while (true)
                    Environment.Exit(0);
            }
            else
            {
                if (Core != null && Core.Graph != null)
                {
                    foreach (var igs in Core.Graph!.GraphsList.Values)
                    {
                        foreach (var ig2 in igs.Values)
                        {
                            foreach (var ig3 in ig2)
                            {
                                ig3.Stop(true);
                            }
                        }
                    }
                }
                while (Windows.Count != 0)
                {
                    Windows[0].Close();
                }
                buffViewer?.Dispose();
                ScheduleTask?.Dispose();
                Buff?.Dispose();
                Main?.Dispose();
                AutoSaveTimer?.Stop();
                petHelper?.Close();
                winSetting?.Close();
                winBetterBuy?.Close();
                winWorkMenu?.Close();
                
                App.MainWindows.Remove(this);
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
                notifyIcon?.Dispose();
            }
        }


        public long lastclicktime { get; set; }

        public void LoadLatestSave(string petname)
        {
            if (Directory.Exists(ExtensionValue.BaseDirectory + @"\Saves"))
            {
                var ds = new List<string>(Directory.GetFiles(ExtensionValue.BaseDirectory + @"\Saves", $@"Save{PrefixSave}_*.lps"))
                    .OrderBy(x =>
                 {
                     if (int.TryParse(x.Split('_').Last().Split('.')[0], out int i))
                         return i;
                     return 0;
                 }).ToList();

                if (ds.Count != 0)
                {
                    int.TryParse(ds.Last().Split('_').Last().Split('.')[0], out int lastid);
                    if (Set.SaveTimes < lastid)
                    {
                        Set.SaveTimes = lastid;
                    }
                }
                for (int i = ds.Count - 1; i >= 0; i--)
                {
                    var latestsave = ds[i];
                    if (latestsave != null)
                    {
                        if (TryLoadSaveFile(latestsave))
                            return;
                    }
                }

            }
            var newSave = new GameSave_v2(petname.Translate());
            // 新建存档与读取已有存档共用 Buff、日程和物品的加载流程。
            CheckBackupConsistency(newSave, "New Game");
            SavesLoad(newSave.ToLPS());
        }

        /// <summary>
        /// 尝试加载指定存档文件
        /// </summary>
        /// <param name="saveFilePath"></param>
        /// <returns></returns>
        private bool TryLoadSaveFile(string saveFilePath)
        {
            if (string.IsNullOrEmpty(saveFilePath))
                return false;
#if !DEBUG
            try
            {
#endif
            var content = File.ReadAllText(saveFilePath);
            GameSave_v2 gs = new GameSave_v2(new LPS(content));
            // 检查备份一致性
            CheckBackupConsistency(gs, new FileInfo(saveFilePath).Name);

            if (SavesLoad(new LPS(content)))
                return true;
#if !DEBUG
            }
            catch (Exception ex)
            {
                MessageBoxX.Show("存档损毁,无法加载该存档\n可能是数据溢出/超模导致的" + '\n' + ex.Message, "存档损毁".Translate());
            }
#endif
            return false;
        }

        /// <summary>
        /// 与最新备份对比并提示用户
        /// </summary>
        private void CheckBackupConsistency(GameSave_v2 gs, string currentName)
        {
            if (!Directory.Exists(ExtensionValue.BaseDirectory + @"\Saves_BKP"))
                return;
            try
            {
                var bks = new DirectoryInfo(ExtensionValue.BaseDirectory + @"\Saves_BKP")
                    .GetFiles($"Save{PrefixSave}_*.lps").OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                if (bks != null)
                {
                    try
                    {
                        var gs2 = new GameSave_v2(new LPS(File.ReadAllText(bks.FullName)));
                        if (!(gs2.GameSave.Level == gs.GameSave.Level &&
                            gs2.GameSave.Exp == gs.GameSave.Exp &&
                            gs2.GameSave.Money == gs.GameSave.Money))
                        {
                            //和备份不一样,说明可能有问题, 提示用户
                            MessageBox.Show("检测到存档和备份不一致\n当前存档:{0} Lv{1} ${4:f0}\n备份存档:{2} Lv{3} ${5:f0}\n如需还原请在设置中加载备份还原存档"
                                .Translate(currentName, gs.GameSave.Level, bks.Name, gs2.GameSave.Level, gs.GameSave.Money, gs2.GameSave.Money)
                                , "存档不一致提示".Translate());

                        }
                    }
                    catch
                    {
                        //备份损坏了,那就不管了
                    }
                }
            }
            catch
            {

            }
        }

        private void WorkTimer_E_FinishWork(WorkTimer.FinishWorkInfo obj)
        {
            if (obj.work.Type == GraphHelper.Work.WorkType.Work)
            {
                GameSavesData.Statistics![(gint)"stat_single_profit_money"] = (int)obj.count;
            }
            else
            {
                GameSavesData.Statistics![(gint)"stat_single_profit_exp"] = (int)obj.count;
            }
        }

        private void Main_Event_TouchBody()
        {
            GameSavesData.Statistics![(gint)"stat_touch_body"]++;
        }

        private void Main_Event_TouchHead()
        {
            GameSavesData.Statistics![(gint)"stat_touch_head"]++;
        }

        private void Main_OnSay(SayInfo obj)
        {
            GameSavesData.Statistics![(gint)"stat_say_times"]++;
        }

        private void MoveTimer_Elapsed(object? sender, ElapsedEventArgs? e)
        {
            GameSavesData.Statistics![(gint)"stat_move_length"] += (int)(Math.Abs(Main.MoveTimerPoint.X) + Math.Abs(Main.MoveTimerPoint.Y));
        }

        private void AutoSaveTimer_Elapsed(object? sender, ElapsedEventArgs? e)
        {
            if (lifetime.IsCancellationRequested) return;
            try { Save(); }
            catch (Exception exception) { LocalLogWriter.Write(Path.Combine(ApplicationPaths.LogDirectory, "runtime-events.log"), $"{DateTimeOffset.Now:o} autosave {exception.GetType().Name}"); }
        }


        private void Window_Closed(object? sender, EventArgs? e)
        {
            StopRuntimeTasks();
            IdleTrial?.Dispose(); IdleTrial = null; ChatCompanion?.Dispose();
            Fall?.Dispose(); Fall = null; GravityWalk?.Dispose();
            CloseConfirm = false;

            Save();
            ApplicationLifecycle.Write("closed", this);
            Exit();
        }

        [DllImport("user32", EntryPoint = "SetWindowLong")]
        private static extern uint SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);

        [DllImport("user32", EntryPoint = "GetWindowLong")]
        private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            //const int WS_EX_TRANSPARENT = 0x20;
            //const int GWL_EXSTYLE = -20;
            //IntPtr hwnd = new WindowInteropHelper(this).Handle;
            //uint extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            //SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            ((HwndSource)PresentationSource.FromVisual(this)).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                //想要让窗口透明穿透鼠标和触摸等，需要同时设置 WS_EX_LAYERED 和 WS_EX_TRANSPARENT 样式，
                //确保窗口始终有 WS_EX_LAYERED 这个样式，并在开启穿透时设置 WS_EX_TRANSPARENT 样式
                //但是WPF窗口在未设置 AllowsTransparency = true 时，会自动去掉 WS_EX_LAYERED 样式（在 HwndTarget 类中)，
                //如果设置了 AllowsTransparency = true 将使用WPF内置的低性能的透明实现，
                //所以这里通过 Hook 的方式，在不使用WPF内置的透明实现的情况下，强行保证这个样式存在。
                if (msg == (int)Win32.WM.STYLECHANGING && (long)wParam == (long)Win32.GetWindowLongFields.GWL_EXSTYLE)
                {
#pragma warning disable CS8605 // 取消装箱可能为 null 的值。
                    var styleStruct = (STYLESTRUCT)Marshal.PtrToStructure(lParam, typeof(STYLESTRUCT));
#pragma warning restore CS8605 // 取消装箱可能为 null 的值。
                    styleStruct.styleNew |= (int)Win32.ExtendedWindowStyles.WS_EX_LAYERED;

                    // Hide windows from alt+tab: https://stackoverflow.com/questions/357076/best-way-to-hide-a-window-from-the-alt-tab-program-switcher
                    if (Set.HideFromTaskControl)
                    {
                        styleStruct.styleNew |= (int)Win32.ExtendedWindowStyles.WS_EX_TOOLWINDOW;
                    }

                    Marshal.StructureToPtr(styleStruct, lParam, false);
                    handled = true;
                }
                return IntPtr.Zero;
            });
        }
        private readonly bool _dwmEnabled;
        private readonly IntPtr _hwnd;
        public bool HitThrough { get; private set; } = false;
        public bool MouseHitThrough
        {
            get => HitThrough;
            set
            {
                if (value != HitThrough)
                    SetTransparentHitThrough();
            }
        }
        /// <summary>
        /// 设置点击穿透到后面透明的窗口
        /// </summary>
        public void SetTransparentHitThrough()
        {
            if (_dwmEnabled)
            {
                //const int WS_EX_TRANSPARENT = 0x20;
                //const int GWL_EXSTYLE = -20;
                //IntPtr hwnd = new WindowInteropHelper(this).Handle;
                //uint extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                //SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                HitThrough = !HitThrough;
                (notifyIcon.ContextMenuStrip!.Items.Find("NotifyIcon_HitThrough", false)!.First() as System.Windows.Forms.ToolStripMenuItem)!.Checked = HitThrough;
                if (HitThrough)
                {
                    Win32.User32.SetWindowLongPtr(_hwnd, Win32.GetWindowLongFields.GWL_EXSTYLE,
                        (IntPtr)(int)((long)Win32.User32.GetWindowLongPtr(_hwnd, Win32.GetWindowLongFields.GWL_EXSTYLE) | (long)Win32.ExtendedWindowStyles.WS_EX_TRANSPARENT));
                    petHelper?.SetOpacity(false);
                    if (Set.OpacityHitThrough)
                        Opacity = Set.Opacity;
                }
                else
                {
                    Win32.User32.SetWindowLongPtr(_hwnd, Win32.GetWindowLongFields.GWL_EXSTYLE,
                  (IntPtr)(int)((long)Win32.User32.GetWindowLongPtr(_hwnd, Win32.GetWindowLongFields.GWL_EXSTYLE) & ~(long)Win32.ExtendedWindowStyles.WS_EX_TRANSPARENT));
                    petHelper?.SetOpacity(true);
                    if (Set.OpacityMain)
                        Opacity = Set.Opacity;
                    else
                        Opacity = 1;
                }
            }
        }
        private void WindowX_LocationChanged(object sender, EventArgs e)
        {
            petHelper?.SetLocation();
        }
        /// <summary>
        /// 显示输入框
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="text">文本</param>
        /// <param name="defaulttext">默认文本</param>
        /// <param name="ENDAction">结束事件</param>
        /// <param name="AllowMutiLine">是否允许多行输入</param>
        /// <param name="TextCenter">文本居中</param>
        /// <param name="CanHide">能否隐藏</param>
        public void ShowInputBox(string title, string text, string defaulttext, Action<string> ENDAction, bool AllowMutiLine = false, bool TextCenter = true, bool CanHide = false)
        {
            winInputBox.Show(this, title, text, defaulttext, ENDAction, AllowMutiLine, TextCenter, CanHide);
        }
        /// <summary>
        /// </summary>
        
    }
}
