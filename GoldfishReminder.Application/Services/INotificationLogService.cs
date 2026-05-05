namespace GoldfishReminder.Application.Services;

//通知紀錄介面
public interface INotificationLogService
{
    Task<bool> HasSentTodayAsync(Guid userId, string notificationType, Guid targetId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
    Task AddAsync(Guid userId, string notificationType, Guid targetId, string messageContent, DateTimeOffset sentAtUtc, CancellationToken cancellationToken = default);
}