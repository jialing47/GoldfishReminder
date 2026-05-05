using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GoldfishReminder.Api.Background;

// 背景工作 hosted service 從 queue 取出工作並在獨立 scope 中執行
// 服務對象包含 Discord onboarding 與 daily reminder job
public class TaskQueueHostedService : BackgroundService
{
    private readonly IBackgroundTaskQueue taskQueue;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<TaskQueueHostedService> logger;

    public TaskQueueHostedService(IBackgroundTaskQueue taskQueue, IServiceScopeFactory scopeFactory, ILogger<TaskQueueHostedService> logger)
    {
        this.taskQueue = taskQueue;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    // 持續從 queue 取工作直到取消
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await taskQueue.DequeueAsync(stoppingToken);

            using var scope = scopeFactory.CreateScope();
            try
            {
                await workItem(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                // 吞掉避免 BackgroundService 停掉 透過 logger 讓錯誤寫入 host 的 log 系統供事後查看
                logger.LogError(ex, "Background task failed");
            }
        }
    }
}
