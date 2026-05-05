namespace GoldfishReminder.Application.Services;

//Discord 設定提供介面
public interface IDiscordSettingsProvider
{
    string GetGuildId();
    string GetCategoryName();
}