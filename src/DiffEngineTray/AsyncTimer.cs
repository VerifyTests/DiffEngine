using System;
using System.Threading;
using System.Threading.Tasks;

class AsyncTimer :
    IAsyncDisposable
{
    Func<DateTime, CancellationToken, Task> callback;
    TimeSpan interval;
    Action<Exception> errorCallback;
    Func<TimeSpan, CancellationToken, Task> delayStrategy;
    Task task;
    CancellationTokenSource tokenSource = new CancellationTokenSource();

    public AsyncTimer(
        Func<DateTime, CancellationToken, Task> callback,
        TimeSpan interval,
        Action<Exception>? errorCallback = null,
        Func<TimeSpan, CancellationToken, Task>? delayStrategy = null)
    {
        this.callback = callback;
        this.interval = interval;
        this.errorCallback = errorCallback ?? (exception => { });
        this.delayStrategy = delayStrategy ?? Task.Delay;
        var cancellation = tokenSource.Token;

        task = Task.Run(async () => { await RunLoop(cancellation); }, cancellation);
    }

    async Task RunLoop(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var utcNow = DateTime.UtcNow;
                await delayStrategy(interval, cancellation);
                await callback(utcNow, cancellation);
            }
            catch (OperationCanceledException)
            {
                // noop
            }
            catch (Exception exception)
            {
                errorCallback(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        tokenSource.Cancel();
        tokenSource.Dispose();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}