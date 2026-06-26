using System.Text.Json;

namespace GoldfishReminder.Application.Services;

//Discord API 用戶端介面
public interface IDiscordApiClient
{
    Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken = default);
    Task<JsonElement> PostJsonAsync(string url, object payload, CancellationToken cancellationToken = default);
    Task SendFollowupAsync(string applicationId, string interactionToken, string content, bool isEphemeral, CancellationToken cancellationToken = default);
    Task SendFollowupPayloadAsync(string applicationId, string interactionToken, object payload, CancellationToken cancellationToken = default);
    Task SendChannelMessageAsync(string channelId, object payload, CancellationToken cancellationToken = default);
}