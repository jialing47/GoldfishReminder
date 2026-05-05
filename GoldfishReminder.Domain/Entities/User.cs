namespace GoldfishReminder.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DiscordUserId { get; set; }
    public string? DiscordPrivateChannelId { get; set; }
}
