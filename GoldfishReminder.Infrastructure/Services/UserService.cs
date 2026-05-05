using GoldfishReminder.Application.Models;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

//使用者資料服務
public class UserService : IUserService
{
    private readonly AppDbContext dbContext;

    public UserService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    //新增或更新使用者
    public async Task<User> UpsertAsync(UpsertUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name is required");
        }

        var name = request.Name.Trim();
        string? discordUserId = null;
        string? discordPrivateChannelId = null;

        if (!string.IsNullOrWhiteSpace(request.DiscordUserId))
        {
            discordUserId = request.DiscordUserId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.DiscordPrivateChannelId))
        {
            discordPrivateChannelId = request.DiscordPrivateChannelId.Trim();
        }

        User user;

        if (request.Id.HasValue)
        {
            var existingUser = await dbContext.Users.FirstOrDefaultAsync(x => x.Id == request.Id.Value, cancellationToken);

            if (existingUser == null)
            {
                throw new KeyNotFoundException($"User not found Id:{request.Id.Value}");
            }

            user = existingUser;
        }
        else
        {
            user = new User
            {
                Id = Guid.NewGuid()
            };

            dbContext.Users.Add(user);
        }

        if (!string.IsNullOrWhiteSpace(discordUserId))
        {
            var exists = await dbContext.Users
                .AsNoTracking()
                .AnyAsync(x => x.DiscordUserId == discordUserId && x.Id != user.Id, cancellationToken);

            if (exists)
            {
                throw new InvalidOperationException("DiscordUserId already exists");
            }
        }

        user.Name = name;
        user.DiscordUserId = discordUserId;
        user.DiscordPrivateChannelId = discordPrivateChannelId;

        await dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }

    //依 Id 查詢使用者
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    //依 DiscordUserId 查詢使用者
    public async Task<User?> GetByDiscordIdAsync(string discordUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(discordUserId))
        {
            throw new ArgumentException("DiscordUserId is required");
        }

        var normalizedDiscordUserId = discordUserId.Trim();

        return await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DiscordUserId == normalizedDiscordUserId, cancellationToken);
    }
}