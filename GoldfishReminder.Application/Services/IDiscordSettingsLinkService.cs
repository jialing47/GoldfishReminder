using GoldfishReminder.Application.Models;

namespace GoldfishReminder.Application.Services;

//Discord 設定連結服務介面
public interface IDiscordSettingsLinkService
{
    Task SendSettingsLinkAsync(DiscordSettingsLinkRequest request, CancellationToken cancellationToken = default);
}