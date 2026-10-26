using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VPet_Simulator.Windows.Companion;

/// <summary>Immutable chat snapshot. Text stays wire-compatible; user attachments become multimodal content parts.</summary>
internal sealed class ChatMessage
{
    public string role { get; }
    public object content
    {
        get
        {
            if (Images.Count == 0) return Text;
            var parts = new List<object>(Images.Count + 1);
            if (!string.IsNullOrWhiteSpace(Text)) parts.Add(new { type = "text", text = Text });
            foreach (var image in Images)
                parts.Add(new { type = "image_url", image_url = new { url = image.DataUrl } });
            return parts;
        }
    }

    [JsonIgnore] public string Text { get; }
    [JsonIgnore] public IReadOnlyList<ChatImage> Images { get; }
    [JsonIgnore] public long AttachmentBytes { get; }

    public ChatMessage(string role, string content, IReadOnlyList<ChatImage>? images = null)
    {
        this.role = role ?? throw new ArgumentNullException(nameof(role));
        Text = content ?? "";
        var copy = images?.ToArray() ?? Array.Empty<ChatImage>();
        if (copy.Any(image => image == null)) throw new ArgumentException("图片附件不能为空。", nameof(images));
        if (copy.Length > 0 && role != "user") throw new ArgumentException("只有用户消息可以包含图片。", nameof(role));
        Images = Array.AsReadOnly(copy);
        AttachmentBytes = copy.Sum(image => (long)image.ByteLength);
    }

    public static ChatMessage User(string text, IReadOnlyList<ChatImage> images) => new("user", text, images);
}
