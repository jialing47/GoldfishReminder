using GoldfishReminder.Application.Services;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GoldfishReminder.Infrastructure.Services;

//Discord 設定提供實作
public class DiscordSettingsProvider : IDiscordSettingsProvider
{
    private readonly DiscordOptions discordOptions;

    public DiscordSettingsProvider(IOptions<DiscordOptions> discordOptions)
    {
        this.discordOptions = discordOptions.Value;
    }

    //取得 GuildId
    public string GetGuildId()
    {
        return discordOptions.GuildId;
    }

    //取得分類名稱
    public string GetCategoryName()
    {
        return discordOptions.CategoryName;
    }
}