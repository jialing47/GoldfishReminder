﻿using GoldfishReminder.Application.Services;

namespace GoldfishReminder.Infrastructure.Services;

//Discord 通知發送實作
public class NotificationSender : INotificationSender
{
    private readonly IDiscordApiClient discordApiClient;

    public NotificationSender(IDiscordApiClient discordApiClient)
    {
        this.discordApiClient = discordApiClient;
    }

    //發送 Discord 頻道訊息
    public async Task SendAsync(string recipientChannelId, string messageContent, object[]? components = null, CancellationToken cancellationToken = default)
    {
        if (components == null || components.Length == 0)
        {
            await discordApiClient.SendChannelMessageAsync(
                recipientChannelId,
                new
                {
                    content = messageContent
                },
                cancellationToken);

            return;
        }

        await discordApiClient.SendChannelMessageAsync(
            recipientChannelId,
            new
            {
                content = messageContent,
                components
            },
            cancellationToken);
    }
}
