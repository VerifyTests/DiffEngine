class AsyncTimer :
    IAsyncDisposable
{
    Func<CancellationToken, Task> callback;
    TimeSpan interval;
    Action<Exception> errorCallback;
    Func<TimeSpan, CancellationToken, Task> delayStrategy;
    Task task;
    CancellationTokenSource tokenSource = new();

    public AsyncTimer(
        Func<CancellationToken, Task> callback,
        TimeSpan interval,
        Action<Exception>? errorCallback = null,
        Func<TimeSpan, CancellationToken, Task>? delayStrategy = null)
    {
        this.callback = callback;
        this.interval = interval;
        this.errorCallback = errorCallback ?? (_ =>
        {
        });
        this.delayStrategy = delayStrategy ?? Task.Delay;
        var cancellation = tokenSource.Token;

        task = Task.Run(
            async () =>
            {
                await RunLoop(cancellation);
            },
            cancellation);
    }

    async Task RunLoop(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await delayStrategy(interval, cancellation);
                await callback(cancellation);
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