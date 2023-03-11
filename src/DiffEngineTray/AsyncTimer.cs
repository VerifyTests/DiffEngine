class AsyncTimer :
    IAsyncDisposable
{
    Func<CancellationToken, Task> callback;
    TimeSpan interval;
    Action<Exception> errorCallback;
    Task task;
    CancellationTokenSource tokenSource = new();

    public AsyncTimer(
        Func<CancellationToken, Task> callback,
        TimeSpan interval,
        Action<Exception>? errorCallback = null)
    {
        this.callback = callback;
        this.interval = interval;
        this.errorCallback = errorCallback ?? (_ =>
        {
        });
        var cancellation = tokenSource.Token;

        task = Task.Run(() => RunLoop(cancellation), cancellation);
    }

    async Task RunLoop(Cancellation cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellation);
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