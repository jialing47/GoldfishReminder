namespace GoldfishReminder.Application.Models;

// 新增或更新使用者請求
public class UpsertUserRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DiscordUserId { get; set; }
    public string? DiscordPrivateChannelId { get; set; }
}
