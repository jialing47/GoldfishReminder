using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;

namespace GoldfishReminder.Application.Workflows;

//Discord 設定連結服務
public class DiscordSettingsLinkService : IDiscordSettingsLinkService
{
    private readonly IDiscordOnboardingService onboardingService;
    private readonly IWebLinkTokenService webLinkTokenService;
    private readonly IDiscordApiClient discordApiClient;
    private readonly IWebUrlProvider webUrlProvider;

    public DiscordSettingsLinkService(IDiscordOnboardingService onboardingService, IWebLinkTokenService webLinkTokenService, IDiscordApiClient discordApiClient, IWebUrlProvider webUrlProvider)
    {
        this.onboardingService = onboardingService;
        this.webLinkTokenService = webLinkTokenService;
        this.discordApiClient = discordApiClient;
        this.webUrlProvider = webUrlProvider;
    }

    //送出設定連結
    public async Task SendSettingsLinkAsync(DiscordSettingsLinkRequest request, CancellationToken cancellationToken = default)
    {
        var baseUrl = webUrlProvider.GetBaseUrl();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            await discordApiClient.SendFollowupAsync(request.ApplicationId, request.InteractionToken, "Web:BaseUrl 未設定。", true, cancellationToken);
            return;
        }

        var user = await onboardingService.EnsureUserChannelAsync(request.DiscordUserId, request.DiscordDisplayName, cancellationToken);
        var tokenResult = await webLinkTokenService.CreateOrRotateAsync(user.Id, TimeSpan.FromMinutes(30), cancellationToken);
        var settingsUrl = $"{baseUrl.TrimEnd('/')}/?token={Uri.EscapeDataString(tokenResult.Token)}";

        await discordApiClient.SendChannelMessageAsync(
            user.DiscordPrivateChannelId!,
            new
            {
                content = "此連結為一次性，再次取得連結請點擊公告區按鈕\n網頁連結：",
                components = new object[]
                {
                    new
                    {
                        type = 1,
                        components = new object[]
                        {
                            new { type = 2, style = 5, label = "開啟網頁", url = settingsUrl }
                        }
                    }
                }
            },
            cancellationToken);

        var followupMessage = request.Reason == SettingsLinkReason.Start
            ? $"已建立/確認你的私人提醒頻道：<#{user.DiscordPrivateChannelId}>，並已發送網頁連結。"
            : $"已發送網頁連結到你的私人提醒頻道：<#{user.DiscordPrivateChannelId}>。";

        await discordApiClient.SendFollowupAsync(request.ApplicationId, request.InteractionToken, followupMessage, true, cancellationToken);
    }
}