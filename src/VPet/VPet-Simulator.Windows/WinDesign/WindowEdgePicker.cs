using System;
using System.Windows;
using System.Windows.Controls;

namespace VPet_Simulator.Windows;

/// <summary>Explicit opt-in selection; no hooks, injected code or automatic window switching.</summary>
internal sealed class WindowEdgePicker : Window
{
    private readonly ListBox list = new() { MinHeight = 130, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly GravityWalkController controller;
    public WindowEdgePicker(MainWindow owner, GravityWalkController controller)
    {
        this.controller = controller;
        Owner = owner; Title = "娜娜莉 · 窗口外缘行走";
        Width = 580; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Margin = new Thickness(24, 12, 24, 24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var introduction = new TextBlock
        {
            Text = "选择一个未最大化的应用窗口，让娜娜莉用脚沿它的外缘行走。请在窗口周围留出足够空间。",
            TextWrapping = TextWrapping.Wrap
        };
        layout.Children.Add(introduction); Grid.SetRow(list, 1); layout.Children.Add(list);
        var bottom = new StackPanel(); Grid.SetRow(bottom, 2); layout.Children.Add(bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var attach = new Button { Content = "开始贴边", Padding = new Thickness(16, 9, 16, 9) };
        var refresh = new Button { Content = "刷新窗口", Margin = new Thickness(10, 0, 0, 0) };
        var stop = new Button { Content = "停止贴边", Margin = new Thickness(10, 0, 0, 0) };
        buttons.Children.Add(attach); buttons.Children.Add(refresh); buttons.Children.Add(stop);
        bottom.Children.Add(buttons); bottom.Children.Add(status); Content = layout;
        attach.Click += (_, _) => Start();
        list.MouseDoubleClick += (_, _) => Start();
        refresh.Click += (_, _) => Refresh();
        stop.Click += (_, _) => { controller.SetEnabled(false); status.Text = "已停止，娜娜莉已恢复普通活动。"; };
        Refresh();
    }

    private void Refresh()
    {
        list.ItemsSource = WindowEdgeSurface.Enumerate();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        status.Text = controller.WindowAttached ? $"正在沿窗口行走：{controller.TargetWindowTitle}" :
            controller.LastWindowStopReason ?? "只列出可用的普通应用窗口；最小化、最大化和桌宠自身窗口不会列出。";
    }

    private void Start()
    {
        if (list.SelectedItem is not WindowEdgeCandidate target) { status.Text = "请选择窗口，或点击刷新窗口。"; return; }
        if (controller.AttachWindow(target, out string reason)) Close();
        else status.Text = reason;
    }
}
