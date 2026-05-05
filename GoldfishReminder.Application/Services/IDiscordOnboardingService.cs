using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

//Discord onboarding 服務介面
public interface IDiscordOnboardingService
{
    Task<User> EnsureUserChannelAsync(string discordUserId, string? discordDisplayName, CancellationToken cancellationToken = default);
}