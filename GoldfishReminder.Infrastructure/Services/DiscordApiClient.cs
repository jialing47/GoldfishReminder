using GoldfishReminder.Application.Services;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GoldfishReminder.Infrastructure.Services;

//Discord API 用戶端實作
public class DiscordApiClient : IDiscordApiClient
{
    private const int MaxErrorBodyLength = 200; // Discord 失敗回應 body 截斷上限 避免敏感或過長內容進入 log
    private const int EphemeralFlag = 64;       // Discord ephemeral 訊息 flag

    private readonly HttpClient httpClient;
    private readonly DiscordOptions discordOptions;

    public DiscordApiClient(HttpClient httpClient, IOptions<DiscordOptions> discordOptions)
    {
        this.httpClient = httpClient;
        this.discordOptions = discordOptions.Value;
    }

    //GET 取得 JSON
    public async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.BotToken);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Discord GET failed. Status:{(int)response.StatusCode} Body:{TruncateErrorBody(text)}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    //POST JSON
    public async Task<JsonElement> PostJsonAsync(string url, object payload, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.BotToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Discord POST failed. Status:{(int)response.StatusCode} Body:{TruncateErrorBody(text)}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    //送出 followup 訊息
    public async Task SendFollowupAsync(string applicationId, string interactionToken, string content, bool isEphemeral, CancellationToken cancellationToken = default)
    {
        var url = BuildFollowupUrl(applicationId, interactionToken);

        var flags = 0;
        if (isEphemeral)
        {
            flags = EphemeralFlag;
        }

        var payload = new
        {
            content,
            flags
        };

        await PostJsonAsync(url, payload, cancellationToken);
    }

    //送出帶自訂 payload 的 followup 訊息 ephemeral 與元件由呼叫端自行在 payload 內帶上
    public async Task SendFollowupPayloadAsync(string applicationId, string interactionToken, object payload, CancellationToken cancellationToken = default)
    {
        var url = BuildFollowupUrl(applicationId, interactionToken);
        await PostJsonAsync(url, payload, cancellationToken);
    }

    //組出 followup webhook URL 兩個 followup 方法共用避免重複硬寫
    private static string BuildFollowupUrl(string applicationId, string interactionToken)
    {
        return $"https://discord.com/api/v10/webhooks/{applicationId}/{interactionToken}";
    }

    //送出頻道訊息
    public async Task SendChannelMessageAsync(string channelId, object payload, CancellationToken cancellationToken = default)
    {
        var url = $"https://discord.com/api/v10/channels/{channelId}/messages";
        await PostJsonAsync(url, payload, cancellationToken);
    }

    //截斷 Discord 錯誤回應 body 避免長 body 或敏感內容直接寫入 log
    private static string TruncateErrorBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return string.Empty;
        }

        if (body.Length <= MaxErrorBodyLength)
        {
            return body;
        }

        return body.Substring(0, MaxErrorBodyLength) + "...(truncated)";
    }
}