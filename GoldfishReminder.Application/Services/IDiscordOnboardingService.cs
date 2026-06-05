using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

//Discord onboarding 服務介面
public interface IDiscordOnboardingService
{
    // forceRefresh 為 true 時跳過 DB 已記錄的 channel 信任 強制去 Discord 列頻道並重建 用於頻道被人為刪除後的補救
    Task<User> EnsureUserChannelAsync(string discordUserId, string? discordDisplayName, bool forceRefresh, CancellationToken cancellationToken = default);
}