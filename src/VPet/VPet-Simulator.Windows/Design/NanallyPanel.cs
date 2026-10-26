using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows;

/// <summary>Single navigation surface. Existing panels remain responsible for business operations.</summary>
internal sealed class NanallyPanel : Window
{
    private readonly MainWindow pet;
    private readonly ContentControl page = new();
    private readonly StackPanel navigation = new();
    public NanallyPanel(MainWindow pet)
    {
        this.pet = pet;
        Title = "娜娜莉 · 陪伴空间"; Width = 1000; Height = 740; MinWidth = 780; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(180) }); root.ColumnDefinitions.Add(new());
        root.Children.Add(navigation); Grid.SetColumn(page, 1); root.Children.Add(page); Content = root;
        navigation.Margin = new Thickness(0, 18, 20, 0);
        Nav("陪伴", () => Render("陪伴", "有话想说，随时都可以来找我。", ("和娜娜莉聊聊", () => pet.ChatCompanion!.Show())));
        Nav("日常", () => Render("一起度过今天", "整理资料、读一会儿书，或者放松一下。", ("整理资料", () => Work(GraphHelper.Work.WorkType.Work)),
            ("看书", () => Work(GraphHelper.Work.WorkType.Study)), ("玩掌机", () => Work(GraphHelper.Work.WorkType.Play))));
        Nav("物品", () => Render("小小收藏", "照顾日常，也留一点空间给喜欢的东西。", ("打开背包", () => new winInventory(pet).Show()),
            ("逛逛商店", () => pet.winBetterBuy!.Show(Food.FoodType.Meal))));
        Nav("成长", () => Render("我们的日常记录", "看看娜娜莉现在的状态，以及一起度过的时光。", ("状态与成长", () => new winCharacterPanel(pet).Show()),
            ("本地存档", () => new winSaveManager(pet).Show())));
        Nav("设置", () => Render("按照你的习惯", "调整外观、互动和 AI 服务。", ("桌宠设置", () => pet.ShowSetting()),
            ("AI 设置", () => pet.ChatCompanion!.Show(true))));
        Select(0);
    }
    private void Work(GraphHelper.Work.WorkType type)
    {
        if (pet.winWorkMenu == null) pet.winWorkMenu = new winWorkMenu(pet, type);
        else pet.winWorkMenu.LsbCategory.SelectedIndex = (int)type;
        pet.winWorkMenu.Show(); pet.winWorkMenu.Activate();
    }
    private void Nav(string title, Action action)
    {
        var button = new Button { Content = title, HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(20, 14, 20, 14), Tag = action };
        int index = navigation.Children.Count;
        button.Click += (_, _) => Select(index); navigation.Children.Add(button);
    }
    internal void Select(int index)
    {
        for (int i = 0; i < navigation.Children.Count; i++)
        {
            var button = (Button)navigation.Children[i];
            button.FontWeight = i == index ? FontWeights.SemiBold : FontWeights.Normal;
            button.BorderBrush = (Brush)FindResource(i == index ? "NanallyAccent" : "NanallyLine");
        }
        ((Action)((Button)navigation.Children[index]).Tag)();
    }
    private void Render(string title, string subtitle, params (string Title, Action Open)[] actions)
    {
        var content = new StackPanel { Margin = new Thickness(24, 26, 24, 0) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("NanallyMuted"), Margin = new Thickness(0, 10, 0, 24) });
        foreach (var action in actions)
        {
            var button = new Button { Content = action.Title + "   →", FontSize = 18, Padding = new Thickness(22),
                Margin = new Thickness(0, 0, 0, 16), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => action.Open(); content.Children.Add(button);
        }
        content.Children.Add(new TextBlock { Text = "NANALLY / EVERYDAY TOGETHER", FontSize = 10,
            Foreground = (Brush)FindResource("NanallyMuted"), Margin = new Thickness(0, 20, 0, 0) });
        page.Content = content;
    }
}
