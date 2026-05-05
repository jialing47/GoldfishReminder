namespace GoldfishReminder.Domain.Entities;

public class NotificationLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public string NotificationType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string MessageContent { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // success / fail
    public string? ErrorMessage { get; set; }
    public DateTimeOffset SentAt { get; set; }

}