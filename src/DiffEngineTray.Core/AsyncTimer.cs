class AsyncTimer :
    IAsyncDisposable
{
    Func<Cancel, Task> callback;
    TimeSpan interval;
    Action<Exception> errorCallback;
    Task task;
    CancelSource tokenSource = new();

    public AsyncTimer(
        Func<Cancel, Task> callback,
        TimeSpan interval,
        Action<Exception>? errorCallback = null)
    {
        this.callback = callback;
        this.interval = interval;
        this.errorCallback = errorCallback ?? (_ =>
        {
        });
        var cancel = tokenSource.Token;

        task = Task.Run(() => RunLoop(cancel), cancel);
    }

    async Task RunLoop(Cancel cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancel);
                await callback(cancel);
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
        await tokenSource.CancelAsync();
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