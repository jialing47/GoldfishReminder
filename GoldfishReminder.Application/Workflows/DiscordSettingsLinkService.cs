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

        var user = await onboardingService.EnsureUserChannelAsync(request.DiscordUserId, request.DiscordDisplayName, forceRefresh: false, cancellationToken);
        var tokenResult = await webLinkTokenService.CreateOrRotateAsync(user.Id, TimeSpan.FromMinutes(30), cancellationToken);
        var settingsUrl = $"{baseUrl.TrimEnd('/')}/?token={Uri.EscapeDataString(tokenResult.Token)}";
        var payload = BuildSettingsLinkPayload(settingsUrl);

        // 信任 DB 紀錄不再驗 channel 存在性 換取送連結延遲降低
        // trade-off 是若頻道被人為刪除 第一次送會 404 此時 fallback 強制重建後重送一次
        try
        {
            await discordApiClient.SendChannelMessageAsync(user.DiscordPrivateChannelId!, payload, cancellationToken);
        }
        catch (InvalidOperationException ex) when (IsChannelMissingError(ex))
        {
            // 強制重列 Discord channels 重建私人頻道後再送
            user = await onboardingService.EnsureUserChannelAsync(request.DiscordUserId, request.DiscordDisplayName, forceRefresh: true, cancellationToken);
            await discordApiClient.SendChannelMessageAsync(user.DiscordPrivateChannelId!, payload, cancellationToken);
        }

        var followupMessage = $"已發送網頁連結到你的私人提醒頻道：<#{user.DiscordPrivateChannelId}>。";

        await discordApiClient.SendFollowupAsync(request.ApplicationId, request.InteractionToken, followupMessage, true, cancellationToken);
    }

    // 建立送網頁連結的 Discord 訊息 payload 抽出共用避免 fallback 重送時邏輯分歧
    private static object BuildSettingsLinkPayload(string settingsUrl)
    {
        return new
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
        };
    }

    // 判斷 exception 是否為頻道不存在的錯誤
    // DiscordApiClient 失敗訊息格式為 "Discord POST failed. Status:{code} Body:{...}" 由 Status:404 判定
    // 404 對 POST channel/{id}/messages 幾乎只發生在 channel 被刪 或 bot 看不到該頻道 兩種都應走 fallback 重建
    private static bool IsChannelMissingError(InvalidOperationException ex)
    {
        return ex.Message.Contains("Status:404", StringComparison.Ordinal);
    }
}