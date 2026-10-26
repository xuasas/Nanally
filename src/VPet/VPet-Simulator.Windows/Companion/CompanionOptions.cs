using System;

namespace VPet_Simulator.Windows.Companion;

internal sealed record CompanionOptions
{
    public string BaseUrl { get; init; } = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    public string Model { get; init; } = "qwen3.8-flash";
    public string Persona { get; init; } = "你是娜娜莉，非官方同人桌面伙伴。活泼亲切，认真回答，不编造官方设定。";
    public int TimeoutSeconds { get; init; } = 60;
    public int HistoryTurns { get; init; } = 6;
    public int MaxOutputTokens { get; init; } = 1024;

    public Uri Endpoint()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || !Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("服务地址须为 HTTPS（本机服务可用 HTTP），不能包含密钥、查询参数或片段。");
        string address = uri.AbsoluteUri.TrimEnd('/');
        return new Uri(address.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? address : address + "/chat/completions");
    }

    public void Validate()
    {
        _ = Endpoint();
        if (string.IsNullOrWhiteSpace(Model) || Model.Length > 200 || string.IsNullOrWhiteSpace(Persona) || Persona.Length > 12000)
            throw new ArgumentException("请填写模型名称和人设；人设最多 12000 字符。");
        if (TimeoutSeconds is < 1 or > 180 || HistoryTurns is < 0 or > 12 || MaxOutputTokens is < 64 or > 4096)
            throw new ArgumentException("超时或上下文长度超出允许范围。");
    }
}
