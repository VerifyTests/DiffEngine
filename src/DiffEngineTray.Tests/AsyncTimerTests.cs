[TestFixture]
public class AsyncTimerTests
{
    [Test]
    public async Task It_calls_error_callback()
    {
        var errorCallbackInvoked = new TaskCompletionSource<bool>();

        await using var timer = new AsyncTimer(
            callback: _ => throw new("Simulated!"),
            interval: TimeSpan.Zero,
            errorCallback: _ =>
            {
                errorCallbackInvoked.TrySetResult(true);
            });

        True(await errorCallbackInvoked.Task);
    }

    [Test]
    public async Task It_continues_to_run_after_an_error()
    {
        var callbackInvokedAfterError = new TaskCompletionSource<bool>();

        var fail = true;
        var exceptionThrown = false;
        await using var timer = new AsyncTimer(
            callback: _ =>
            {
                if (fail)
                {
                    fail = false;
                    throw new("Simulated!");
                }

                True(exceptionThrown);
                callbackInvokedAfterError.TrySetResult(true);
                return Task.FromResult(0);
            },
            interval: TimeSpan.Zero,
            errorCallback: _ =>
            {
                exceptionThrown = true;
            });

        True(await callbackInvokedAfterError.Task);
    }

    [Test]
    public async Task Stop_cancels_token_while_in_callback()
    {
        var callbackCanceled = false;
        var callbackStarted = new TaskCompletionSource<bool>();
        var stopInitiated = new TaskCompletionSource<bool>();
        var timer = new AsyncTimer(
            callback: async token =>
            {
                callbackStarted.SetResult(true);
                await stopInitiated.Task;
                if (token.IsCancellationRequested)
                {
                    callbackCanceled = true;
                }
            },
            interval: TimeSpan.Zero);

        await callbackStarted.Task;
        var stopTask = timer.DisposeAsync();
        stopInitiated.SetResult(true);
        await stopTask;
        True(callbackCanceled);
    }

    [Test]
    public async Task Stop_waits_for_callback_to_complete()
    {
        var callbackCompleted = new TaskCompletionSource<bool>();
        var callbackTaskStarted = new TaskCompletionSource<bool>();
        var timer = new AsyncTimer(
            callback: _ =>
            {
                callbackTaskStarted.SetResult(true);
                return callbackCompleted.Task;
            },
            interval: TimeSpan.Zero);

        await callbackTaskStarted.Task;

        var stopTask = timer.DisposeAsync().AsTask();
        var delayTask = Task.Delay(1000);

        var firstToComplete = await Task.WhenAny(stopTask, delayTask);
        AreEqual(delayTask, firstToComplete);
        callbackCompleted.SetResult(true);

        await stopTask;
    }
}