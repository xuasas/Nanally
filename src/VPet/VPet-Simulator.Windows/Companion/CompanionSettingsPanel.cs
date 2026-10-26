using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VPet_Simulator.Windows.Companion;

/// <summary>Embedded AI settings page; existing credentials never enter the password box.</summary>
internal sealed class CompanionSettingsPanel : UserControl
{
    private readonly CompanionController controller;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox endpoint = new(), model = new(), timeout = new(), turns = new(), persona = new();
    private readonly PasswordBox key = new();
    private readonly CheckBox clearKey = new() { Content = "删除已保存的密钥（重新填写新密钥可替换）" };
    public CompanionSettingsPanel(CompanionController controller)
    {
        this.controller = controller;
        Background = Brushes.White; Foreground = Brushes.Black; FontSize = 14;
        var root = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(BuildSettings()); Content = root;
        Loaded += (_, _) =>
        {
            controller.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => { controller.Changed -= Refresh; key.Clear(); clearKey.IsChecked = false; };
        Refresh();
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 6, 8, 6), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => action();
        return button;
    }
    private UIElement BuildSettings()
    {
        CompanionOptions options = controller.Options;
        endpoint.Text = options.BaseUrl; model.Text = options.Model;
        timeout.Text = options.TimeoutSeconds.ToString(); turns.Text = options.HistoryTurns.ToString();
        persona.Text = options.Persona; persona.AcceptsReturn = true; persona.TextWrapping = TextWrapping.Wrap;
        persona.Height = 135; persona.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var panel = new StackPanel { Margin = new Thickness(0, 12, 12, 0) };
        void Field(string label, UIElement element)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(element);
        }
        Field("服务地址（Base URL）", endpoint); Field("模型名称", model);
        Field(controller.Store.HasKey ? "API Key 已保存；留空保留。更换服务地址后须重新填写。" : "API Key（仅保存在当前 Windows 账户的加密文件中）", key);
        panel.Children.Add(clearKey);
        Field("请求超时（1–180 秒）", timeout); Field("保留上下文（0–12 轮，仅在本次运行内存中保存）", turns);
        Field("娜娜莉人设", persona);
        var actions = new WrapPanel();
        actions.Children.Add(Button("保存设置", () => Save()));
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "发送的文字、人设和有限上下文会传给你配置的模型服务。密钥、对话正文不写入诊断日志。保存设置会停止当前对话并清空上下文。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    private bool Save()
    {
        try
        {
            if (!int.TryParse(timeout.Text, out int seconds) || !int.TryParse(turns.Text, out int count))
                throw new ArgumentException("超时和上下文轮数需要填写整数。");
            var options = controller.Options with
            {
                BaseUrl = endpoint.Text.Trim(), Model = model.Text.Trim(), Persona = persona.Text,
                TimeoutSeconds = seconds, HistoryTurns = count
            };
            controller.SaveOptions(options, key.Password, clearKey.IsChecked == true);
            key.Clear(); clearKey.IsChecked = false;
            return true;
        }
        catch (ArgumentException exception) { status.Text = exception.Message; }
        catch (Exception exception) { status.Text = $"设置未保存（{exception.GetType().Name}），请检查用户目录权限或重新填写密钥。"; }
        return false;
    }
    private void Refresh() => status.Text = controller.Status;
}
