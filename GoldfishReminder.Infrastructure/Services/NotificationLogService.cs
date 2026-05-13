using GoldfishReminder.Application;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

// 通知紀錄服務實作
public class NotificationLogService : INotificationLogService
{
    private const int MaxErrorMessageLength = 500; // 失敗訊息存入 DB 前截斷上限 避免長 stack trace 撐爆 row

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

    // 新增成功通知紀錄
    public async Task AddAsync(Guid userId, string notificationType, Guid targetId, string messageContent, DateTimeOffset sentAtUtc, CancellationToken cancellationToken = default)
    {
        var notificationLog = BuildLog(userId, notificationType, targetId, messageContent, sentAtUtc, "success", null);
        dbContext.NotificationLogs.Add(notificationLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // 新增失敗通知紀錄 errorMessage 截斷至 500 字 避免極長 stack trace 撐爆 row
    public async Task AddFailureAsync(Guid userId, string notificationType, Guid targetId, string messageContent, string errorMessage, DateTimeOffset sentAtUtc, CancellationToken cancellationToken = default)
    {
        var truncated = errorMessage; // 截斷後的錯誤訊息
        if (truncated == null)
        {
            truncated = string.Empty;
        }
        if (truncated.Length > MaxErrorMessageLength)
        {
            truncated = truncated.Substring(0, MaxErrorMessageLength);
        }

        var notificationLog = BuildLog(userId, notificationType, targetId, messageContent, sentAtUtc, "fail", truncated);
        dbContext.NotificationLogs.Add(notificationLog);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // 建立通知紀錄 entity 抽共用避免兩個 Add 路徑分歧
    private static NotificationLog BuildLog(Guid userId, string notificationType, Guid targetId, string messageContent, DateTimeOffset sentAtUtc, string status, string? errorMessage)
    {
        return new NotificationLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            NotificationType = notificationType,
            TargetId = targetId,
            MessageContent = messageContent,
            SentAt = sentAtUtc.ToUniversalTime(),
            Status = status,
            ErrorMessage = errorMessage
        };
    }
}
