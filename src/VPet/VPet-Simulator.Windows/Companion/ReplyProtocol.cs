using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VPet_Simulator.Windows.Companion;

internal enum CompanionEmotion { normal, happy, angry, surprised, sad, sleepy }
internal sealed record CompanionReply(string Text, CompanionEmotion Emotion)
{
    // Diagnostics distinguish the model's declared label from the safe parsed fallback.
    public string? ModelEmotion { get; init; }
}

internal static class ReplyProtocol
{
    public const string Instruction = """
        仅输出一个JSON对象，包含text（给用户的自然语言回复）和emotion（normal、happy、angry、surprised、sad、sleepy之一）。
        emotion描述娜娜莉自己在本次回应中的情绪，不是用户情绪分类。结合当前聊天内容、前文、双方关系和人设自然选择，让正文语气与标签一致；不需要用户明确下达情绪表演指令。
        normal是默认：普通问答、平静闲聊、认真解释和温柔平静的陪伴可用normal。没有明确情绪理由时保持normal。
        判断依据是对话中已有的具体事件。亲切、可爱、愿意陪伴、句末带“～”本身不等于happy；普通生活话题没有明确触发事件时使用normal。不要自行编造愉快场景、收到礼物或别的遭遇来制造情绪。
        happy：她自己开心、被逗乐、受到认可、收到喜欢的东西或为成功高兴。
        angry：她受到友好捉弄、被冒犯或小期待落空而轻微嗔怒、傲娇；不辱骂、威胁或无故发火。
        surprised：她遇到明显出乎意料的事情，自然惊讶，而不是每条新消息都大惊小怪。
        sad：她自己失落、遗憾、舍不得或真挚共情；不要看到用户难过就机械复制sad，温柔安慰可以保持normal。
        sleepy：上下文显示她自己疲倦、打哈欠、想休息或困倦地道晚安；用户困了不等于她必然困倦，也不改变桌宠实际睡眠状态。
        普通情绪保持默认，但合适语境下不要压抑自然的开心、轻嗔、惊讶、低落与困倦。不要随机选择、轮流切换、平均分配或为了展示标签而夸张表演，不要求每句话都有强烈情绪。
        正文自然简洁，避免每次重复重力设定或大段舞台动作；不宣称看过用户文件或已经进行现实操作，轻嗔也不承诺惩罚用户。
        回复只用于文字气泡与表情/嘴型，不生成语音或声线指令。不要输出工具指令、代码执行请求、资源路径、解释选择过程或额外格式；text不包含情绪标签。
        """;
    public static CompanionReply Parse(string content)
    {
        string value = content.Trim();
        if (value.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = value.IndexOf('\n');
            if (firstLine < 0 || !value.EndsWith("```", StringComparison.Ordinal)) throw new FormatException("回复格式不完整，请重试。");
            value = value[(firstLine + 1)..^3].Trim();
        }
        value = Regex.Replace(value, @"^\s*\[(?:emotion|情绪)\s*[:：][^\]]*\]\s*", "", RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
        if (value.StartsWith('{') || value.StartsWith('['))
        {
            try
            {
                using var json = JsonDocument.Parse(value);
                if (json.RootElement.ValueKind != JsonValueKind.Object ||
                    !json.RootElement.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                    throw new FormatException("回复缺少可显示的文字，请重试。");
                string? declared = json.RootElement.TryGetProperty("emotion", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                string emotion = declared ?? "normal";
                CompanionEmotion mode = Array.IndexOf(Enum.GetNames<CompanionEmotion>(), emotion.ToLowerInvariant()) >= 0
                    ? Enum.Parse<CompanionEmotion>(emotion, true) : CompanionEmotion.normal;
                return Create(text.GetString()!, mode) with { ModelEmotion = declared };
            }
            catch (JsonException)
            {
                var match = Regex.Match(value, "\"text\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")", RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
                if (match.Success)
                {
                    try { return Create(JsonSerializer.Deserialize<string>(match.Groups[1].Value)!, CompanionEmotion.normal); }
                    catch (JsonException) { }
                }
                throw new FormatException("回复格式损坏，未播放原始内容，请重试。");
            }
        }
        value = Regex.Replace(value, @"^\s*\[(?:emotion|情绪)\s*[:：][^\]]*\]\s*", "", RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
        return Create(value, CompanionEmotion.normal);
    }

    private static CompanionReply Create(string text, CompanionEmotion emotion)
    {
        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("模型返回了空回复，请重试。");
        if (text.Length > 6000) text = text[..6000];
        return new(text, emotion);
    }
}
