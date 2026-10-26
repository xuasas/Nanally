using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VPet_Simulator.Windows.Companion;

/// <summary>Explicit image attachments stay in a bounded draft until a successful send; configuration uses the shared settings page.</summary>
internal sealed class CompanionWindow : Window
{
    private readonly CompanionController controller;
    private readonly StackPanel messages = new();
    private readonly ScrollViewer conversation = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 75, MaxLength = 6000 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock uploadNotice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock viewNotice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
    private readonly WrapPanel attachmentPreviews = new();
    private readonly ScrollViewer attachmentArea = new() { MaxHeight = 190, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly List<ChatImage> pendingImages = new();
    private readonly Dictionary<UIElement, (long Bytes, long Pixels)> displayedPictures = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? importCancellation;
    private long importGeneration, draftRevision, textRevision, sendGeneration;
    private long? sendingDraftRevision;
    private bool closed;
    private Button sendButton = null!, addImageButton = null!, cancelImportButton = null!;
    private ImagePreviewWindow? imageViewer;
    private const long DisplayPixelLimit = CompanionController.MaxHistoryImagePixels;

    internal TextBox InputBox => input;
    internal IReadOnlyList<ChatImage> PendingImages => pendingImages.AsReadOnly();
    internal int MessageCardCount => messages.Children.Count;
    internal long DisplayedImageBytes { get; private set; }
    internal long DisplayedImagePixels { get; private set; }
    internal bool IsImporting => importCancellation != null;

    public CompanionWindow(CompanionController controller)
    {
        this.controller = controller;
        Title = "和娜娜莉聊聊";
        Width = 760; Height = 860; MinWidth = 500; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.White; Foreground = Brushes.Black; FontSize = 15;
        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        Content = root;
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "今天想和我聊些什么？", FontSize = 24, FontWeight = FontWeights.SemiBold });
        heading.Children.Add(new TextBlock { Text = "文字与图片陪伴 · 心情与表情随对话变化", Foreground = (Brush)FindResource("NanallyMuted"), Margin = new Thickness(0, 6, 0, 0) });
        viewNotice.Foreground = (Brush)FindResource("NanallyMuted");
        heading.Children.Add(viewNotice);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        root.Children.Add(BuildChat());
        Append(new CompanionChatMessage("娜娜莉", "我在这里，慢慢说就好。也可以把想聊的图片发给我。", Array.Empty<ChatImage>()));
        controller.Changed += Refresh;
        controller.RichMessage += Append;
        controller.HistoryCleared += HistoryCleared;
        AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(OnPreviewDragOver), true);
        AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler(OnPreviewDrop), true);
        AllowDrop = true; input.AllowDrop = true;
        Loaded += (_, _) => input.Focus();
        Closed += OnClosed;
        Refresh();
    }

    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 6, 8, 6), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => action();
        return button;
    }

    private UIElement BuildChat()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var footer = new StackPanel();
        footer.Children.Add(new TextBlock
        {
            Text = "可拖入图片或 Ctrl+V 粘贴截图；Ctrl+Enter 发送。每条最多4张 PNG/JPG。",
            FontSize = 12, TextWrapping = TextWrapping.Wrap
        });
        uploadNotice.Foreground = (Brush)FindResource("NanallyMuted"); footer.Children.Add(uploadNotice);
        attachmentArea.Content = attachmentPreviews; attachmentArea.Visibility = Visibility.Collapsed;
        footer.Children.Add(attachmentArea); footer.Children.Add(input);
        input.Margin = new Thickness(0, 10, 0, 4);
        input.TextChanged += (_, _) => { textRevision++; draftRevision++; Refresh(); };
        DataObject.AddPastingHandler(input, OnPasting);
        var actions = new WrapPanel();
        sendButton = Button("发送消息", () => _ = SendDraftAsync());
        sendButton.Style = (Style)FindResource("NanallyPrimaryButton");
        addImageButton = Button("添加图片", ChooseImages);
        cancelImportButton = Button("取消添加", () => { CancelImport(); controller.SetLocalStatus("已取消添加图片，已有草稿保留。"); Refresh(); });
        actions.Children.Add(sendButton); actions.Children.Add(addImageButton);
        actions.Children.Add(cancelImportButton);
        actions.Children.Add(Button("停止", () =>
        {
            sendGeneration++; sendingDraftRevision = null;
            CancelImport(); controller.Stop(); Refresh();
        }));
        actions.Children.Add(Button("AI 设置", () => controller.Show(true)));
        actions.Children.Add(Button("清空上下文", controller.ClearHistory));
        footer.Children.Add(actions);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            { e.Handled = true; _ = SendDraftAsync(); }
        };
        DockPanel.SetDock(footer, Dock.Bottom); panel.Children.Add(footer);
        conversation.Content = messages; panel.Children.Add(conversation);
        return panel;
    }

    private void ChooseImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要发给娜娜莉的图片", Filter = "PNG/JPG图片|*.png;*.jpg;*.jpeg", Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) _ = ImportFilesAsync(dialog.FileNames);
    }

    internal Task ImportFilesAsync(IEnumerable<string> paths)
    {
        string[] files = paths.ToArray();
        return PrepareImagesAsync(files.Length, async token =>
        {
            var prepared = new List<ChatImage>();
            foreach (string path in files) prepared.Add(await ChatImage.FromFileAsync(path, token));
            return prepared;
        });
    }

    internal async Task<bool> ImportClipboardDataAsync(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                if (data.GetData(DataFormats.FileDrop) is string[] files) await ImportFilesAsync(files);
                else controller.SetLocalStatus("剪贴板中的文件列表无法读取，请重新复制图片文件。");
                return true;
            }
            if (data.GetDataPresent(DataFormats.Bitmap, true))
            {
                if (data.GetData(DataFormats.Bitmap, true) is not BitmapSource image)
                { controller.SetLocalStatus("截图无法读取，请重新复制截图，或使用“添加图片”。"); return true; }
                BitmapSource snapshot = image.IsFrozen ? image : (BitmapSource)image.CloneCurrentValue();
                if (!snapshot.IsFrozen) snapshot.Freeze();
                await PrepareImagesAsync(1, async token => new[] { await ChatImage.FromBitmapAsync(snapshot, "剪贴板截图", token) });
                return true;
            }
            return false;
        }
        catch (Exception exception) { ShowImportError(exception); return true; }
    }

    internal Task ImportDropDataAsync(IDataObject data)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
                return ImportFilesAsync(files);
            controller.SetLocalStatus("请拖入本地 PNG/JPG 图片文件。");
        }
        catch (Exception exception) { ShowImportError(exception); }
        return Task.CompletedTask;
    }

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Bitmap, true) && !e.DataObject.GetDataPresent(DataFormats.FileDrop)) return;
            e.CancelCommand(); e.Handled = true;
            _ = ImportClipboardDataAsync(e.DataObject);
        }
        catch (Exception exception) { e.CancelCommand(); ShowImportError(exception); }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = !IsImporting && !closed && pendingImages.Count < CompanionController.MaxImagesPerMessage
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true; _ = ImportDropDataAsync(e.Data);
    }

    private async Task PrepareImagesAsync(int count, Func<CancellationToken, Task<IReadOnlyList<ChatImage>>> prepare)
    {
        if (closed || count == 0) return;
        if (IsImporting) { controller.SetLocalStatus("正在准备图片，请稍候，或点击“取消添加”。"); return; }
        if (count + pendingImages.Count > CompanionController.MaxImagesPerMessage)
        { controller.SetLocalStatus("每条消息最多4张图片，请先移除部分图片。"); return; }
        var source = importCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        long current = ++importGeneration;
        controller.SetLocalStatus("正在准备图片…尚未上传。"); Refresh();
        try
        {
            var images = await prepare(source.Token);
            if (closed || current != importGeneration || source.IsCancellationRequested) return;
            if (images.Count + pendingImages.Count > CompanionController.MaxImagesPerMessage ||
                pendingImages.Sum(x => (long)x.ByteLength) + images.Sum(x => (long)x.ByteLength) > CompanionController.MaxMessageImageBytes)
            { controller.SetLocalStatus("本条图片总大小超限，请减少图片或缩小图片后重试。"); return; }
            pendingImages.AddRange(images); draftRevision++;
            RenderDraft();
            controller.SetLocalStatus($"已添加{images.Count}张图片，点击发送后才会上传。");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!closed && current == importGeneration) ShowImportError(exception); }
        finally
        {
            if (ReferenceEquals(importCancellation, source)) importCancellation = null;
            source.Dispose(); if (!closed) Refresh();
        }
    }

    private void CancelImport()
    {
        importGeneration++;
        var old = importCancellation; importCancellation = null;
        old?.Cancel();
    }

    private void ShowImportError(Exception exception)
    {
        if (closed) return;
        string message = exception.Message;
        if (message.Length > 180) message = message[..180];
        controller.SetLocalStatus("图片未添加：" + message);
    }

    private void RenderDraft()
    {
        attachmentPreviews.Children.Clear();
        foreach (var image in pendingImages)
        {
            var tile = new StackPanel { Width = 140, Margin = new Thickness(0, 8, 10, 2) };
            tile.Children.Add(Thumbnail(image, 140, 88));
            tile.Children.Add(new TextBlock { Text = image.Name, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, ToolTip = image.Name });
            tile.Children.Add(new TextBlock { Text = $"{image.Width}×{image.Height} · {image.ByteLength / 1024.0:0.#} KB", FontSize = 11 });
            var remove = Button("移除", () =>
            {
                int index = pendingImages.FindIndex(item => ReferenceEquals(item, image));
                if (index >= 0) { pendingImages.RemoveAt(index); draftRevision++; RenderDraft(); Refresh(); }
            });
            remove.Tag = image; remove.Margin = new Thickness(0, 3, 0, 0); remove.Padding = new Thickness(6, 2, 6, 2);
            AutomationProperties.SetName(remove, "移除图片 " + image.Name);
            tile.Children.Add(remove); attachmentPreviews.Children.Add(tile);
        }
        attachmentArea.Visibility = pendingImages.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    internal async Task SendDraftAsync()
    {
        if (closed) return;
        if (IsImporting) { controller.SetLocalStatus("图片尚在准备中，请稍后发送。"); return; }
        string text = input.Text.Trim();
        if (text.Length == 0 && pendingImages.Count == 0) return;
        if (controller.IsBusy && sendingDraftRevision == draftRevision) return;
        var images = Array.AsReadOnly(pendingImages.ToArray());
        long capturedTextRevision = textRevision, current = ++sendGeneration;
        sendingDraftRevision = draftRevision;
        try
        {
            var reply = await controller.SendAsync(text, images);
            if (closed || reply == null || current != sendGeneration) return;
            if (capturedTextRevision == textRevision) input.Clear();
            foreach (var image in images)
            {
                int index = pendingImages.FindIndex(item => ReferenceEquals(item, image));
                if (index >= 0) { pendingImages.RemoveAt(index); draftRevision++; }
            }
            RenderDraft();
        }
        finally
        {
            if (current == sendGeneration) sendingDraftRevision = null;
            if (!closed) Refresh();
        }
    }

    private void Refresh()
    {
        if (closed) return;
        status.Text = controller.Status;
        string provider;
        try { provider = controller.Options.Endpoint().Host; } catch (ArgumentException) { provider = "当前模型服务"; }
        uploadNotice.Text = $"图片将在发送后上传至 {provider}，只在本次会话中保留。";
        if (sendButton == null) return;
        sendButton.IsEnabled = !IsImporting && (input.Text.Trim().Length != 0 || pendingImages.Count != 0) &&
            !(controller.IsBusy && sendingDraftRevision == draftRevision);
        addImageButton.IsEnabled = !IsImporting && pendingImages.Count < CompanionController.MaxImagesPerMessage;
        cancelImportButton.Visibility = IsImporting ? Visibility.Visible : Visibility.Collapsed;
    }

    private Button Thumbnail(ChatImage image, double width, double height)
    {
        var button = new Button { Content = new Image { Source = image.Preview, Width = width, Height = height, Stretch = Stretch.Uniform },
            Padding = new Thickness(2), Margin = new Thickness(0, 0, 8, 6), ToolTip = "查看图片：" + image.Name };
        button.Click += (_, _) =>
        {
            imageViewer?.Close();
            imageViewer = new ImagePreviewWindow(image) { Owner = this };
            imageViewer.Closed += (_, _) => imageViewer = null;
            imageViewer.Show();
        };
        return button;
    }

    private void Append(CompanionChatMessage message)
    {
        if (closed) return;
        long bytes = message.Images.Sum(image => (long)image.ByteLength);
        long pixels = message.Images.Sum(image => (long)image.Width * image.Height);
        bool releasedPictures = false;
        while (messages.Children.Count > 0 && (messages.Children.Count >= 100 ||
            DisplayedImageBytes + bytes > CompanionController.MaxHistoryImageBytes || DisplayedImagePixels + pixels > DisplayPixelLimit))
        {
            var first = messages.Children[0];
            if (displayedPictures.Remove(first, out var weight))
            {
                DisplayedImageBytes -= weight.Bytes; DisplayedImagePixels -= weight.Pixels;
                if (weight.Bytes != 0) { releasedPictures = true; imageViewer?.Close(); }
            }
            messages.Children.RemoveAt(0);
        }
        if (releasedPictures) viewNotice.Text = "较旧图片已从本地视图释放。";
        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock { Text = message.Name, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("NanallyAccent"), Margin = new Thickness(0, 0, 0, 6) });
        if (message.Images.Count != 0)
        {
            var pictures = new WrapPanel();
            foreach (var image in message.Images) pictures.Children.Add(Thumbnail(image, 144, 110));
            textPanel.Children.Add(pictures);
        }
        if (!string.IsNullOrEmpty(message.Text))
            textPanel.Children.Add(new TextBox { Text = message.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), FontSize = 15 });
        var card = new Border { Child = textPanel, Padding = new Thickness(16), Margin = new Thickness(0, 0, 12, 14),
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("NanallyLine"),
            Background = message.Name == "你" ? new SolidColorBrush(Color.FromRgb(250, 237, 243)) : Brushes.White };
        displayedPictures.Add(card, (bytes, pixels)); DisplayedImageBytes += bytes; DisplayedImagePixels += pixels;
        messages.Children.Add(card); conversation.ScrollToEnd();
    }

    private void HistoryCleared()
    {
        if (closed) return;
        sendGeneration++; sendingDraftRevision = null;
        CancelImport(); imageViewer?.Close();
        pendingImages.Clear(); attachmentPreviews.Children.Clear(); attachmentArea.Visibility = Visibility.Collapsed;
        messages.Children.Clear(); displayedPictures.Clear(); DisplayedImageBytes = 0; DisplayedImagePixels = 0;
        viewNotice.Text = ""; draftRevision++; Refresh();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true; sendGeneration++; CancelImport(); lifetime.Cancel(); imageViewer?.Close();
        DataObject.RemovePastingHandler(input, OnPasting);
        controller.Changed -= Refresh; controller.RichMessage -= Append; controller.HistoryCleared -= HistoryCleared;
        pendingImages.Clear(); attachmentPreviews.Children.Clear(); messages.Children.Clear(); displayedPictures.Clear();
        DisplayedImageBytes = 0; DisplayedImagePixels = 0; lifetime.Dispose();
    }

    private sealed class ImagePreviewWindow : Window
    {
        public ImagePreviewWindow(ChatImage image)
        {
            Title = "娜娜莉 · " + image.Name; Width = 900; Height = 720; MinWidth = 320; MinHeight = 280;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var picture = new Image { Source = image.Preview, Stretch = Stretch.Uniform, Margin = new Thickness(18), ToolTip = image.Name };
            var loading = new TextBlock { Text = "正在打开图片…", HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
            var content = new Grid(); content.Children.Add(picture); content.Children.Add(loading); Content = content;
            var cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            Loaded += async (_, _) =>
            {
                try
                {
                    var full = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = new MemoryStream(Convert.FromBase64String(image.DataUrl[(image.DataUrl.IndexOf(',') + 1)..]));
                        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); token.ThrowIfCancellationRequested();
                        return bitmap;
                    }, token);
                    if (!token.IsCancellationRequested) { picture.Source = full; loading.Text = ""; }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { if (!token.IsCancellationRequested) loading.Text = "完整图片无法打开，当前显示缩略图。"; }
            };
            Closed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); picture.Source = null; };
        }
    }
}
