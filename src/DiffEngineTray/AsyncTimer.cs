using System;
using System.Threading;
using System.Threading.Tasks;

class AsyncTimer
{
    Func<DateTime, CancellationToken, Task> callback;
    TimeSpan interval;
    Action<Exception> errorCallback;
    Func<TimeSpan, CancellationToken, Task> delayStrategy;

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
    }

    public virtual void Start()
    {
        tokenSource = new CancellationTokenSource();
        var cancellation = tokenSource.Token;

        task = Task.Run(async () =>
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
                    catch (Exception ex)
                    {
                        errorCallback(ex);
                    }
                }
            },
            cancellation);
    }

    public virtual Task Stop()
    {
        if (tokenSource == null)
        {
            return Task.FromResult(0);
        }

        tokenSource.Cancel();
        tokenSource.Dispose();

        return task ?? Task.FromResult(0);
    }

    Task? task;
    CancellationTokenSource? tokenSource;
}