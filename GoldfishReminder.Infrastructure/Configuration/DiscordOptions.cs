namespace GoldfishReminder.Infrastructure.Configuration;

//Discord 設定
public class DiscordOptions
{
    public string PublicKey { get; set; } = string.Empty;
    public string BotToken { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ApplicationId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = "GoldfishReminder";
}