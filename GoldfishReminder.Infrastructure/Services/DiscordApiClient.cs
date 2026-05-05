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
            throw new InvalidOperationException($"Discord GET failed. Status:{(int)response.StatusCode} Body:{text}");
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
            throw new InvalidOperationException($"Discord POST failed. Status:{(int)response.StatusCode} Body:{text}");
        }

        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    //送出 followup 訊息
    public async Task SendFollowupAsync(string applicationId, string interactionToken, string content, bool isEphemeral, CancellationToken cancellationToken = default)
    {
        var url = $"https://discord.com/api/v10/webhooks/{applicationId}/{interactionToken}";
        var payload = new
        {
            content,
            flags = isEphemeral ? 64 : 0
        };

        await PostJsonAsync(url, payload, cancellationToken);
    }

    //送出頻道訊息
    public async Task SendChannelMessageAsync(string channelId, object payload, CancellationToken cancellationToken = default)
    {
        var url = $"https://discord.com/api/v10/channels/{channelId}/messages";
        await PostJsonAsync(url, payload, cancellationToken);
    }
}