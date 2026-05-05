using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using System.Text.Json;

namespace GoldfishReminder.Application.Workflows;

//Discord onboarding 服務
public class DiscordOnboardingService : IDiscordOnboardingService
{
    private static readonly SemaphoreSlim botUserIdLock = new(1, 1);
    private static string? cachedBotUserId;
    private static DateTimeOffset cachedBotUserIdExpiresAt;

    private readonly IUserService userService;
    private readonly IDiscordApiClient discordApiClient;
    private readonly IDiscordSettingsProvider discordSettingsProvider;

    public DiscordOnboardingService(IUserService userService, IDiscordApiClient discordApiClient, IDiscordSettingsProvider discordSettingsProvider)
    {
        this.userService = userService;
        this.discordApiClient = discordApiClient;
        this.discordSettingsProvider = discordSettingsProvider;
    }

    //確保使用者與私人提醒頻道存在
    public async Task<User> EnsureUserChannelAsync(string discordUserId, string? discordDisplayName, CancellationToken cancellationToken = default)
    {
        var guildId = discordSettingsProvider.GetGuildId();
        var categoryName = discordSettingsProvider.GetCategoryName();

        if (string.IsNullOrWhiteSpace(guildId))
        {
            throw new InvalidOperationException("Discord:GuildId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            throw new ArgumentException("discordUserId is required.", nameof(discordUserId));
        }

        var normalizedDiscordUserId = discordUserId.Trim();
        var user = await GetOrCreateUserAsync(normalizedDiscordUserId, discordDisplayName, cancellationToken);
        var channels = await discordApiClient.GetJsonAsync($"https://discord.com/api/v10/guilds/{guildId}/channels", cancellationToken);

        if (!string.IsNullOrWhiteSpace(user.DiscordPrivateChannelId) && ChannelExists(channels, user.DiscordPrivateChannelId))
        {
            return user;
        }

        var categoryId = FindCategoryId(channels, categoryName);

        if (string.IsNullOrWhiteSpace(categoryId))
        {
            categoryId = await CreateCategoryAsync(guildId, categoryName, cancellationToken);
        }

        var channelName = $"gr-{normalizedDiscordUserId}";
        var privateChannelId = FindChannelId(channels, categoryId, channelName);
        var createdNewChannel = false;

        if (string.IsNullOrWhiteSpace(privateChannelId))
        {
            privateChannelId = await CreatePrivateUserChannelAsync(guildId, categoryId, normalizedDiscordUserId, cancellationToken);
            createdNewChannel = true;
        }

        user = await userService.UpsertAsync(
            new UpsertUserRequest
            {
                Id = user.Id,
                Name = user.Name,
                DiscordUserId = user.DiscordUserId,
                DiscordPrivateChannelId = privateChannelId
            },
            cancellationToken);

        if (createdNewChannel)
        {
            await SendWelcomeMessageAsync(privateChannelId, cancellationToken);
        }

        return user;
    }

    //取得或建立 user
    private async Task<User> GetOrCreateUserAsync(string discordUserId, string? discordDisplayName, CancellationToken cancellationToken)
    {
        var user = await userService.GetByDiscordIdAsync(discordUserId, cancellationToken);

        if (user == null)
        {
            return await userService.UpsertAsync(
                new UpsertUserRequest
                {
                    Name = string.IsNullOrWhiteSpace(discordDisplayName) ? "DiscordUser" : discordDisplayName.Trim(),
                    DiscordUserId = discordUserId
                },
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(user.Name) && !string.IsNullOrWhiteSpace(discordDisplayName))
        {
            user = await userService.UpsertAsync(
                new UpsertUserRequest
                {
                    Id = user.Id,
                    Name = discordDisplayName.Trim(),
                    DiscordUserId = user.DiscordUserId,
                    DiscordPrivateChannelId = user.DiscordPrivateChannelId
                },
                cancellationToken);
        }

        return user;
    }

    //判斷頻道是否存在於目前 guild channels 快照中
    private static bool ChannelExists(JsonElement channels, string channelId)
    {
        foreach (var channel in channels.EnumerateArray())
        {
            if (!channel.TryGetProperty("id", out var idElement))
            {
                continue;
            }

            if (string.Equals(idElement.GetString(), channelId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    //由目前 guild channels 快照找 category id
    private static string? FindCategoryId(JsonElement channels, string categoryName)
    {
        foreach (var channel in channels.EnumerateArray())
        {
            if (!channel.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (typeElement.GetInt32() != 4)
            {
                continue;
            }

            var name = channel.GetProperty("name").GetString();

            if (string.Equals(name, categoryName, StringComparison.OrdinalIgnoreCase))
            {
                return channel.GetProperty("id").GetString();
            }
        }

        return null;
    }

    //由目前 guild channels 快照找私人頻道 id
    private static string? FindChannelId(JsonElement channels, string categoryId, string channelName)
    {
        foreach (var channel in channels.EnumerateArray())
        {
            var name = channel.GetProperty("name").GetString();

            if (!string.Equals(name, channelName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!channel.TryGetProperty("parent_id", out var parentIdElement) || parentIdElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (string.Equals(parentIdElement.GetString(), categoryId, StringComparison.Ordinal))
            {
                return channel.GetProperty("id").GetString();
            }
        }

        return null;
    }

    //建立分類頻道
    private async Task<string> CreateCategoryAsync(string guildId, string categoryName, CancellationToken cancellationToken)
    {
        var created = await discordApiClient.PostJsonAsync(
            $"https://discord.com/api/v10/guilds/{guildId}/channels",
            new
            {
                name = categoryName,
                type = 4
            },
            cancellationToken);

        return created.GetProperty("id").GetString()!;
    }

    //送出歡迎訊息
    private async Task SendWelcomeMessageAsync(string channelId, CancellationToken cancellationToken)
    {
        var content =
            "已建立你的私人提醒頻道。\n" +
            "之後系統會在這裡發送提醒與網頁連結。";

        await discordApiClient.SendChannelMessageAsync(channelId, new { content }, cancellationToken);
    }

    //建立私人提醒頻道
    private async Task<string> CreatePrivateUserChannelAsync(string guildId, string categoryId, string discordUserId, CancellationToken cancellationToken)
    {
        var channelName = $"gr-{discordUserId}";
        var botUserId = await GetBotUserIdAsync(cancellationToken);

        const string denyEveryone = "1024";
        const string allowUser = "68608";
        const string allowBot = "68624";

        var payload = new
        {
            name = channelName,
            type = 0,
            parent_id = categoryId,
            permission_overwrites = new object[]
            {
                new { id = guildId, type = 0, allow = "0", deny = denyEveryone },
                new { id = discordUserId, type = 1, allow = allowUser, deny = "0" },
                new { id = botUserId, type = 1, allow = allowBot, deny = "0" }
            }
        };

        var created = await discordApiClient.PostJsonAsync($"https://discord.com/api/v10/guilds/{guildId}/channels", payload, cancellationToken);
        return created.GetProperty("id").GetString()!;
    }

    //取得 bot user id，做短期快取
    private async Task<string> GetBotUserIdAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(cachedBotUserId) && cachedBotUserIdExpiresAt > now)
        {
            return cachedBotUserId;
        }

        await botUserIdLock.WaitAsync(cancellationToken);

        try
        {
            now = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(cachedBotUserId) && cachedBotUserIdExpiresAt > now)
            {
                return cachedBotUserId;
            }

            var botMe = await discordApiClient.GetJsonAsync("https://discord.com/api/v10/users/@me", cancellationToken);
            cachedBotUserId = botMe.GetProperty("id").GetString()!;
            cachedBotUserIdExpiresAt = now.AddMinutes(30);

            return cachedBotUserId;
        }
        finally
        {
            botUserIdLock.Release();
        }
    }
}