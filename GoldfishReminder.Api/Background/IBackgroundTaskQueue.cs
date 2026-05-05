using System;
using System.Threading;
using System.Threading.Tasks;

namespace GoldfishReminder.Api.Background;

public interface IBackgroundTaskQueue
{
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem);
    Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}