using LinePutScript;
using LinePutScript.Converter;
using LinePutScript.Dictionary;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Win32;

namespace VPet_Simulator.Windows
{
    internal class CoreMOD : IModInfo
    {
        /// <summary>
        /// 自动启用MOD名称
        /// </summary>
        public static readonly string[] OnModDefList = new string[] { "Core", "Nanally" };

        public static string? NowLoading = null;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Intro { get; set; } = string.Empty;
        public DirectoryInfo Path { get; set; } = null!;
        public int GameVer { get; set; }
        public int Ver { get; set; }
        public HashSet<string> Tag { get; set; } = new HashSet<string>();
        public bool SuccessLoad = true;
        public DateTime CacheDate;
        public string ErrorMessage = string.Empty;
        public static string INTtoVER(int ver) => ver < 10000 ? $"{ver / 100}.{ver % 100:00}" : $"{ver / 10000}.{ver % 10000 / 100}.{ver % 100:00}";
        public static void LoadImage(MainWindow mw, DirectoryInfo di, string pre = "")
        {
            //加载其他放在文件夹的图片
            foreach (FileInfo fi in di.EnumerateFiles("*.png"))
            {
                mw.ImageSources.AddSource(pre + fi.Name.ToLowerInvariant().Substring(0, fi.Name.Length - 4), fi.FullName);
            }
            //加载其他放在文件夹中文件夹的图片
            foreach (DirectoryInfo fordi in di.EnumerateDirectories())
            {
                LoadImage(mw, fordi, pre + fordi.Name + "_");
            }
            //加载标志好的图片和图片设置
            foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
            {
                var tmp = new LpsDocument(File.ReadAllText(fi.FullName));
                if (fi.Name.ToLowerInvariant().StartsWith("set_"))
                    foreach (var line in tmp)
                        mw.ImageSources.ImageSetting.AddorReplaceLine(line);
                else
                    mw.ImageSources.AddImages(tmp, di.FullName);
            }
        }
        public static void LoadFile(MainWindow mw, DirectoryInfo di, string pre = "")
        {
            //加载其他放在文件夹的文件
            foreach (FileInfo fi in di.EnumerateFiles())
            {
                mw.FileSources.AddSource(pre + fi.Name, fi.FullName);
            }
            //加载其他放在文件夹中文件夹的文件
            foreach (DirectoryInfo fordi in di.EnumerateDirectories())
            {
                LoadFile(mw, fordi, pre + fordi.Name + "_");
            }
        }
        public CoreMOD(DirectoryInfo directory, MainWindow mw)
        {
#if !DEBUG
            try
            {
#endif
            Path = directory;
            LpsDocument modlps = new LpsDocument(File.ReadAllText(directory.FullName + @"\info.lps"));
            Name = modlps.FindLine("vupmod")!.Info;
            NowLoading = Name;
            Intro = modlps.FindLine("intro")!.Info;
            GameVer = modlps.FindSub("gamever")!.InfoToInt;
            Ver = modlps.FindSub("ver")!.InfoToInt;
            Author = modlps.FindSub("author")!.Info.Split('[').First();
            CacheDate = modlps.GetDateTime("cachedate", DateTime.MinValue);
            if (CacheDate > DateTime.Now)
            {//去掉不合理的清理缓存日期
                CacheDate = DateTime.MinValue;
            }

            //MOD未加载时支持翻译
            foreach (var line in modlps.FindAllLine("lang"))
            {
                List<ILine> ls = new List<ILine>();
                foreach (var sub in line)
                {
                    ls.Add(new Line(sub.Name, sub.info));
                }
                LocalizeCore.AddCulture(line.info, ls);
            }

            if (mw.CoreMODs.FirstOrDefault(x => x.Name == Name) != null)
            {
                Name += $"({"MOD名称重复".Translate()})";
                ErrorMessage = "MOD名称重复".Translate();
                return;
            }

            if (!IsOnMOD(mw))
            {
                Tag.Add("该模组已停用");
                foreach (DirectoryInfo di in Path.EnumerateDirectories())
                    Tag.Add(di.Name.ToLowerInvariant());
                return;
            }

            foreach (DirectoryInfo di in Path.EnumerateDirectories())
            {
                switch (di.Name.ToLowerInvariant())
                {
                    case "theme":
                        Tag.Add("theme");
                        if (Directory.Exists(di.FullName + @"\fonts"))
                            foreach (var str in Directory.EnumerateFiles(di.FullName + @"\fonts", "*.ttf"))
                            {
                                mw.Fonts.Add(new IFont(new FileInfo(str)));
                            }

                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            var tmp = new Theme(new LpsDocument(File.ReadAllText(fi.FullName)));
                            var oldtheme = mw.Themes.Find(x => x.xName == tmp.xName);
                            if (oldtheme != null)
                                mw.Themes.Remove(oldtheme);
                            mw.Themes.Add(tmp);
                            //加载图片包
                            DirectoryInfo tmpdi = new DirectoryInfo(di.FullName + '\\' + tmp.Image);
                            if (tmpdi.Exists)
                            {
                                foreach (FileInfo tmpfi in tmpdi.EnumerateFiles("*.png"))
                                {
                                    tmp.Images.AddSource(tmpfi.Name.ToLowerInvariant().Substring(0, tmpfi.Name.Length - 4), tmpfi.FullName);
                                }
                                foreach (DirectoryInfo fordi in tmpdi.EnumerateDirectories())
                                {
                                    foreach (FileInfo tmpfi in fordi.EnumerateFiles("*.png"))
                                    {
                                        tmp.Images.AddSource(fordi.Name + '_' + tmpfi.Name.ToLowerInvariant().Substring(0, tmpfi.Name.Length - 4), tmpfi.FullName);
                                    }
                                }
                            }
                        }
                        break;
                    case "pet":
                        //宠物模型                           
                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            LpsDocument lps = new LpsDocument(File.ReadAllText(fi.FullName));
                            if (lps.First()!.Name.ToLowerInvariant() == "pet")
                            {
                                var name = lps.First()!.Info;
                                if (name == "默认虚拟桌宠")
                                    name = "vup";//旧版本名称兼容

                                var p = mw.Pets.FirstOrDefault(x => x.Name == name);
                                if (p == null)
                                {
                                    Tag.Add("pet");
                                    p = new PetLoader(lps, di);
                                    if (p.Config.Works.Count > 0)
                                        Tag.Add("work");
                                    mw.Pets.Add(p);
                                }
                                else
                                {
                                    if (lps.FindAllLine("work").Length >= 0)
                                    {
                                        Tag.Add("work");
                                    }
                                    var dis = new DirectoryInfo(di.FullName + "\\" + lps.First()!["path"].Info);
                                    if (dis.Exists && dis.GetDirectories().Length > 0)
                                        Tag.Add("pet");
                                    p.path.Add(di.FullName + "\\" + lps.First()!["path"].Info);
                                    p.Config.Set(lps);
                                }
                            }
                        }
                        break;
                    case "food":
                        Tag.Add("food");
                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            var tmp = new LpsDocument(File.ReadAllText(fi.FullName));
                            foreach (ILine li in tmp)
                            {
                                if (li.Name != "food")
                                    continue;
                                string tmps = li.Find("name")!.info;
                                mw.Foods.RemoveAll(x => x.Name == tmps);
                                mw.Foods.Add(LPSConvert.DeserializeObject<Food>(li)!);
                            }
                        }
                        break;
                    case "image":
                        Tag.Add("image");
                        LoadImage(mw, di);
                        break;
                    case "file":
                        Tag.Add("file");
                        LoadFile(mw, di);
                        break;
                    case "photo":
                        Tag.Add("photo");
                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            var tmp = new LPS(File.ReadAllText(fi.FullName));
                            foreach (Line li in tmp)
                            {
                                if (li.Name != "photo")
                                    continue;
                                mw.Photos.Add(new Photo(li));
                            }
                        }
                        break;
                    case "text":
                        Tag.Add("text");
                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            var tmp = new LpsDocument(File.ReadAllText(fi.FullName));
                            foreach (ILine li in tmp)
                            {
                                switch (li.Name.ToLowerInvariant())
                                {
                                    case "lowfoodtext":
                                        mw.LowFoodText.Add(LPSConvert.DeserializeObject<LowText>(li)!);
                                        Tag.Add("lowtext");
                                        break;
                                    case "lowdrinktext":
                                        mw.LowDrinkText.Add(LPSConvert.DeserializeObject<LowText>(li)!);
                                        Tag.Add("lowtext");
                                        break;
                                    case "clicktext":
                                        mw.ClickTexts.Add(LPSConvert.DeserializeObject<ClickText>(li)!);
                                        Tag.Add("clicktext");
                                        break;
                                    case "selecttext":
                                        mw.SelectTexts.Add(LPSConvert.DeserializeObject<SelectText>(li)!);
                                        Tag.Add("selecttext");
                                        break;
                                    case "schedulepackage":
                                        mw.SchedulePackage.Add(LPSConvert.DeserializeObject<ScheduleTask.PackageFull>(li)!);
                                        break;
                                    case "buff":
                                        mw.BuffEffects.Add(LPSConvert.DeserializeObject<Buff.Effect>(li)!);
                                        break;
                                }
                            }
                        }
                        break;
                    case "lang":
                        Tag.Add("lang");
                        foreach (FileInfo fi in di.EnumerateFiles("*.lps"))
                        {
                            LocalizeCore.AddCulture(fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length), new LPS_D(File.ReadAllText(fi.FullName)));
                        }
                        foreach (DirectoryInfo dis in di.EnumerateDirectories())
                        {
                            foreach (FileInfo fi in dis.EnumerateFiles("*.lps"))
                            {
                                LocalizeCore.AddCulture(dis.Name, new LPS_D(File.ReadAllText(fi.FullName)));
                            }
                        }
                        if (mw.Set.Language == "null")
                        {
                            LocalizeCore.LoadDefaultCulture();
                        }
                        else
                            LocalizeCore.LoadCulture(mw.Set.Language);
                        break;
                }
            }
#if !DEBUG
            }
            catch (Exception e)
            {
                Console.WriteLine("{Name}:Error {e.Message}");
                ErrorMessage = e.Message;
                Tag.Add("该模组已损坏");
                SuccessLoad = false;
            }
#endif
        }
        public bool IsOnMOD(MainWindow mw) => mw.Set.IsOnMod(Name);
    }
    public static class ExtensionSetting
    {
        internal static bool IsOnMod(this Setting setting, string name) => CoreMOD.OnModDefList.Contains(name);
    }
}
