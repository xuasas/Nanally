using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.Companion;

/// <summary>Transport adapter: no retries, tools or automatic background requests.</summary>
internal sealed class ChatCompletionClient : IDisposable
{
    private readonly HttpClient http;
    public ChatCompletionClient(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<CompanionReply> CompleteAsync(CompanionOptions options, string key,
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellation)
    {
        options.Validate();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint());
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var bodyValues = new Dictionary<string, object>
        {
            ["model"] = options.Model, ["messages"] = messages, ["stream"] = false, ["max_tokens"] = options.MaxOutputTokens
        };
        // This provider/model defaults to retained reasoning. This desktop text mode deliberately opts out.
        if (options.Model.Equals("qwen3.8-flash", StringComparison.OrdinalIgnoreCase) &&
            options.Endpoint().Host.EndsWith(".aliyuncs.com", StringComparison.OrdinalIgnoreCase))
        {
            bodyValues["enable_thinking"] = false;
            bodyValues["preserve_thinking"] = false;
        }
        try
        {
            // Base64 histories can be tens of MiB. Keep serialization and UTF-8 encoding
            // off the WPF dispatcher; use the same cancellation generation as transport.
            request.Content = await Task.Run(() =>
            {
                timeout.Token.ThrowIfCancellationRequested();
                string json = JsonSerializer.Serialize(bodyValues);
                timeout.Token.ThrowIfCancellationRequested();
                return new StringContent(json, Encoding.UTF8, "application/json");
            }, timeout.Token).ConfigureAwait(false);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"模型服务返回 HTTP {(int)response.StatusCode}。请检查模型名称、密钥权限和服务地址。", null, response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] block = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(block, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) throw new FormatException("模型回复过大，已停止接收。");
                buffer.Write(block, 0, count);
            }
            using var body = JsonDocument.Parse(buffer.ToArray());
            if (!body.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
                !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
                throw new FormatException("服务没有返回文字回复；本版不执行工具调用。");
            return ReplyProtocol.Parse(content.GetString()!);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("模型回复超时，请稍后重试。桌宠仍可正常互动。"); }
        catch (JsonException) { throw new FormatException("服务响应格式异常，请检查接口兼容性。"); }
    }

    public void Dispose() => http.Dispose();
}
