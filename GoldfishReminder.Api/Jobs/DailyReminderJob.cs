using GoldfishReminder.Application.Workflows;
using GoldfishReminder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldfishReminder.Api.Jobs;

// 每日提醒 job
public class DailyReminderJob
{
    private const int WebLinkTokenRetentionDays = 30;     // WebLinkToken row 保留天數 超過即刪
    private const int NotificationLogRetentionDays = 90;  // NotificationLog row 保留天數 超過即刪

    private readonly CreditBillWorkflow creditBillWorkflow;
    private readonly AppDbContext dbContext;

    public DailyReminderJob(CreditBillWorkflow creditBillWorkflow, AppDbContext dbContext)
    {
        this.creditBillWorkflow = creditBillWorkflow;
        this.dbContext = dbContext;
    }

    // 執行每日提醒並順便清理過期資料
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await creditBillWorkflow.RunDailyReminderAsync(cancellationToken);
        await CleanupTokensAsync(cancellationToken);
        await CleanupLogsAsync(cancellationToken);
    }

    // 清除指定天數前已過期或已使用的 web link token
    private async Task CleanupTokensAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-WebLinkTokenRetentionDays);

        await dbContext.WebLinkTokens
            .Where(x => x.ExpiresAt < threshold || (x.UsedAt != null && x.UsedAt < threshold))
            .ExecuteDeleteAsync(cancellationToken);
    }

    // 清除指定天數前的通知記錄 避免無限累積
    private async Task CleanupLogsAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow.AddDays(-NotificationLogRetentionDays);

        await dbContext.NotificationLogs
            .Where(x => x.SentAt < threshold)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
