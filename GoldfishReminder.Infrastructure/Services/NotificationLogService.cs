using GoldfishReminder.Application;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

// 通知紀錄服務實作
public class NotificationLogService : INotificationLogService
{
    private readonly AppDbContext dbContext;

    public NotificationLogService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    // 檢查今天是否已送出
    public async Task<bool> HasSentTodayAsync(Guid userId, string notificationType, Guid targetId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        TaiwanClock.GetDayRange(nowUtc, out var utcStart, out var utcEnd);

        return await dbContext.NotificationLogs
            .AsNoTracking()
            .AnyAsync(
                x => x.UserId == userId
                     && x.NotificationType == notificationType
                     && x.TargetId == targetId
                     && x.Status == "success"
                     && x.SentAt >= utcStart
                     && x.SentAt < utcEnd,
                cancellationToken);
    }

    // 新增通知紀錄
    public async Task AddAsync(Guid userId, string notificationType, Guid targetId, string messageContent, DateTimeOffset sentAtUtc, CancellationToken cancellationToken = default)
    {
        var notificationLog = new NotificationLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NotificationType = notificationType,
            TargetId = targetId,
            MessageContent = messageContent,
            SentAt = sentAtUtc.ToUniversalTime(),
            Status = "success"
        };

        dbContext.NotificationLogs.Add(notificationLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
