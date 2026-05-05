using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace GoldfishReminder.Api.Background;

public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> channel;

    public BackgroundTaskQueue()
    {
        channel = Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    }

    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (workItem == null)
        {
            throw new ArgumentNullException(nameof(workItem));
        }

        channel.Writer.TryWrite(workItem);
    }

    public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        var workItem = await channel.Reader.ReadAsync(cancellationToken);
        return workItem;
    }
}