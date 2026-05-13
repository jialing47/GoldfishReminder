using System.Security.Cryptography;
using System.Text;
using GoldfishReminder.Application.Services;
using GoldfishReminder.Domain.Entities;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Infrastructure.Services;

//Web 連結 token 服務實作
public class WebLinkTokenService : IWebLinkTokenService
{
    private readonly AppDbContext dbContext;

    public WebLinkTokenService(AppDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    //建立或輪替 token
    public async Task<WebLinkTokenResult> CreateOrRotateAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required");
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(ttl);
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);
        var tokenHash = Sha256Hex(token);

        var activeTokens = await dbContext.WebLinkTokens
            .Where(x => x.UserId == userId && x.UsedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var activeToken in activeTokens)
        {
            activeToken.UsedAt = now;
        }

        dbContext.WebLinkTokens.Add(new WebLinkToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            UsedAt = null,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new WebLinkTokenResult
        {
            Token = token,
            ExpiresAt = expiresAt
        };
    }

    //使用 token
    public async Task<TokenConsumeResult> ConsumeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required");
        }

        var tokenHash = Sha256Hex(token.Trim());
        var now = DateTimeOffset.UtcNow;

        var row = await dbContext.WebLinkTokens
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > now, cancellationToken);

        if (row == null)
        {
            throw new InvalidOperationException("Token is invalid or expired");
        }

        row.UsedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TokenConsumeResult
        {
            UserId = row.UserId
        };
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}