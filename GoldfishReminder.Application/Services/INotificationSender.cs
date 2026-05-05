namespace GoldfishReminder.Application.Services;

//通知發送介面
public interface INotificationSender
{
    Task SendAsync(string recipientChannelId, string messageContent, object[]? components = null, CancellationToken cancellationToken = default);
}
