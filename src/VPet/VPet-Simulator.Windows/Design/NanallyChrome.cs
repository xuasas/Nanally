using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Media.Imaging;
using System.IO;

namespace VPet_Simulator.Windows;

internal static class NanallyChrome
{
    private static readonly DependencyProperty Applied = DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(NanallyChrome));
    public static void Initialize()
    {
        var icon = (ImageSource)Application.Current.FindResource("NanallyGiftIcon");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) context.DrawImage(icon, new Rect(48, 48, 416, 416));
        var render = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32); render.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        Application.Current.Resources["NanallyGiftBitmap"] = image;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
    }
    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !ReferenceEquals(e.OriginalSource, window) || window is MainWindow or PetHelper ||
            window.GetType().Assembly != typeof(MainWindow).Assembly || (bool)window.GetValue(Applied)) return;
        window.SetValue(Applied, true);
        window.WindowStyle = WindowStyle.None;
        window.Template = (ControlTemplate)Application.Current.FindResource("NanallyWindowTemplate");
        window.FontFamily = new FontFamily("Microsoft YaHei UI");
        window.Foreground = (Brush)Application.Current.FindResource("NanallyInk");
        window.Background = (Brush)Application.Current.FindResource("NanallyCanvas");
        window.MinHeight = Math.Max(window.MinHeight, 280);
        if (window is winGameSetting) window.Title = "娜娜莉 · 设置";
        else if (window is winBetterBuy) window.Title = "娜娜莉 · 物品商店";
        else if (window is winInventory) window.Title = "娜娜莉 · 背包";
        else if (window is winSaveManager) window.Title = "娜娜莉 · 本地存档";
        else if (window is winCharacterPanel) window.Title = "娜娜莉 · 成长记录";
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 72, CornerRadius = new CornerRadius(14),
            ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.CanResize ? 5 : 0), GlassFrameThickness = new Thickness(0) });
        window.ApplyTemplate();
        if (window.Template.FindName("NanallyTools", window) is ContentPresenter tools)
            tools.Visibility = window is winInventory or winBetterBuy ? Visibility.Visible : Visibility.Collapsed;
        if (window.Template.FindName("NanallyClose", window) is Button close) close.Click += (_, _) => window.Close();
        if (window.Template.FindName("NanallyMinimize", window) is Button minimize) minimize.Click += (_, _) => window.WindowState = WindowState.Minimized;
    }
}
