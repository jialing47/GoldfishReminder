namespace GoldfishReminder.Application.Models;

//Discord 設定連結請求
public class DiscordSettingsLinkRequest
{
    public string ApplicationId { get; set; } = string.Empty;
    public string InteractionToken { get; set; } = string.Empty;
    public string DiscordUserId { get; set; } = string.Empty;
    public string? DiscordDisplayName { get; set; }
}