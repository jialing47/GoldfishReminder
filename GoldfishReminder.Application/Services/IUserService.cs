using GoldfishReminder.Application.Models;
using GoldfishReminder.Domain.Entities;

namespace GoldfishReminder.Application.Services;

//使用者資料服務介面
public interface IUserService
{
    Task<User> UpsertAsync(UpsertUserRequest request, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByDiscordIdAsync(string discordUserId, CancellationToken cancellationToken = default);
}