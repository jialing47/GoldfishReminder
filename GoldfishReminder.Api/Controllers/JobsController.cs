using System.Security.Cryptography;
using System.Text;
using GoldfishReminder.Api.Background;
using GoldfishReminder.Api.Jobs;
using GoldfishReminder.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GoldfishReminder.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private const string AuthHeaderName = "Authorization";
    private const string BearerPrefix = "Bearer ";

    private readonly IBackgroundTaskQueue taskQueue;
    private readonly JobsOptions jobsOptions;

    public JobsController(IBackgroundTaskQueue taskQueue, IOptions<JobsOptions> jobsOptions)
    {
        this.taskQueue = taskQueue;
        this.jobsOptions = jobsOptions.Value;
    }

    // 手動觸發每日提醒 立即回應 實際工作丟背景佇列執行 配合 GCP Cloud Scheduler 觸發
    [HttpPost("daily-reminder")]
    public IActionResult RunDailyReminder()
    {
        if (!IsAuthorized())
        {
            return Unauthorized();
        }

        taskQueue.Enqueue(async (serviceProvider, stoppingToken) =>
        {
            var job = serviceProvider.GetRequiredService<DailyReminderJob>();
            await job.RunAsync(stoppingToken);
        });

        return Accepted(new
        {
            message = "Daily reminder job enqueued."
        });
    }

    // 驗證 Authorization Bearer token 與設定一致 設定未填視為全部拒絕避免無意暴露
    private bool IsAuthorized()
    {
        if (string.IsNullOrWhiteSpace(jobsOptions.AuthToken))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(AuthHeaderName, out var header))
        {
            return false;
        }

        var value = header.ToString();

        if (!value.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var token = value[BearerPrefix.Length..];

        // 用 constant-time 比對避免 timing attack 量測 token 前幾字對到幾個
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var authTokenBytes = Encoding.UTF8.GetBytes(jobsOptions.AuthToken);
        return CryptographicOperations.FixedTimeEquals(tokenBytes, authTokenBytes);
    }
}
