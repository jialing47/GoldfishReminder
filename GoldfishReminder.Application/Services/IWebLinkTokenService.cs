namespace GoldfishReminder.Application.Services;

//Web 連結 token 服務介面
public interface IWebLinkTokenService
{
    Task<WebLinkTokenResult> CreateOrRotateAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default);
    Task<TokenConsumeResult> ConsumeAsync(string token, CancellationToken cancellationToken = default);
}

//Web 連結 token 建立結果
public class WebLinkTokenResult
{
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

//Web 連結 token 使用結果
public class TokenConsumeResult
{
    public Guid UserId { get; set; }
}